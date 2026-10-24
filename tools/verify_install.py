"""Read-only installed-profile audit, plus private machine-readable evidence."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
from prepare_profile import APP, SERVICE, ROOT, ident, ARROWS, arrow


def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def tree(root): return {p.relative_to(root).as_posix(): digest(p) for p in root.rglob('*') if p.is_file()}
def normalize(profile):
    profile = copy.deepcopy(profile)
    mode = next(m for m in profile['layout']['layoutModes'] if m['modeName'] == 'System')
    for field, name in [('touchPages','touch'),('encoderPages','encoders'),('wheelPages','wheel'),('workspaces','workspace')]:
        mode[field] = [p for p in mode[field] if p['name'] != ident(name)]
    profile['additionalNativePluginNames'] = [n for n in profile['additionalNativePluginNames'] if n != 'YamahaDirect']
    profile['layout']['roundPage']['controls'][2]['pressAction'] = None
    for index, (_, old) in ARROWS.items(): profile['layout']['squarePage']['controls'][index]['pressAction'] = old
    return profile


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--baseline', type=Path, required=True)
    args = parser.parse_args(); backup = args.baseline.resolve()
    app = json.loads((APP/'ApplicationInfo.json').read_text(encoding='utf-8-sig'))
    pid = app['defaultProfileName']; profile_dir = APP/'Profiles'/pid
    read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
    profile = read(profile_dir/'ProfileInfo.json'); baseline = read(backup/'System/Profiles'/pid/'ProfileInfo.json')
    assert normalize(profile) == normalize(baseline), 'An unrelated System setting changed'
    codex = SERVICE/'Applications/Loupedeck20/chatgpt'
    before, after = tree(backup/'Codex'), tree(codex)
    differences = sorted(k for k in set(before)|set(after) if before.get(k) != after.get(k))
    assert not differences, f'Codex files changed: {differences}'
    assert digest(APP/'ApplicationInfo.json') == digest(backup/'System/ApplicationInfo.json'), 'System application metadata changed'
    mode = next(m for m in profile['layout']['layoutModes'] if m['modeName'] == 'System')
    assert len(mode['workspaces']) == 3
    for index, (direction, _) in ARROWS.items():
        assert profile['layout']['squarePage']['controls'][index]['pressAction'] == arrow(direction)
    wheel = next(p for p in mode['wheelPages'] if p['name'] == ident('wheel'))
    assert wheel['templateName'] == 'YamahaDirect___WheelToolYamahaNowPlaying'
    installed = SERVICE/'Plugins/YamahaDirect/bin'
    built = ROOT/'Yamaha.Loupedeck/bin/Release'
    for name in ['YamahaDirectPlugin.dll','Yamaha.Core.dll']:
        assert digest(installed/name) == digest(built/name), f'Installed binary differs: {name}'
    report = {'baseline': str(backup), 'system_unrelated_settings_preserved': True,
              'codex_files_identical': len(before), 'workspaces': [p['displayName'] for p in mode['workspaces']],
              'arrow_routes': True, 'full_wheel_template': wheel['templateName'], 'installed_binaries_match': True}
    output = ROOT/'.local/verification.json'; output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(report, indent=2))

if __name__ == '__main__': main()
