# UniPlaySong Demo (Fullscreen)

A Fullscreen theme for trying UniPlaySong's theme features, and a working reference for theme developers. It is
Playnite's default Fullscreen theme with three pages added to the main view, opened from the bottom bar:

| Button | Page | UniPlaySong features |
|---|---|---|
| ♫ Visualizer | Now Playing around the music visualizer: a spinning cover you turn to seek, a seekable timeline, title and transport, and game music's recent tracks | `UPS_MusicVisualControl`, `UPS_SeekDisc`, `UPS_MediaControllerTransport`, `UPS_GameRecentTracks`, `ActiveMediaProgress` |
| ♫ Spotify | The same page while Spotify plays: a backdrop gradient in the album art's colours and Spotify's recently played | `NowPlayingArtColor1`/`NowPlayingArtColor2`, `SpotifyRecentTracks` |
| ▶ Player | The Now-Playing popup with a live spectrum on its seek bar, over whatever page is open | `UPS_MusicPlayerControlVisual` |

The game details view also shows the media controller and mini-player elements.

## Install

Needs Playnite 10 and UniPlaySong 1.9.0 or later.

1. Download `UniPlaySong_Demo_Fullscreen_1_9_0.pthm` from the
   [1.9.0 release](https://github.com/aHuddini/UniPlaySong/releases/tag/1.9.0) and open it (or drag it onto Playnite).
   Or copy this `UPSNowPlayingDemoFS` folder into `%AppData%\Playnite\Themes\Fullscreen\` (a portable Playnite:
   `Themes\Fullscreen\` beside `Playnite.FullscreenApp.exe`).
2. Restart Playnite, open Fullscreen mode, and pick **UniPlaySong Demo (Fullscreen)** under Settings → Appearance → Theme.
3. In UniPlaySong's settings, turn on **Live Effects**: the visualizer and the player's spectrum hear UniPlaySong's music
   through it. Without it the pages still work, with no visualizer.

## For theme developers

Everything here is ordinary theme XAML - copy what you need. `Views/Main.xaml` holds the three pages, each commented;
the [Theme Integration Guide](../../docs/dev_docs/Theme%20Support/THEME_INTEGRATION_GUIDE.md) explains every element and
binding. To try another visualizer style or palette, give the visualizer a `Tag`:

```xml
<ContentControl x:Name="UPS_MusicVisualControl" Tag="Style=GlowBars; Palette=Disc" ... />
```

Your own look: make it in the [VisualizerPrototyper](https://ahuddini.github.io/UniPlaySong/VisualizerPrototyper/) and
send it through a [Visualizer Style issue](https://github.com/aHuddini/UniPlaySong/issues/new?template=visualizer_style.yml).

## Licence

Based on Playnite's default Fullscreen theme, © Josef Nemec, MIT licence - see `LICENSE-Playnite.md`. The UniPlaySong
additions are under UniPlaySong's licence.
