# 1.0.1 · First public release

Standalone Yamaha R-N301 receiver control for Windows + Loupedeck CT.

- Eight named NET RADIO/YCast favorites and the receiver's actual FM presets.
- Power, mute, all detected inputs and source-specific media transport.
- Live volume/source/station dial feedback with immediate pending-volume display.
- Custom full 240px now-playing wheel, amber volume arc and violet muted state.
- White, unboxed station icons with amber active-state accents; warm functional colors.
- Contextual physical previous/next arrows with normal-arrow fallback elsewhere.
- Editable C# and Lucide SVGs, build/profile tools, setup and rollback documentation.

Requires .NET 10-compatible Logi/Loupedeck software; developed against API 6.4.2.3414. Read [README.md](README.md) before installing. The plugin package contains no personal settings or Windows profiles. Configure your own receiver host, device identity, favorites and workspace ID.

The reference profile merge helper is specific to the author's existing two-workspace configuration; new users should follow the manual setup guide. Spotify/AirPlay/SERVER transport with active media and a physical DHCP change remain manual verification items. See [TESTING.md](TESTING.md).
