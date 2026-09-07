# UniPlaySong External Control — URI Command Reference

Control UniPlaySong playback via Playnite's `playnite://` URI protocol. Works from any tool that can open a URL — no setup required.

## Commands

| Command | URI | Description |
|---------|-----|-------------|
| Play / Resume | `playnite://uniplaysong/play` | Resume playback. No-op if already playing. |
| Pause | `playnite://uniplaysong/pause` | Pause playback. No-op if already paused. |
| Toggle Play/Pause | `playnite://uniplaysong/playpausetoggle` | Pauses if playing, resumes if paused. |
| Skip / Next | `playnite://uniplaysong/skip` (alias: `next`) | Skip to the next song. |
| Previous | `playnite://uniplaysong/previous` | Previous track (restarts the current song for UPS). |
| Toggle Mute | `playnite://uniplaysong/togglemute` | Mute/unmute UPS music. Unmute restores the pre-mute volume. |
| Restart | `playnite://uniplaysong/restart` | Restart the current song from the beginning. |
| Stop | `playnite://uniplaysong/stop` | Stop playback entirely. |
| Set Volume | `playnite://uniplaysong/volume/{0-100}` | Set volume to a value between 0 and 100. |

## Sound events (for other extensions)

These play a notification sound rather than controlling playback. They are namespaced under the
calling plugin so each can add events later without a second convention, and each no-ops silently
when its sound setting is off — so calling them is safe whether or not the user has them enabled.

| Event | URI | Description |
|---|---|---|
| Achievement unlocked | `playnite://uniplaysong/playniteachievements/{tier}` | Plays the sound for that rarity tier. `{tier}` is one of `commonachievement`, `uncommonachievement`, `rareachievement`, `ultrarareachievement`, `capstoneachievement`, `hidden`. An unknown tier falls back to the master achievement sound. |
| Controller detected | `playnite://uniplaysong/controlup/detecttrigger` | Plays the controller-detected sound. An unknown or missing event segment no-ops rather than playing the wrong sound. |

Both play on a dedicated lightweight player, so they work over a running game without disturbing
music playback. Loudness is the user's `JingleVolume`, independent of their Music Volume.

Full integration notes, including how the per-rarity sound packs resolve and how to ask UniPlaySong
which sound *would* play: [ACHIEVEMENT_SOUND_INTEGRATION.md](dev_docs/features/ACHIEVEMENT_SOUND_INTEGRATION.md).

## Usage Examples

- **Stream Deck** — Set a button's action to "Open URL" and paste the URI
- **AutoHotkey** — `Run, playnite://uniplaysong/pause`
- **PowerShell** — `Start-Process "playnite://uniplaysong/skip"`
- **Windows Run (Win+R)** — Type the URI and press Enter
- **Desktop Shortcut** — Right-click desktop > New Shortcut > paste the URI as the target
- **Batch File** — `start playnite://uniplaysong/volume/50`
- **Playnite Themes** — See [Theme Integration Guide](dev_docs/Theme%20Support/THEME_INTEGRATION_GUIDE.md)
