# Yamaha control reference

Windows default profile → round button **3** → **Yamaha**.

## Touchscreen

| Column 1 | Column 2 | Column 3 | Column 4 |
|---|---|---|---|
| Toide | Vaigista / Heli tagasi | Sisendid | Meedia |
| Sky Plus | Raadio 2 | Raadio Kuku | Vikerraadio |
| Power Hit Radio | Raadio Elmar | NRJ | Star FM |

The example above is the **NET RADIO** view. The top row stays fixed; the lower eight buttons follow the **confirmed active receiver input**, including changes from HA or the remote. Turning L2 without pressing it only previews an input and does not change these buttons.

| Active input | Second row, left → right | Third row, left → right | Meedia menu |
|---|---|---|---|
| NET RADIO | First four configured favorites | Last four configured favorites | Previous, play/stop, next, stop |
| TUNER | First four stored FM presets | Next four stored FM presets | All actual stored FM presets |
| SERVER | Previous, play/pause, next, stop | Shuffle, repeat, blank, blank | Same six controls |
| Spotify / AirPlay | Previous, play/pause, next, stop | Four blank buttons | Same four controls |
| CD / OPTICAL / COAXIAL / LINE | Four blank buttons | Four blank buttons | Does not open |
| Disconnected | Four blank buttons | Four blank buttons | Does not open |

For manual assignment, use **Yamaha · aktiivne sisend → Sisendinupp 1–8** for the eight lower positions, in row-major order. The names and icons change at runtime. Blank slots are black and do nothing. FM buttons display their frequency, use actual receiver preset IDs and highlight the selected preset; fewer than eight presets leave unused slots blank. The Meedia list can show any additional presets beyond the eight main-page positions.

FM captions learn the receiver's stable RDS **Program Service** name during normal listening and remember it locally by frequency. Until learned, a preset retains its frequency caption. Both the main page and Meedia use the learned name; the icon still shows frequency. There is no automatic scan/retuning. On the wheel, the station name appears above current **Radio Text A/B**, when supplied by the station. See [RDS details and cache management](README.md#automatic-fm-names-and-rds-text).

Favorite actions power on if necessary, navigate the existing YCast Bookmarks by name, revalidate the menu position, and confirm playback. Radio favorites and FM presets remain usable to wake standby; other media controls disappear in standby. Input selection shows all 11 inputs read from the receiver and highlights the active input. A press from an outdated source view is cancelled after checking the receiver, rather than switching inputs or executing an unrelated command.

The original fixed **station** actions are still available for users who intentionally want permanent NET RADIO shortcuts; the reference main page now uses the contextual slots instead. Spotify/AirPlay/SERVER commands still require testing with real active playback.

## Dials and wheel

| Control | Rotate | Press / touch | Feedback |
|---|---|---|---|
| L1, upper left | Yamaha volume ±1 | Mute | Immediate numeric target, confirmed receiver volume; Mute/Off/Offline |
| L2, middle left | Preview input | Apply preview | Short input name; arrow prefix while previewing |
| L3, lower left | Previous/next station, preset or track | NET RADIO/media: toggle playback; TUNER: open FM preset list | Short current station name |
| R1, upper right | Windows volume | Windows mute | Existing Windows feedback |
| R2, middle right | Display brightness | Existing brightness reset | Existing control retained |
| R3, lower right | Windows desktop switching | Existing close-desktop action | Existing control retained |
| Large wheel | Yamaha volume ±1 | Center: play/stop/pause; lower left/right: previous/next | Full-screen now-playing view with 270° volume arc |
| Physical left/right arrows | — | Yamaha: previous/next; other System workspaces: ordinary arrows | Existing Fn Home/End kept |

NET RADIO: previous/next wrap the eight configured favorites. TUNER: navigate actual stored preset IDs. SERVER/Spotify/AirPlay: use supported track transport. Passive inputs such as LINE/OPTICAL do not have media transport.

The **Sisendid** list and L2 preview both start with **NET RADIO, TUNER (FM)**, followed by all other sources in their original receiver order. From NET RADIO, turn L2 one step forward for FM; from FM, one step backward for NET RADIO. Press L2 to apply the preview; turning alone still does not change the playing input.

## Visual language

Black background, unboxed Lucide geometry. Power is green, mute/volume amber, inputs violet, media/track rose. Radio and FM preset icons are white; an active station has an amber glow and dot. The wheel uses warm cream main text, muted lavender secondary text, an amber/coral volume arc, and a violet arc while muted. The number remains beside L1 rather than duplicating it in the wheel center.

The power tile includes a readable state caption: green **SEES**, gray **OOTEL**, or red **ÜHENDUS PUUDUB**. The same state appears at the top of the wheel. These indicate reported receiver power/availability, independently of whether media is currently playing.

The two original Windows workspaces and their touch/encoder/wheel pages are retained. Shared square-button left/right presses now route by the active workspace; all their Fn mappings and other square buttons remain unchanged. Other application profiles are not edited.
