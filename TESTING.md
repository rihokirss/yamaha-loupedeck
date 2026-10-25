# Verification record · 2026-10-24

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
