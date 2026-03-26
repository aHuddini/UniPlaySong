# UniPlaySong v1.3.8 Release Notes

### Playback Fix
- **OGG Vorbis Support**: OGG files are now properly recognized and play on both SDL2 and NAudio backends. Added native OGG decoding via NVorbis — no Windows codec pack required.

### Settings Fix
- **Open Log Folder Button**: No longer crashes on non-standard Playnite installs (portable, `AppData\Local`). Uses actual extension location instead of hardcoded path.

### Critical Fix — Theme Compatibility
- **Music Blocked on Startup**: Certain themes set internal overlay flags during app startup that permanently blocked UPS from automatically playing music. Users had to manually press play every time. These flags are now reset on every launch to ensure music starts automatically regardless of theme.

### Improved
- **External Audio Detection**: Wallpaper Engine processes (`wallpaper64`, `wallpaper32`, `webwallpaper32`) now excluded by default under "Pause on External Audio" setting. Previously, Wallpaper Engine's persistent audio output caused constant pause/resume cycling. Existing users receive the new exclusions automatically on update.
- **Thread Safety**: Improved thread safety for external audio detection state during fullscreen/desktop mode switches.

### New Dependency
- `NVorbis.dll` (79 KB) — pure managed OGG Vorbis decoder. No native binaries.
