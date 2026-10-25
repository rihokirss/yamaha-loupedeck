# Verification record · 2026-10-24

## Adjacent radio inputs · 2026-10-25 (1.2.1)

- The shared input list now starts with NET RADIO and TUNER (FM), followed by all other inputs in their original receiver order. Both the Sisendid folder and L2 preview/apply use that same list. Input IDs/API tags are unchanged. One forward step from NET RADIO previews FM; one backward step from FM previews NET RADIO. The existing press-to-apply behavior is retained.
- Core suite passed **60/60** with the fake receiver's list deliberately matching the real nonadjacent radio order. Tests assert radio adjacency, stable remaining order, preserved API identities and no input-selection PUT while sorting. The real SDK preview also exercises one-step forward/reverse radio preview without retuning. Release build: zero warnings/errors.
- A read-only query of the actual receiver confirmed all 11 inputs remain available in this order: NET RADIO, TUNER, Spotify, SERVER, AirPlay, CD, OPTICAL, COAXIAL, LINE1, LINE2, LINE3. No real input, volume or playback command was sent for verification.
- Pre-update backup: `backups/yamaha-plugin-update-20261025-135603/`. Plugin-only installation preserved CT application/profile files and the private settings/RDS cache; recovery profile and plugin archives were regenerated and ZIP-validated. Physical dial feel and input apply remain a manual check.

## FM RDS learning update · 2026-10-25 (1.2.0)

- TUNER metadata now uses normalized `Program_Service` and `Radio_Text_A`, with `Radio_Text_B` fallback when A is empty/placeholder-only. The full wheel receives the station name and live show/song text. Empty text clears the old program; untuned/unready signals do not expose old RDS metadata as live.
- FM button and Meedia captions learn stable PS names by canonical frequency, not preset ID. First frames/transient names are ignored; learning requires repeated observations, at least 3 seconds on the frequency and 2 seconds with the same name. It never sends a tuning/scan command. Frequencies remain in the icons and are used as labels until learned. Cached names are serialized in the existing private settings; settings writes now use a locked temporary-file replacement.
- Core suite passed **58/58**, covering A/B fallback, padding/accents, absent/untuned/AM metadata, frequency normalization, settle timing, stale PS after tuning, empty-data retention rules, copy-on-write cache snapshots, JSON persistence round-trip, preset reassignment, main/menu labels, learning notifications and no-PUT polling. Earlier context, menu-race and volume tests also passed.
- Release plugin build: zero warnings/errors. Real SDK preview checks learned names in main and menu captions, frequency icon retention, and complete visible station/program text across wrapped wheel lines. The generated 240px FM wheel was visually inspected.
- Fresh pre-update backup: `backups/yamaha-plugin-update-20261025-125725/`. Installed service reports **1.2.0 / Normal / Plugin successfully loaded**. DLL hashes match the tested build. **All 131 CT application/profile files remain byte-identical**; pre-existing private receiver settings are unchanged apart from the added learned-name cache.
- Real receiver, without retuning: current FM 95.4 MHz reported `SKYPLUS` and B-field text. The running plugin learned and persisted `95.4 → SKYPLUS`; the native icon response now labels the first FM button `SKYPLUS`. The native rendered wheel was visually checked and shows `SKYPLUS` plus the current `SKY PLUS - #1 Mu` broadcast text. Reception text can be partial/garbled at its source; no missing words are invented.
- No real receiver power, volume, mute, input or preset commands were sent by these checks. Other presets will acquire their names as the user listens. Physical reception-loss/re-tune transitions, other stations and a separate service-restart persistence rehearsal remain manual; the corresponding logic and serialization are simulated. A fresh private recovery LP4 and plugin package were ZIP-validated. No GitHub release was created by the installation.

## Contextual main-page update · 2026-10-25 (1.1.0)

- The lower eight touchscreen positions now follow the confirmed receiver input, not the L2 preview. NET RADIO uses configured favorites; TUNER uses actual stored preset IDs/frequencies; SERVER has six controls; Spotify/AirPlay have four. Passive sources, disconnected state and unused positions are black and inactive. Media folders expose only the current source's actions; FM presets appear only on TUNER.
- The core simulation suite passed **38/38** on this run, including stale-view cancellation after an external input change (no PUT requests), valid context station/preset selection, SERVER shuffle/repeat feedback, normalized FM frequencies, nonconsecutive preset IDs, standby behavior, and all earlier volume/menu safety tests.
- Release plugin build: **zero warnings, zero errors**. The SDK preview checks eight pre-load subscriptions, the existing immediate/dial feedback, and live caption/image getters on NET RADIO, TUNER, SERVER, Spotify, AirPlay and OPTICAL. FM frequency and SERVER control previews were visually inspected. Context previews are regenerated under `.tmp/context-previews/`, not published as personal device screenshots.
- Fresh backup: `backups/yamaha-plugin-update-20261025-104058/`. The opt-in migration changed exactly eight Yamaha main-page press bindings. A post-restart audit confirmed all other System profile content is semantically identical, **130 other CT application-profile files are byte-identical**, and both installed DLL hashes match the tested build. This comparison includes the current other-application profiles rather than an obsolete original-install snapshot.
- The installed service reports **1.1.0 / Normal / Plugin successfully loaded**; all eight contextual commands are registered. All eight live default icon responses contain the expected NET RADIO station captions in configured order (including `Raadio 2`), rather than the palette placeholders `Sisendinupp 1–8`.
- Receiver identity, current NET RADIO state and actual eight FM presets were read successfully. **No power, input, playback, mute or volume changes were sent to the real receiver during this update**, preserving current radio playback. Other-source transitions and physical button presses therefore remain manual checks. Real active Spotify/AirPlay/SERVER playback is not claimed as tested.
- Updated private recovery profile and portable plugin archives were generated and ZIP-validated. No GitHub release was created by the installation.

