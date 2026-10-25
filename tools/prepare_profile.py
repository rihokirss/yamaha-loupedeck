"""Stage a Yamaha workspace from the CURRENT System profile. Never writes the live store."""
from __future__ import annotations
import argparse
import copy
import json
import os
import shutil
import uuid
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SERVICE = Path(os.environ["LOCALAPPDATA"]) / "Logi/LogiPluginService"
APP = SERVICE / "Applications/Loupedeck20/@_defaultwin"
STAGE = ROOT / ".tmp/yamaha-profile"
NS = uuid.UUID("15ccfd0f-652d-47f0-a830-556a380ef7c5")

def ident(name): return uuid.uuid5(NS, name).hex.upper()
def typed(cls, **values): return {"$type": f"Loupedeck.Service.{cls}, LoupedeckService", **values}
def command(name): return "$YamahaDirect___Loupedeck.YamahaDirectPlugin.ReceiverCommand___" + name
def folder(name): return "$YamahaDirect___#DynamicFolder___DynamicFolder#Loupedeck.YamahaDirectPlugin." + name
def adjustment(name): return "$YamahaDirect___Loupedeck.YamahaDirectPlugin." + name

def context_slot(index): return "$YamahaDirect___Loupedeck.YamahaDirectPlugin.ContextSlotCommand___slot" + str(index)

ARROWS = {
    7: ("left", "$@Generic___@ProfileAction___F726ED144C7A4913864651E8B705E7F1"),
    10: ("right", "$@Generic___@ProfileAction___A2119887B34D44679C0B89DC06BAC9AA"),
}
def arrow(name): return "$YamahaDirect___Loupedeck.YamahaDirectPlugin.ContextArrowCommand___" + name
def button(action): return typed("ProfileLayoutButton", pressAction=action, fnPressAction=None)
def encoder(rotate, press): return typed("ProfileLayoutEncoder", pressAction=press, fnPressAction=None, rotateAction=rotate, fnRotateAction=None)

def build():
    app = json.loads((APP / "ApplicationInfo.json").read_text(encoding="utf-8-sig"))
    profile_dir = APP / "Profiles" / app["defaultProfileName"]
    original = json.loads((profile_dir / "ProfileInfo.json").read_text(encoding="utf-8-sig"))
    profile = copy.deepcopy(original)
    mode = next(m for m in profile["layout"]["layoutModes"] if m["modeName"] == "System")
    workspace_id = ident("workspace")
    prior = [w for w in mode["workspaces"] if w["name"] != workspace_id]
    if len(prior) != 2:
        raise RuntimeError("Expected exactly two pre-existing System workspaces; inspect before installing")
    old_round = profile["layout"]["roundPage"]["controls"][2]["pressAction"]
    expected_round = f"$@Generic___@ChangeWorkspace___System|{workspace_id}"
    if old_round not in (None, "", expected_round): raise RuntimeError("Round button 3 is already assigned")
    for index, (direction, old_action) in ARROWS.items():
        control = profile["layout"]["squarePage"]["controls"][index]
        if control["pressAction"] not in (old_action, arrow(direction)):
            raise RuntimeError(f"Square arrow {direction} has a custom mapping; inspect before replacing")
        control["pressAction"] = arrow(direction)
    for field, name in [("touchPages", "touch"), ("encoderPages", "encoders"), ("wheelPages", "wheel"), ("workspaces", "workspace")]:
        mode[field] = [p for p in mode[field] if p["name"] != ident(name)]
    page = typed("ProfileLayoutButtonPage", name=ident("touch"), displayName="Yamaha", description="Receiver and active-input controls",
        controls=[button(command("power")), button(command("mute")), button(folder("InputsFolder")), button(folder("MediaFolder"))] +
                 [button(context_slot(i)) for i in range(8)],
        dynamicPageName=None, dynamicPagePluginName=None, dynamicPageNumber=0)
    mode["touchPages"].append(page)
    old_encoders = mode["encoderPages"][0]["controls"]
    mode["encoderPages"].append(typed("ProfileLayoutEncoderPage", name=ident("encoders"), displayName="Yamaha + Windows", description=None,
        controls=[encoder(adjustment("VolumeAdjustment"), command("mute")), encoder(adjustment("SourceAdjustment"), command("source_apply")),
                  encoder(adjustment("TrackAdjustment"), folder("TrackPressFolder")), encoder("$DefaultWin___Volume", "$DefaultWin___ResetVolume"),
                  copy.deepcopy(old_encoders[4]), copy.deepcopy(old_encoders[5])], dynamicPageName=None, dynamicPagePluginName=None, dynamicPageNumber=0))
    mode["wheelPages"].append(typed("ProfileLayoutWheelPage", name=ident("wheel"), displayName="Yamaha Now Playing", description=None,
        templateName="YamahaDirect___WheelToolYamahaNowPlaying", parameters={"$type": "Loupedeck.StringDictionaryNoCase, PluginApi", "adjustment": adjustment("VolumeAdjustment"),
        "actions": ",".join(command(a) for a in ["previous", "play", "next"])}))
    mode["workspaces"].append(typed("ProfileLayoutWorkspace20", name=workspace_id, displayName="Yamaha", description="Yamaha R-N301 direct LAN control",
        touchPageNames=[ident("touch")], encoderPageNames=[ident("encoders")], wheelPageNames=[ident("wheel")], activationActions=[]))
    profile["layout"]["roundPage"]["controls"][2]["pressAction"] = expected_round
    if "YamahaDirect" not in profile["additionalNativePluginNames"]: profile["additionalNativePluginNames"].append("YamahaDirect")
    # Strict preservation: remove only our additions and compare the full original payload.
    check = copy.deepcopy(profile)
    cmode = next(m for m in check["layout"]["layoutModes"] if m["modeName"] == "System")
    for field, name in [("touchPages", "touch"), ("encoderPages", "encoders"), ("wheelPages", "wheel"), ("workspaces", "workspace")]:
        cmode[field] = [p for p in cmode[field] if p["name"] != ident(name)]
    baseline = copy.deepcopy(original)
    bmode = next(m for m in baseline["layout"]["layoutModes"] if m["modeName"] == "System")
    for field, name in [("touchPages", "touch"), ("encoderPages", "encoders"), ("wheelPages", "wheel"), ("workspaces", "workspace")]:
        bmode[field] = [p for p in bmode[field] if p["name"] != ident(name)]
    for p in (check, baseline):
        p["additionalNativePluginNames"] = [n for n in p["additionalNativePluginNames"] if n != "YamahaDirect"]
        p["layout"]["roundPage"]["controls"][2]["pressAction"] = None
        for index, (_, old_action) in ARROWS.items():
            p["layout"]["squarePage"]["controls"][index]["pressAction"] = old_action
    if check != baseline: raise AssertionError("Unrelated System configuration changed")
    STAGE.mkdir(parents=True, exist_ok=True)
    shutil.copytree(profile_dir, STAGE, dirs_exist_ok=True)
    shutil.copy2(APP / "ApplicationInfo.json", STAGE / "ApplicationInfo.json")
    (STAGE / "ProfileInfo.json").write_text(json.dumps(profile, ensure_ascii=False, indent=4)+"\n", encoding="utf-8")
    print(f"Staged {STAGE}; retained both existing workspaces, all old macros, settings and icons")

if __name__ == "__main__": build()
