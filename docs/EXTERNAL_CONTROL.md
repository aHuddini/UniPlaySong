# UniPlaySong External Control — URI Command Reference

Control UniPlaySong playback via Playnite's `playnite://` URI protocol. Works from any tool that can open a URL — no setup required.

## Commands

| Command | URI | Description |
|---------|-----|-------------|
| Play / Resume | `playnite://uniplaysong/play` | Resume playback. No-op if already playing. |
| Pause | `playnite://uniplaysong/pause` | Pause playback. No-op if already paused. |
| Toggle Play/Pause | `playnite://uniplaysong/playpausetoggle` | Pauses if playing, resumes if paused. |
| Skip | `playnite://uniplaysong/skip` | Skip to the next song. |
| Restart | `playnite://uniplaysong/restart` | Restart the current song from the beginning. |
| Stop | `playnite://uniplaysong/stop` | Stop playback entirely. |
| Set Volume | `playnite://uniplaysong/volume/{0-100}` | Set volume to a value between 0 and 100. |

## Usage Examples

- **Stream Deck** — Set a button's action to "Open URL" and paste the URI
- **AutoHotkey** — `Run, playnite://uniplaysong/pause`
- **PowerShell** — `Start-Process "playnite://uniplaysong/skip"`
- **Windows Run (Win+R)** — Type the URI and press Enter
- **Desktop Shortcut** — Right-click desktop > New Shortcut > paste the URI as the target
- **Batch File** — `start playnite://uniplaysong/volume/50`
- **Playnite Themes** — See [Theme Integration Guide](dev_docs/THEME_INTEGRATION_GUIDE.md)