Read-only audit for this migration:

```powershell
python .\tools\verify_context_slots.py --baseline .\backups\yamaha-plugin-update-20261025-104058
```

Manual acceptance: select TUNER in **Sisendid** and check frequency labels/highlight, select SERVER or Spotify/AirPlay with active playback and check their controls, then select OPTICAL to confirm the eight slots are blank and Meedia does not open. Change the input via HA/remote too; allow the normal 2s visible/10s background poll. Merely turning L2 without applying it must not change the main-page content.

## Power-feedback update · 2026-10-25 (1.0.2)

- Power tile and wheel now explicitly distinguish green `SEES`, gray `OOTEL`, and red `ÜHENDUS PUUDUB`.
- Preview checks cover on, standby, offline, and offline-after-last-known-on states; both surfaces use the same labels/colors. Disconnected views hide stale media metadata, and startup no longer shows an on-state demo as live feedback.
- All six preview images were visually checked. The installed service reports `Normal`; its live power icon contains `SEES`, and its rendered wheel shows the same state. Installed DLL hash matches the tested build.
- Plugin-only updater created a complete plugin/settings/profile backup and verified every CT application-profile file stayed unchanged. No receiver power, volume, input or playback commands were sent during this update.
- The core suite passed **21/21** in an isolated rerun. An earlier run hit the existing volume-pump timing assertion under load; volume-pump code was not changed by this UI update. Keep that timing sensitivity in mind when rerunning tests on a busy machine.
- Standby/offline visuals were simulated; a physical remote-control power transition and cable-disconnect check remain manual so current radio playback was not interrupted.

## Automated checks

- Release plugin build: zero warnings, zero errors.
- `Yamaha.Tools test`: **21/21** simulated receiver checks, including volume limits, configured ceiling, no wake from volume in standby, delayed menu loading, station selection by name, menu race rejection, favorites/preset navigation, radio stop/resume, transport rejection on passive inputs, tick coalescing/rate limiting, serialized requests, outage queue discard, reconnect without replay, device identity filtering and Estonian metadata decoding.
- Immediate volume target/event is asserted **before the receiver round-trip**.
- `Yamaha.Preview`: real installed SDK callback bridge verifies all seven action/folder subscriptions before `Plugin.Load`, all three dial value notifications, the dedicated immediate-volume notification, and friendly station captions instead of `station0` etc.
- The native service returns the custom full-size wheel image successfully. The registered template is `YamahaDirect___WheelToolYamahaNowPlaying`; the installed plugin reports `Normal / Plugin successfully loaded`.
- `verify_install.py` compares all unrelated System content against the pre-install snapshot, checks the original setup's separately backed-up application-profile tree, the intended two contextual arrow bindings, custom wheel template and installed DLL hashes. Machine-specific results remain in `.local/verification.json`.

## Real receiver checks completed

Confirmed through the receiver's reported state, not by an audio listening test:

- Model/device identity and all 11 inputs; eight stored FM presets.
- Modest volume +1 and restoration, mute on/off, OPTICAL input and return to radio.
- Standby, and favorite selection waking the receiver.
- Each of all eight configured NET RADIO favorites.
- Each of all eight actual FM presets.
- Previous/next favorite and NET RADIO stop/resume.
- Original state was restored after these smoke tests. Later user-selected volume changes were preserved; the plugin does not force the old test volume.

The user confirmed the full-wheel design and working live feedback after installation. The final station-only visual adjustment changes the radio icons to white and retains amber active-state accents.

## Manual checks still applicable

- Physical left/right arrows on Yamaha, then normal arrows and Fn Home/End on each original System workspace. The routing and preserved bindings are checked in software; each physical press has not been independently observed.
- Each lower wheel touch target and dial press in both TUNER and NET RADIO modes; confirm the labels/values are readable on the actual panel.
- Spotify, AirPlay and SERVER transport with active playback; SERVER shuffle/repeat and resume after stop in the same folder.
- A real DHCP address change / unplug-and-reconnect scenario. Identity filtering and offline queue discard are simulated; physical network recovery has not been forced.
- Brightness/Windows audio/desktop controls are retained from existing mappings, not newly exercised to avoid disturbing the desktop.
- LP4 import/rollback on a separate installation: archives are validated, but the live profile has not been destructively replaced to rehearse recovery.
