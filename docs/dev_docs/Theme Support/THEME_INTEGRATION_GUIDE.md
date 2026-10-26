# UniPlaySong Theme Integration Guide

How to wire your Playnite Fullscreen theme into UniPlaySong's music playback.

**Special thanks to Mike Aniki** for guidance and testing on this.

---

## What you can do with UPS

| Goal | Tool |
|---|---|
| Pause UPS music while an overlay / video / login screen is up | `UPS_MusicControl` element |
| Keep UPS music playing during a **decorative/silent** theme video (don't let it pause the music) | Set the video's `IsMuted="True"` (or `Volume="0"`) — UPS ignores muted video automatically |
| Swap game music for the user's default music while a custom panel is open (tag editor, sidebar, etc.) — restore game music on close | `UPS_MusicControl_PauseGamePlayDefault` element (v1.5.3+) |
| Bind UPS settings (Enable Music, Radio Mode, Calm Down, etc.) to checkboxes in your theme | `{PluginSettings}` markup (v1.4.6+) |
| Switch the radio source (UPS pool ↔ Spotify) from Fullscreen | `SwitchRadioMode` bool via `{PluginSettings}` (v1.5.10+) |
| Let Radio Mode keep playing during a game session instead of pausing | `RadioPlaysThroughGames` bool via `{PluginSettings}` (v1.7.0+) |
| Ship a dedicated audio track that UPS plays as default music | `UPS_BackgroundAudio.mp3` in your theme's `audio/` folder (v1.5.2+) |
| Ship per-rarity achievement unlock sounds | `audio/Achievements/{rarity}.wav` in your theme's folder (v1.5.10+) |
| Custom media-control buttons (play, pause, skip, volume) | `playnite://uniplaysong/...` URIs (v1.3.10+) |
| Drop in a ready-made Now-Playing overlay, transport bar, or minimal play/skip control | `UPS_MediaControllerOverlay` / `UPS_MediaControllerBar` / `UPS_MediaControllerCompact` elements (v1.5.8+) |
| Big previous / play-pause / next buttons for a player page that shows its own art, title and timeline | `UPS_MediaControllerTransport` element (v1.9.0+) |
| Let the user seek on your timeline — mouse and right stick | Nothing: any `ProgressBar` bound to `ActiveMediaProgress` already seeks (v1.9.0+) — see [Seeking](#seeking--scrub-the-current-song-v190) |
| Skip forward / back buttons | `playnite://uniplaysong/seek/{seconds}` and `seekto/{0-100}` URIs (v1.9.0+) |
| A turntable-style cover the user turns to seek | `UPS_SeekDisc` overlay element (v1.9.0+) |
| The Now-Playing popup with a live, art-coloured spectrum on its seek bar | `UPS_MusicPlayerControlVisual` element (v1.9.0+) — see [Music player with a spectrum](#music-player-with-a-spectrum--ups_musicplayercontrolvisual-v190) |
| An animated, music-reactive visualizer with real transparency, for a page or your music player | `UPS_MusicVisualControl` element (v1.9.0+) — see [Music visualizer](#music-visualizer--ups_musicvisualcontrol-v190) |

---

## 1. Pause music with `UPS_MusicControl`

The most common use — pause UPS music while a theme overlay is visible.

### Quick start

```xml
<ContentControl x:Name="UPS_MusicControl"
    Tag="{Binding ElementName=MyOverlay, Path=IsVisible}" />
```

- `Tag="True"` → music fades out
- `Tag="False"` → music fades back in

### DataTrigger style (more flexible)

```xml
<ContentControl x:Name="UPS_MusicControl">
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Setter Property="Tag" Value="False"/>
            <Setter Property="Focusable" Value="False"/>
            <Style.Triggers>
                <!-- Pause during intro video -->
                <DataTrigger Binding="{Binding ElementName=IntroHost, Path=Tag}" Value="Playing">
                    <Setter Property="Tag" Value="True"/>
                </DataTrigger>

                <!-- Pause when settings menu is visible -->
                <DataTrigger Binding="{Binding ElementName=SettingsPanel, Path=Visibility}" Value="Visible">
                    <Setter Property="Tag" Value="True"/>
                </DataTrigger>

                <!-- Resume when overlay ends -->
                <DataTrigger Binding="{Binding ElementName=IntroHost, Path=Tag}" Value="Ended">
                    <Setter Property="Tag" Value="False"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ContentControl.Style>
</ContentControl>
```

### MultiDataTrigger (pause only when ALL conditions match)

```xml
<MultiDataTrigger>
    <MultiDataTrigger.Conditions>
        <Condition Binding="{Binding ElementName=TrailerContainer, Path=Opacity}" Value="1"/>
        <Condition Binding="{Binding ElementName=VideoPlayer, Path=Content.IsPlayerMuted}" Value="False"/>
    </MultiDataTrigger.Conditions>
    <Setter Property="Tag" Value="True"/>
</MultiDataTrigger>
```

### Don't pause Radio Mode music for your overlay — gate the `Tag` on `RadioModeEnabled`

A `Tag="True"` on `UPS_MusicControl` pauses **whatever** is currently playing, including Radio Mode (a pool, or Spotify). If your overlay (welcome hub, sidebar, menu) should pause per-game music but let *Radio Mode* keep playing, add `RadioModeEnabled` as a condition so the pause only fires when Radio Mode is **off**:

```xml
<MultiDataTrigger>
    <MultiDataTrigger.Conditions>
        <Condition Binding="{PluginSettings Plugin=AnikiHelper, Path=IsWelcomeHubOpen}" Value="True"/>
        <!-- only pause when Radio Mode is OFF; Radio Mode music plays through the hub -->
        <Condition Binding="{PluginSettings Plugin=UniPlaySong, Path=RadioModeEnabled}" Value="False"/>
    </MultiDataTrigger.Conditions>
    <Setter Property="Tag" Value="True"/>
</MultiDataTrigger>
```

`RadioModeEnabled` covers **all** Radio Mode sources (a UPS pool *or* Spotify) — that's usually what you want, and it's the simplest gate. There is no separate "Spotify radio" mode: Spotify is just one of the sources Radio Mode can play, chosen by the user in UniPlaySong's settings.

If you specifically need to distinguish **Spotify-as-radio** from a UPS-pool radio (e.g. pause pool-radio but never Spotify), bind the derived read-only `SpotifyRadioMode` instead — it's `true` only when Radio Mode is on **and** the source is Spotify:

```xml
<Condition Binding="{PluginSettings Plugin=UniPlaySong, Path=SpotifyRadioMode}" Value="False"/>
```

Both properties update live as the user toggles Radio Mode or switches the source, so the trigger re-evaluates on the fly. (Prefer `RadioModeEnabled` unless you truly need the Spotify-only distinction.)

### ANIKI REMAKE reference

The full set of triggers Mike Aniki uses for intro videos, trailers, settings, and welcome control:

```xml
<ContentControl x:Name="UPS_MusicControl">
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Setter Property="Tag" Value="False"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding ElementName=IntroHost, Path=Tag}" Value="Idle">
                    <Setter Property="Tag" Value="True"/>
                </DataTrigger>
                <DataTrigger Binding="{Binding ElementName=IntroHost, Path=Tag}" Value="Playing">
                    <Setter Property="Tag" Value="True"/>
                </DataTrigger>
                <DataTrigger Binding="{Binding ElementName=IntroHost, Path=Tag}" Value="Ended">
                    <Setter Property="Tag" Value="False"/>
                </DataTrigger>
                <DataTrigger Binding="{PluginSettings Plugin=ThemeOptions, Path=Options[IntroVideo_None]}" Value="True">
                    <Setter Property="Tag" Value="False"/>
                </DataTrigger>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding ElementName=TrailerContainer, Path=Opacity}" Value="1"/>
                        <Condition Binding="{Binding ElementName=ExtraMetadataLoader_VideoLoaderControl_NoControls_Sound, Path=Content.IsPlayerMuted}" Value="False"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Tag" Value="True"/>
                </MultiDataTrigger>
                <DataTrigger Binding="{Binding ElementName=AcceuilSettings, Path=Visibility}" Value="Visible">
                    <Setter Property="Tag" Value="True"/>
                </DataTrigger>
                <DataTrigger Binding="{Binding ElementName=WelcomeControl, Path=Tag}" Value="False">
                    <Setter Property="Tag" Value="True"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ContentControl.Style>
</ContentControl>
```

### Keep music playing during decorative videos — mute them with `IsMuted="True"`

UPS automatically pauses your game music when it detects a **theme video that is actually producing sound**. It does *not* pause for silent video. A video counts as "playing" only when **all three** are true:

- the `MediaElement` has an audio track (`HasAudio`),
- it is **not** muted (`IsMuted="False"`), and
- its `Volume` is above `0`.

So if your theme shows a **decorative or background video** that should *not* stop the music — an intro loop, a trophy/menu flourish, an ambient background clip — mark that element muted and UPS will ignore it:

```xml
<MediaElement Source="..."
              IsMuted="True"
              LoadedBehavior="Play" />
```

With `IsMuted="True"` (or `Volume="0"`), UPS never treats the video as audible playback, so your game music keeps playing while it's on screen. No `UPS_MusicControl` wiring needed for this — it's automatic.

**When to use which:**

| You want... | Do this |
|---|---|
| A video's **own audio** to take over (trailer, cutscene) — UPS music pauses | Leave the video **unmuted** (`IsMuted="False"`, `Volume > 0.8`). UPS pauses automatically. |
| A **silent/decorative** video to play *over* the music — music keeps going | Set the video **`IsMuted="True"`** (or `Volume="0"`). UPS ignores it. |
| Pause music for a **non-video overlay** (login panel, sidebar, menu) | Use `UPS_MusicControl` with a `Tag` trigger (above) — muting doesn't apply to non-video elements. |

> Tip: this is the cleanest way to handle background/ambient video. You don't need a `UPS_MusicControl` Tag trigger for muted videos — just mute the element and UPS does the right thing.

---

## 2. Swap game music for default music — `UPS_MusicControl_PauseGamePlayDefault` (v1.5.3+)

Where `UPS_MusicControl` pauses **everything**, this sibling element **swaps** the current game's own music for the user's chosen default music (Bundled Ambient, Random Game, Custom Folder — whatever they configured) while `Tag=True`, and restores game music when `Tag=False`.

Use case: background music keeps playing while the user interacts with a custom panel (tag editor, sidebar) — no silence, no game-track stutter.

```xml
<ContentControl x:Name="UPS_MusicControl_PauseGamePlayDefault">
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Setter Property="Tag" Value="False"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding ElementName=TagEditor, Path=IsOpen}" Value="True">
                    <Setter Property="Tag" Value="True"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ContentControl.Style>
</ContentControl>
```

- Works alongside `UPS_MusicControl` — they're independent. A pause request from `UPS_MusicControl` still wins over a swap request.
- Multiple instances stack: if ANY active instance has `Tag=True`, the swap is active.

Equivalent without an element in the visual tree:

```xml
<CheckBox IsChecked="{PluginSettings Plugin=UniPlaySong,
                                    Path=ForceDefaultMusicOverride,
                                    Mode=TwoWay}"/>
```

---

## 3. Bind UPS settings to theme checkboxes — `{PluginSettings}` (v1.4.6+)

For an in-theme audio quick-settings menu (Enable Game Music, Radio Mode, Calm Down Mode, etc.), use Playnite's `{PluginSettings}` markup. **No UPS element required in the visual tree** — and the binding silently no-ops if UPS isn't installed (no theme crash).

### The five most-toggled settings

| `Path=` | Type | What it controls |
|---|---|---|
| `EnableMusic` | bool | Game music. Default music keeps playing if `EnableDefaultMusic` is on (toggle both off for full silence) |
| `EnableDefaultMusic` | bool | Default / ambient music |
| `RadioModeEnabled` | bool | Continuous pool-based playback |

> **Radio source is the user's choice.** Binding a toggle to `RadioModeEnabled` turns Radio Mode on/off; the *source* it plays (a UPS pool, or **Spotify**) is chosen by the user in UniPlaySong's settings (Playback → Radio Mode → source). So your existing "Radio Mode" button automatically honors Spotify when the user has picked it — no theme change needed. To let users **switch the source from Fullscreen** (UPS pool ↔ Spotify) without visiting Desktop settings, bind the `SwitchRadioMode` toggle — see [Switch Radio Mode](#switch-radio-mode--flip-ups-pool--spotify-without-leaving-fullscreen-v1510) below.
>
> **Read-only status (for gating your own triggers):** `RadioModeEnabled` also reads back the current state, and the derived read-only `SpotifyRadioMode` is `true` only when Radio Mode is on *and* the source is Spotify. Both update live — use them to keep an overlay from pausing Radio Mode music (see [Don't pause Radio Mode music for your overlay](#dont-pause-radio-mode-music-for-your-overlay--gate-the-tag-on-radiomodeenabled)).
| `PlayOnlyOnGameSelect` | bool | When false, music plays while browsing too |
| `CalmDownModeEnabled` | bool | v1.5.0+ — gentle muffle + dim (great for late-night browsing toggles) |

Any property on `UniPlaySongSettings` is bindable this way — these five are just the most useful for a quick-options menu.

> **Calm Down, v1.8.7 onward.** Two things changed that a theme binding should know about.
>
> The **strength is now the user's** to choose: Live Effects → Calm Down offers presets (Subtle,
> Warm, Muffled, Distant, Whisper) plus manual sliders for high cut, level and transition time. Your
> toggle still just turns it on and off — but do not describe it in your theme as a fixed "1500 Hz,
> half volume, 1.5s" effect, because it may not be.
>
> There is also an **idle option** (`CalmDownOnIdle`) that engages Calm Down by itself after a set
> time with no keyboard or mouse input. It deliberately does **not** write `CalmDownModeEnabled` —
> so when idle has engaged it, your bound checkbox stays *unticked* even though the music is
> softened. That is intentional: it means idle can never switch off a Calm Down the user turned on
> through your toggle. If you want to reflect the audible state rather than the user's switch,
> `CalmDownIdleActive` is readable too (runtime-only, never persisted).

### Quick-options panel example

```xml
<StackPanel>
    <CheckBox Content="Enable Game Music"
              IsChecked="{PluginSettings Plugin=UniPlaySong,
                                         Path=EnableMusic, Mode=TwoWay}"/>

    <CheckBox Content="Enable Default Music"
              IsChecked="{PluginSettings Plugin=UniPlaySong,
                                         Path=EnableDefaultMusic, Mode=TwoWay}"/>

    <CheckBox Content="Radio Mode"
              IsChecked="{PluginSettings Plugin=UniPlaySong,
                                         Path=RadioModeEnabled, Mode=TwoWay}"/>

    <CheckBox Content="Play Only on Game Select"
              IsChecked="{PluginSettings Plugin=UniPlaySong,
                                         Path=PlayOnlyOnGameSelect, Mode=TwoWay}"/>

    <CheckBox Content="Calm Down Mode"
              IsChecked="{PluginSettings Plugin=UniPlaySong,
                                         Path=CalmDownModeEnabled, Mode=TwoWay}"/>
</StackPanel>
```

Two-way sync is automatic — flipping a checkbox in the theme and flipping the same setting in UPS's own settings dialog stay in sync.

### Switch Radio Mode — flip UPS pool ↔ Spotify without leaving Fullscreen (v1.5.10+)

`RadioMusicSource` is a **global** setting, so before v1.5.10 switching between a UPS-pool radio and Spotify radio meant going back to Desktop settings. `SwitchRadioMode` is a theme-bindable **bool toggle** that flips the radio source live from Fullscreen:

```xml
<!-- Unchecked = UPS pool radio · Checked = Spotify radio -->
<CheckBox Content="Switch Radio Mode (UPS ↔ Spotify)"
          IsChecked="{PluginSettings Plugin=UniPlaySong, Path=SwitchRadioMode, Mode=TwoWay}"/>
```

| `Path=` | Type | Behavior |
|---|---|---|
| `SwitchRadioMode` | bool | `false` → last UPS pool (FullLibrary, etc.), `true` → Spotify. Reads back the current source live. |

- **Switches the source only** — it does **not** turn Radio Mode on or off. If Radio Mode is already on, the switch takes effect immediately (the current pool fades out and the new source starts). If Radio Mode is off, the choice is stored and applies the next time Radio Mode turns on.
- Toggling off Spotify returns to the **last UPS pool** the user had (remembered across restarts), not a default.
- Pair it with a `RadioModeEnabled` toggle if you want one control for on/off and another for the source. `SpotifyRadioMode` (read-only) still reflects the result for gating your own triggers.

> Prefer `SwitchRadioMode` (a clean bool) for a Fullscreen "switch" button. `RadioMusicSource` (the raw enum) is also bindable if you want a full multi-source picker (FullLibrary, CustomFolder, Spotify, …).

When Spotify is your active source, UniPlaySong auto-launches it on startup if it isn't already running (Experimental tab, off by default). If Spotify opens to a login screen and doesn't auto-play, your theme's transport controls (play/skip) let the user start playback manually once authenticated.

### Radio plays through games (v1.7.0+)

`RadioPlaysThroughGames` (bool, default `false`) — when enabled, Radio Mode keeps playing while a game is running instead of pausing. Applies only to Radio Mode (UPS pool or Spotify); game-specific music still pauses. Independent of "Pause on Game Launch" — it works whether that setting is on or off.

```xml
<CheckBox Content="Radio plays through games"
          IsChecked="{PluginSettings Plugin=UniPlaySong, Path=RadioPlaysThroughGames, Mode=TwoWay}"/>
```

| `Path=` | Type | Behavior |
|---|---|---|
| `RadioPlaysThroughGames` | bool | `true` → Radio Mode plays through games, `false` → pauses on game launch (default). |

- **Suppressed while a game runs:** game-launch pause, focus loss, minimize, tray-hide, idle timeout, and the game's own audio.
- **Still pause the radio:** manual pause, screen lock, video previews, theme overlays, the dashboard player, and jingles.

### Aniki ReMake reference (drop-in)

Mike's exact paste-in for Aniki ReMake — `Views/AdditionalViews/QuickOptionsView.xaml`, inside the existing `<Grid x:Name="Volume">`, after the BackgroundVolume slider:

```xml
<CheckBoxEx Margin="0,30,0,0"
            HorizontalAlignment="Stretch"
            VerticalAlignment="Center"
            Style="{DynamicResource SettingsSectionCheckbox}"
            Content="Enable Game Music"
            IsChecked="{PluginSettings Plugin=UniPlaySong, Path=EnableMusic, Mode=TwoWay}" />

<CheckBoxEx Margin="0,10,0,0"
            HorizontalAlignment="Stretch"
            VerticalAlignment="Center"
            Style="{DynamicResource SettingsSectionCheckbox}"
            Content="Enable Default Music"
            IsChecked="{PluginSettings Plugin=UniPlaySong, Path=EnableDefaultMusic, Mode=TwoWay}" />

<CheckBoxEx Margin="0,10,0,0"
            HorizontalAlignment="Stretch"
            VerticalAlignment="Center"
            Style="{DynamicResource SettingsSectionCheckbox}"
            Content="Radio Mode"
            IsChecked="{PluginSettings Plugin=UniPlaySong, Path=RadioModeEnabled, Mode=TwoWay}" />

<CheckBoxEx Margin="0,10,0,0"
            HorizontalAlignment="Stretch"
            VerticalAlignment="Center"
            Style="{DynamicResource SettingsSectionCheckbox}"
            Content="Play Only on Game Select"
            IsChecked="{PluginSettings Plugin=UniPlaySong, Path=PlayOnlyOnGameSelect, Mode=TwoWay}" />

<CheckBoxEx Margin="0,10,0,0"
            HorizontalAlignment="Stretch"
            VerticalAlignment="Center"
            Style="{DynamicResource SettingsSectionCheckbox}"
            Content="Calm Down Mode"
            IsChecked="{PluginSettings Plugin=UniPlaySong, Path=CalmDownModeEnabled, Mode=TwoWay}" />
```

Adapting to other themes:

- **`CheckBoxEx` vs `CheckBox`** — `CheckBoxEx` is Playnite's controller-friendly variant (D-Pad focus traversal). If your theme uses plain `CheckBox`, just swap the element name; the binding is identical.
- **`Style="{DynamicResource ...}"`** — substitute whatever checkbox style your theme exposes. `{PluginSettings}` doesn't care about the visual style.
- **Restart Playnite** after editing theme XAML — Fullscreen XAML is parsed at startup; structural changes don't live-reload.

### Live now-playing data (v1.5.7+)

UniPlaySong publishes the active music's metadata as live, bindable properties (updates as the song changes; works in Fullscreen; no UPS element required; no-ops if UPS isn't installed):

| Bind | Type | Example |
| --- | --- | --- |
| `NowPlayingTitle` | text | `<TextBlock Text="{PluginSettings Plugin=UniPlaySong, Path=NowPlayingTitle}"/>` |
| `NowPlayingArtist` | text | `<TextBlock Text="{PluginSettings Plugin=UniPlaySong, Path=NowPlayingArtist}"/>` |
| `NowPlayingAlbumArtPath` | image file path | `<Image Source="{PluginSettings Plugin=UniPlaySong, Path=NowPlayingAlbumArtPath}"/>` |
| `NowPlayingAlbum` | text (Spotify only) | `<TextBlock Text="{PluginSettings Plugin=UniPlaySong, Path=NowPlayingAlbum}"/>` |
| `NowPlayingGenre` | text (Spotify only) | `<TextBlock Text="{PluginSettings Plugin=UniPlaySong, Path=NowPlayingGenre}"/>` |
| `NowPlayingDuration` | text "m:ss" (Spotify only) | `<TextBlock Text="{PluginSettings Plugin=UniPlaySong, Path=NowPlayingDuration}"/>` |
| `NowPlayingArtColor1`, `NowPlayingArtColor2` | colour (darker first; black with no art) | The current art's two colours, for a backdrop gradient. Spotify with no art of its own gives Spotify's dark `#121212` and green `#1DB954`. In Fullscreen's main view a brush can't take `{PluginSettings}` - bind a hidden element's `Tag` to it and the `GradientStop.Color` to that `Tag` (see the demo theme's Spotify page). |
| `SpotifyRecentTracks` | list, newest first (Spotify only) | `<ItemsControl ItemsSource="{PluginSettings Plugin=UniPlaySong, Path=SpotifyRecentTracks}"/>` - each item has `Title`, `Artist`, `Album`, `PlayedAt` and `Art` (cover image, tracks heard this session only). Kept across restarts (last 50). |
| `GameRecentTracks` | list, newest first (game music) | The same for game music; `Album` is the game's name. See [Recent tracks](#recent-tracks--ups_spotifyrecenttracks-ups_gamerecenttracks-v190). |

These reflect whichever source is the active music. Title, artist, and art are populated for both a UniPlaySong game track and a Spotify track. `NowPlayingAlbumArtPath` is a file path to the current art image — embedded ID3 art for game tracks (falling back to the game's cover when the track has none), or the Spotify track's album art — and is empty only when nothing is resolvable (show your own placeholder).

`NowPlayingAlbum`, `NowPlayingGenre`, and `NowPlayingDuration` are populated **only when Spotify is the active music** (album, comma-joined genres, and total track length preformatted as `m:ss`). They are empty strings for game music, so bind them inside a panel you collapse when empty.

### Recent tracks — `UPS_SpotifyRecentTracks`, `UPS_GameRecentTracks` (v1.9.0+)

A ready-made list of the tracks heard recently, newest first: cover, title, artist and time. Text takes your theme's colour and font; the list clips to the space you give it.

```xml
<ContentControl x:Name="UPS_SpotifyRecentTracks" Height="220" />
<ContentControl x:Name="UPS_GameRecentTracks" Height="220" />
```

Three ways to use it:

1. **As is** — place the element.
2. **Your own rows** — define a `DataTemplate` keyed `UPS_SpotifyRecentTracksTemplate` (or `UPS_GameRecentTracksTemplate`) in resources the element can reach. It binds `Title`, `Artist`, `Album` (the game's name, for game music), `PlayedAt` and `Art`.
3. **Your own layout** — skip the element and bind `ItemsSource="{PluginSettings Plugin=UniPlaySong, Path=SpotifyRecentTracks}"` (or `GameRecentTracks`) on your own list.

Both lists outlive a restart: UPS keeps the last 50 in `SpotifyRecentTracks.txt` and `GameRecentTracks.txt` in its data folder (`<Playnite config>\ExtraMetadata\UniPlaySong`), one tab-separated line per track, which other add-ons can read. Covers show only for tracks heard this session.

### Fire an animation when the track changes — `IsMusicChanged` (v1.5.9+)

If you want a **notification-style animation** that plays each time the music changes — rather than a permanently-visible now-playing readout — bind to `IsMusicChanged`. UniPlaySong flips it to `true` the moment the track changes (song→song, silence→song, song→silence, or a source switch), then flips it back to `false` a fraction of a second later. It never fires on a mere re-publish of the same track.

| Bind | Type | Meaning |
| --- | --- | --- |
| `IsMusicChanged` | bool | Pulses `true` briefly on each track change, then returns to `false`. |

The `true`→`false` **edge** is the point — it re-arms so the next change pulses again. The pulse is intentionally short; run your own visible-duration timer in the animation:

```xml
<Style.Triggers>
    <DataTrigger Binding="{PluginSettings Plugin=UniPlaySong, Path=IsMusicChanged}" Value="True">
        <DataTrigger.EnterActions>
            <BeginStoryboard>
                <Storyboard>
                    <!-- e.g. fade a "Now Playing" toast in, hold ~4.5s, then fade out -->
                    <DoubleAnimation Storyboard.TargetProperty="Opacity" To="1" Duration="0:0:0.2"/>
                    <DoubleAnimation Storyboard.TargetProperty="Opacity" To="0" BeginTime="0:0:4.5" Duration="0:0:0.4"/>
                </Storyboard>
            </BeginStoryboard>
        </DataTrigger.EnterActions>
    </DataTrigger>
</Style.Triggers>
```

Combine with `NowPlayingTitle` / `NowPlayingArtist` / `NowPlayingAlbumArtPath` to show *what* changed inside the toast.

#### Basic toast

A minimal slide-in toast. Good starting point — see the **ready-made template** below for a
polished, theme-integrated version.

⚠️ **Style storyboards can't use `TargetName`.** A `Storyboard` inside a `Style` (as above) may only animate the **styled element itself** — WPF throws *"A Storyboard tree in a Style cannot specify a TargetName"* and Playnite crashes if you add `Storyboard.TargetName="..."`. So don't reference a named child. To animate a transform (e.g. slide the toast in), give the styled element its own `RenderTransform` and animate it **by property path**, no name:

```xml
<Border Opacity="0" HorizontalAlignment="Left" VerticalAlignment="Top" ...>
    <Border.RenderTransform>
        <TranslateTransform X="-40"/>   <!-- no x:Name -->
    </Border.RenderTransform>
    <Border.Style>
        <Style TargetType="Border">
            <Style.Triggers>
                <DataTrigger Binding="{PluginSettings Plugin=UniPlaySong, Path=IsMusicChanged}" Value="True">
                    <DataTrigger.EnterActions>
                        <BeginStoryboard>
                            <Storyboard>
                                <DoubleAnimation Storyboard.TargetProperty="Opacity" To="1" Duration="0:0:0.25"/>
                                <!-- animate the Border's OWN transform by path - no TargetName -->
                                <DoubleAnimation Storyboard.TargetProperty="RenderTransform.X" To="0" Duration="0:0:0.25"/>
                                <DoubleAnimation Storyboard.TargetProperty="Opacity" To="0" BeginTime="0:0:4.5" Duration="0:0:0.5"/>
                                <DoubleAnimation Storyboard.TargetProperty="RenderTransform.X" To="-40" BeginTime="0:0:4.5" Duration="0:0:0.5"/>
                            </Storyboard>
                        </BeginStoryboard>
                    </DataTrigger.EnterActions>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <!-- toast content: NowPlaying* bindings -->
</Border>
```

If you need to animate several *different* named children together, drive the storyboard from the element's own `Triggers`/`ControlTemplate.Triggers` (where `TargetName` is allowed) instead of a `Style`.

#### Ready-made "Now Playing" toast template

A complete, drop-in toast — slides in from the top-right on each track change, holds ~4.5s, slides
out — with album art + title + artist bound to the live now-playing data. Contributed by **Mike
Aniki** (ANIKI REMAKE). Notes on what makes it robust:

- Uses your theme's `DynamicResource` brushes/styles (`BackgroundMenu`, `NotificationBorder`,
  `TextHighlightBrush`, `TextBlockBaseStyle`) so it matches the theme instead of hardcoded colors —
  swap these for whatever your theme exposes.
- `MaxWidth` on the border + `TextWrapping="Wrap"` on the title/artist so long track names display
  fully instead of being clipped.
- `RenderOptions.BitmapScalingMode="Fant"` on the album art for crisp scaling.

```xml
<Border HorizontalAlignment="Right"
        VerticalAlignment="Top"
        Margin="0,130,40,0"
        Opacity="0"
        MinHeight="75"
        MinWidth="250"
        MaxWidth="500"
        Background="{DynamicResource BackgroundMenu}"
        BorderBrush="{DynamicResource NotificationBorder}"
        CornerRadius="10"
        Padding="14,10"
        BorderThickness="1">

    <!-- transform used for the slide-in (no x:Name — Style storyboards can't reference names) -->
    <Border.RenderTransform>
        <TranslateTransform X="40"/>
    </Border.RenderTransform>

    <Border.Style>
        <Style TargetType="Border">
            <Style.Triggers>
                <DataTrigger Binding="{PluginSettings Plugin=UniPlaySong, Path=IsMusicChanged}" Value="True">
                    <DataTrigger.EnterActions>
                        <BeginStoryboard>
                            <Storyboard>
                                <!-- slide + fade IN -->
                                <DoubleAnimation Storyboard.TargetProperty="Opacity"
                                                 To="1" Duration="0:0:0.25"/>
                                <DoubleAnimation Storyboard.TargetProperty="RenderTransform.X"
                                                 To="0" Duration="0:0:0.25"/>
                                <!-- hold ~4.5s, then slide + fade OUT (change this to taste) -->
                                <DoubleAnimation Storyboard.TargetProperty="Opacity"
                                                 To="0" BeginTime="0:0:4.5" Duration="0:0:0.5"/>
                                <DoubleAnimation Storyboard.TargetProperty="RenderTransform.X"
                                                 To="40" BeginTime="0:0:4.5" Duration="0:0:0.5"/>
                            </Storyboard>
                        </BeginStoryboard>
                    </DataTrigger.EnterActions>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>

    <!-- toast content: live now-playing data -->
    <StackPanel Orientation="Horizontal">
        <Border CornerRadius="6" ClipToBounds="True" Width="60" Height="60" Margin="0,0,12,0">
            <Image Source="{PluginSettings Plugin=UniPlaySong, Path=NowPlayingAlbumArtPath}"
                   Stretch="UniformToFill"
                   RenderOptions.BitmapScalingMode="Fant"/>
        </Border>
        <StackPanel VerticalAlignment="Center">
            <TextBlock Text="♫ NOW PLAYING"
                       Foreground="{DynamicResource TextHighlightBrush}"
                       Style="{DynamicResource TextBlockBaseStyle}"
                       FontSize="12" FontWeight="SemiBold" Margin="0,0,0,2" Opacity="0.7"/>
            <TextBlock Text="{PluginSettings Plugin=UniPlaySong, Path=NowPlayingTitle}"
                       Style="{DynamicResource TextBlockBaseStyle}"
                       FontWeight="SemiBold" FontSize="20"
                       TextWrapping="Wrap" TextTrimming="CharacterEllipsis" MaxWidth="320"/>
            <TextBlock Text="{PluginSettings Plugin=UniPlaySong, Path=NowPlayingArtist}"
                       Style="{DynamicResource TextBlockBaseStyle}"
                       FontSize="18" Opacity="0.8"
                       TextWrapping="Wrap" TextTrimming="CharacterEllipsis" MaxWidth="320"/>
        </StackPanel>
    </StackPanel>
</Border>
```

### Now-playing mini-player elements (v1.5.7+)

Two display-only elements that show the current track (title, artist, album art; plus Spotify album/genre/duration). Place either in a **Desktop or Fullscreen** theme view (e.g. anchored on a game banner) — like the other `UPS_` elements, they render wherever the theme puts them. They collapse to nothing when no music is playing, and for game music show title + artist + art only (album/genre/duration are Spotify-only).

| Element | Look |
| --- | --- |
| `UPS_NowPlayingMiniPlayer` | Horizontal bar: album art + title + artist + Spotify album·genre·duration |
| `UPS_NowPlayingMiniPlayerCompact` | One line: ♪ + title · artist · duration |

```xml
<!-- in a Desktop or Fullscreen theme view -->
<ContentControl x:Name="UPS_NowPlayingMiniPlayer" />
<ContentControl x:Name="UPS_NowPlayingMiniPlayerCompact" />
```

Both ship a self-contained "glass pill" default style and are display-only (no transport controls). The default sizing (compact art and fonts) is tuned for a Desktop banner; for Fullscreen (TV-distance) you'll likely want to retemplate or scale the element up — both support a standard implicit `Style`/`ControlTemplate` override.

---

## 4. Ship audio with your theme — `UPS_BackgroundAudio.mp3` (v1.5.2+)

If your theme has its own ambient track, drop it in the theme's `audio/` folder and UPS will play it as the user's default music when they pick "Active Theme Music" as their source.

```
YourTheme_GUID/
├── audio/
│   ├── background.mp3            ← Playnite's built-in player reads this
│   ├── UPS_BackgroundAudio.mp3   ← UPS reads this
│   ├── activation.wav             (optional: UI sounds)
│   └── navigation.wav             (optional: UI sounds)
├── Views/
└── theme.yaml
```

**Requirements:**

- Exact filename: `UPS_BackgroundAudio` (case-insensitive match, but canonical form is what's shown)
- Extension: `.mp3`, `.ogg`, `.wav`, or `.flac`
- Location: `audio/` subdirectory of your theme root

**Why a separate file?** Playnite's own SDL player opens `background.mp3` directly when the user is in fullscreen mode. If UPS tried to read the same file, the two players would fight over the file handle — glitches, looping artifacts, silence. The dedicated UPS filename keeps them out of each other's way.

**Same audio or different audio?** Up to you. Most themes ship the same file twice (`background.mp3` for Playnite, `UPS_BackgroundAudio.mp3` for UPS) so the experience is identical with or without UPS installed. Some themes ship a different track when UPS is the audio source.

### What the user sees

In Settings → Playback → Default Music Source → "Active Theme Music", UPS shows the live status of the user's current theme:

| State | When |
|---|---|
| ✓ Ready | Your theme ships `UPS_BackgroundAudio.*` — plays out of the box |
| ⚠ Can be created | Theme has `background.*` only — UPS offers a one-click button to make a UPS-named copy |
| ✗ Unsupported | Theme has neither — user picks a different default music source |
| ℹ Fullscreen only | User is in Desktop mode; option will apply when they switch to Fullscreen |

**First-install bonus (v1.5.3+)**: when a user installs UPS for the first time, UPS automatically checks whether their active theme ships `UPS_BackgroundAudio` and picks it as the default music source if found. They don't have to dig into settings to discover the option.

### Login screen audio

UPS only activates Active Theme Music when the user is at the game library — never on the login screen. Themes that play `background.mp3` during login (Playnite's native behavior) keep working unchanged. Themes that handle login audio via WPF `MediaElement` (like Aniki ReMake) also keep working unchanged.

### Ship achievement unlock sounds with your theme (v1.5.10+)

Your theme can provide its own per-rarity achievement unlock sounds. Drop audio files in an `audio/Achievements/` subfolder of your theme, named by rarity:

```
YourTheme/
├── audio/
│   ├── UPS_BackgroundAudio.mp3
│   └── Achievements/
│       ├── common.wav       ← bronze
│       ├── uncommon.wav      ← silver
│       ├── rare.wav          ← gold
│       ├── ultrarare.wav     ← platinum
│       ├── hidden.[ext]      ← hidden / secret unlock
│       └── capstone.wav      ← perfect / 100%
```

- Rarity filenames: `common`, `uncommon`, `rare`, `ultrarare`, `hidden`, `capstone` (case-insensitive).
- Extensions: `.wav`, `.mp3`, `.ogg`, `.flac` — first match wins.
- Ship as many or as few as you like — the user's selected pack falls back to UPS's bundled **PA Starter Pack** for any rarity you don't provide, and to their master default sound if that's missing too.

The user opts in via **Settings → Gamification → Achievement Unlock Sounds → Rarity sound pack → "Active theme's sounds"**. That screen shows how many of the five rarities your theme provides (`N/5`). Achievement sounds play over a running game (triggered by an external unlock plugin such as Playnite Achievements — UPS doesn't detect unlocks itself).

---

## 5. External control via URIs (v1.3.10+)

For custom media control buttons embedded in your theme. Fire-and-forget — no XAML wiring required, just launch the URI.

**Format:** `playnite://uniplaysong/{command}`

| Command | URI |
|---|---|
| Play / Resume | `playnite://uniplaysong/play` |
| Pause | `playnite://uniplaysong/pause` |
| Toggle Play/Pause | `playnite://uniplaysong/playpausetoggle` |
| Skip to Next Song | `playnite://uniplaysong/skip` |
| Restart Current Song | `playnite://uniplaysong/restart` |
| Stop Playback | `playnite://uniplaysong/stop` |
| Set Volume (0–100) | `playnite://uniplaysong/volume/50` |
| Skip forward / back by N seconds (v1.9.0+) | `playnite://uniplaysong/seek/10`, `playnite://uniplaysong/seek/-10` |
| Jump to a point, 0–100 % (v1.9.0+) | `playnite://uniplaysong/seekto/50` |

**When to use which:**

- `UPS_MusicControl` — state-driven pause/resume tied to UI visibility
- URI — explicit user actions (a "Skip" button on a media bar)

### Firing a URI from a theme button

Use Playnite's own `NavigateUrlCommand` with the URI as the parameter — the same way themes already open
`playnite://playnite/installaddon/...` links. Declare the namespace on your view's root element (many themes, Aniki
ReMake included, already have it):

```xml
xmlns:commands="clr-namespace:Playnite.Commands;assembly=Playnite"
```

Then any button can fire any UPS command:

```xml
<!-- Fullscreen: ButtonEx, so the controller's confirm press activates it -->
<ButtonEx Content="Play / Pause"
          Command="{x:Static commands:GlobalCommands.NavigateUrlCommand}"
          CommandParameter="playnite://uniplaysong/playpausetoggle" />
```

```xml
<!-- Desktop: a plain Button works for the mouse -->
<Button Content="Next song"
        Command="{x:Static commands:GlobalCommands.NavigateUrlCommand}"
        CommandParameter="playnite://uniplaysong/next" />
```

- **Fullscreen needs `ButtonEx`** (or `ToggleButtonEx`). A plain `Button` shows, but ignores the controller's confirm
  press — Playnite only auto-activates its own buttons.
- **A URI takes a short trip through Windows and back into Playnite**, so expect a brief delay per press (typically a
  fraction of a second). Right for buttons; for continuous control like scrubbing, use a seekable timeline or the
  right stick (see [Seeking](#seeking--scrub-the-current-song-v190)).
- A bad or unknown command shows a UniPlaySong notification rather than failing silently, which makes typos easy to
  spot while building.

### Achievement / trophy unlock sound (v1.5.10+)

For **plugins** (e.g. Playnite Achievements) — or a theme — that want UniPlaySong to play a console-style unlock sound when an achievement is earned. UniPlaySong doesn't detect unlocks; the caller fires the URI, and UniPlaySong plays the user's configured achievement sound (Settings → **Gamification** → Achievement Unlock Sounds). It plays on a dedicated lightweight player, so it's near-instant and works over a running game; it no-ops if the user has the sound turned off.

**Format:** `playnite://uniplaysong/playniteachievements/{tier}`

| Rarity | Tier segment | URI |
|---|---|---|
| Common | `commonachievement` | `playnite://uniplaysong/playniteachievements/commonachievement` |
| Uncommon | `uncommonachievement` | `playnite://uniplaysong/playniteachievements/uncommonachievement` |
| Rare | `rareachievement` | `playnite://uniplaysong/playniteachievements/rareachievement` |
| Ultra-rare | `ultrarareachievement` | `playnite://uniplaysong/playniteachievements/ultrarareachievement` |
| Hidden (secret unlock) | `hidden` | `playnite://uniplaysong/playniteachievements/hidden` |
| Capstone (platinum / 100%) | `capstoneachievement` | `playnite://uniplaysong/playniteachievements/capstoneachievement` |

`hidden` is the one segment without the `achievement` suffix — that is the command name Playnite Achievements sends.

Each tier resolves to a sound via the user's selected **Achievement Sound Pack** (their theme's sounds, the bundled PA Starter Pack, or their own custom files), falling back to a master default sound. Case-insensitive; an unknown/missing tier plays the default (forward-compatible). Theme devs: to ship your own per-rarity sounds, see ["Ship achievement unlock sounds with your theme"](#ship-achievement-unlock-sounds-with-your-theme-v1510) above.

---

## Unified Media Controls (v1.5.8+)

<p align="center">
  <img src="../assets/DemoMediaControls.png" alt="UniPlaySong media controls and Now Playing toast in a Fullscreen theme" width="720">
</p>

*The drop-in media controls and the `IsMusicChanged` "Now Playing" toast in a Fullscreen theme.*

Three self-contained, transport-capable theme elements — display **and** control now-playing music in one drop-in. All follow whichever source is currently audible (your game music, or Spotify when Spotify is the active source), collapse to nothing when no music is playing, and swap the play/pause icon with playback state.

| Element | What it is |
|---|---|
| `UPS_MediaControllerOverlay` | PS5-style Now-Playing popup: large album art, source name, position/duration with a progress bar, full transport row (previous / play-pause / next / mute), and a volume slider. |
| `UPS_MediaControllerBar` | Horizontal transport pill: album art + title/artist, with inline previous / play-pause / next buttons. |
| `UPS_MediaControllerCompact` | Minimal one-line control: play-pause + next (skip), for a tight footprint. |
| `UPS_MediaControllerTransport` (v1.9.0+) | Previous / play-pause / next alone, at a large size — for a player page that lays out its own art, title and timeline. |

```xml
<!-- Full Now-Playing overlay -->
<ContentControl x:Name="UPS_MediaControllerOverlay" />
```

```xml
<!-- Horizontal transport bar -->
<ContentControl x:Name="UPS_MediaControllerBar" />
```

```xml
<!-- Minimal play/skip control -->
<ContentControl x:Name="UPS_MediaControllerCompact" />
```

```xml
<!-- Transport buttons only, for your own player layout -->
<ContentControl x:Name="UPS_MediaControllerTransport" Focusable="False" KeyboardNavigation.IsTabStop="False" />
```

Each is an empty-tag `<ContentControl>` — no child XAML, no triggers to wire. Drop it anywhere in a Desktop or Fullscreen theme view and UPS supplies the content, styling, and behavior. If UniPlaySong isn't installed the tag simply resolves to nothing, so it's safe to ship in a theme unconditionally. The now-playing text and album art update live as tracks and playback change (including when the user toggles Radio Mode or edits UPS settings) — no theme-side refresh needed.

### Fullscreen / controller support

The transport buttons inside these elements are controller-friendly in Fullscreen: use the D-pad to move focus onto a button and press the **confirm** button (A, or B if the user has *Swap Confirm/Cancel* enabled) to activate it — UPS bridges the gamepad confirm to the focused button (Playnite only auto-activates its own internal buttons, so plugin controls need this bridge, which UPS provides for you).

Two things to know when placing them in Fullscreen:

- **A control needs keyboard focus to receive the confirm press.** If you drop an element into a **popup, flyout, or reveal panel**, set focus onto it (or one of its buttons) when the panel opens — otherwise the gamepad has nothing focused to confirm. This is the same approach Playnite's own overlays use. The simplest way is a focus trigger on your container:

  ```xml
  <!-- When your popup/panel becomes visible, move focus into the UPS element -->
  <ContentControl x:Name="UPS_MediaControllerBar" Focusable="True">
      <ContentControl.Style>
          <Style TargetType="ContentControl">
              <Style.Triggers>
                  <Trigger Property="IsVisible" Value="True">
                      <Setter Property="FocusManager.FocusedElement"
                              Value="{Binding RelativeSource={RelativeSource Self}}"/>
                  </Trigger>
              </Style.Triggers>
          </Style>
      </ContentControl.Style>
  </ContentControl>
  ```

  For finer control, focus the play/pause button specifically from your own popup-opened handler (e.g. a `Storyboard`/`EventTrigger` calling into your theme code), exactly as the PS5-style overlays do.

- **Always-visible placements** (e.g. docked in a details view) put the transport buttons in the view's controller navigation path. That's what you want if the element is meant to be operated there — the user D-pads to the buttons and confirms. But if you're docking one purely for **display** (e.g. a status bar at the top of a details view), those focusable buttons can trap D-pad focus and stop the user reaching other content like the game description. Wall the element off from directional navigation so it stays display-only:

  ```xml
  <ContentControl x:Name="UPS_MediaControllerBar" Focusable="False"
                  KeyboardNavigation.IsTabStop="False"
                  KeyboardNavigation.DirectionalNavigation="None"
                  KeyboardNavigation.TabNavigation="None"/>
  ```

  Mouse clicks still work; the controller just skips over it. (Use a display-only `UPS_NowPlayingMiniPlayer`/`Compact` instead if you never need transport there.)

- **Focus highlight.** The transport buttons show a focus ring when they hold keyboard/controller focus (there's no mouse pointer in Fullscreen), so the user can see what the confirm press will hit.

Prefer building your **own** transport buttons? Use the `{PluginSettings}` state + `playnite://uniplaysong/*` URIs below, and make your buttons Playnite's Fullscreen `ButtonEx` (a plain `<Button>` will show but won't respond to the gamepad confirm press).

### Custom layouts — read state + trigger transport directly

Playnite can't inject theme XAML into a plugin control, so if you want your **own** buttons and layout instead of the three prefabs above, read live state via `{PluginSettings}` and drive transport via `playnite://uniplaysong/*` URIs.

**Live state via `{PluginSettings Plugin=UniPlaySong, Path=...}`:**

| Property | Type | Meaning |
|---|---|---|
| `ActiveMediaProgress` | double (0–100) | Playback progress of the active source. |
| `ActiveMediaPositionText` | string "m:ss" | Current position of the active source. |
| `ActiveMediaDurationText` | string "m:ss" | Total duration of the active source. |
| `ActiveMediaVolume` | double (0–100) | Volume of the active source. |
| `ActiveMediaIsPlaying` | bool | True when the active source is currently playing (drives your play/pause icon). |
| `ActiveMediaIsMuted` | bool | True when the active source is muted (drives your mute icon). Prefer this over `ActiveMediaVolume == 0` — volume also reads 0 during fades/pauses, which would flash the mute icon. |
| `ActiveMediaSourceName` | string | Friendly name of the active source ("UniPlaySong" / "Spotify"), empty when none. |
| `ActiveMediaSourceKind` | enum `None`/`Ups`/`Spotify` | Which source is active, for source-icon logic. |
| `ActiveMediaHasMedia` | bool | True when there's any active media — collapse your control when false. |
| `ActiveMediaCanNext` | bool | True when the active source can skip to a next track/song. |
| `ActiveMediaCanPrevious` | bool | True when the active source can go to a previous track (or restart, for UPS). |

The existing `NowPlaying*` properties (`NowPlayingTitle`, `NowPlayingArtist`, `NowPlayingAlbumArtPath`, `NowPlayingAlbum`, `NowPlayingGenre`, `NowPlayingDuration` — see [Live now-playing data](#live-now-playing-data-v157)) are unchanged and can be combined with the `ActiveMedia*` props for a fully custom now-playing readout.

```xml
<TextBlock Text="{PluginSettings Plugin=UniPlaySong, Path=ActiveMediaSourceName}"/>
<TextBlock Text="{PluginSettings Plugin=UniPlaySong, Path=ActiveMediaPositionText}"/>
<ProgressBar Value="{PluginSettings Plugin=UniPlaySong, Path=ActiveMediaProgress}" Maximum="100"/>
```

**Trigger transport via URIs (source-aware — same source-follow logic as the prefab elements):**

| Command | URI |
|---|---|
| Toggle Play/Pause | `playnite://uniplaysong/playpausetoggle` |
| Next | `playnite://uniplaysong/next` (alias: `skip`) |
| Previous | `playnite://uniplaysong/previous` |
| Toggle Mute | `playnite://uniplaysong/togglemute` |
| Skip forward / back N seconds (v1.9.0+) | `playnite://uniplaysong/seek/{seconds}` (signed) |
| Jump to a point, 0–100 % (v1.9.0+) | `playnite://uniplaysong/seekto/{percent}` |

These (and the two seek commands) are source-aware — they act on whichever source is audible (UPS or Spotify). `togglemute` mutes UPS's own music via its player volume, or Spotify at the Windows audio-session level (the same as Spotify's Volume Mixer entry, since the media-key API has no mute); either way `ActiveMediaIsMuted` reflects the result. The original `play`, `pause`, `stop`, `restart`, and `volume/{0-100}` URIs (see [External control via URIs](#5-external-control-via-uris-v1310)) are unchanged and remain UPS-only.

> **Note:** Transport currently drives UniPlaySong's own music playback and the Spotify desktop app. Support for other media apps/sessions is planned.

### Seeking — scrub the current song (v1.9.0+)

UPS can move the current song to any point — its own music files and Spotify alike (not chiptunes or PSF, which can only restart). There are four ways in, and the first needs nothing from you.

**1. Your timeline is already seekable.** Any `ProgressBar` whose `Value` is bound to `ActiveMediaProgress` — like the one in the snippet above, or the bar inside `UPS_MediaControllerOverlay` — becomes:

- **click to jump / drag to scrub** with the mouse, and
- **right-stick scrubbable** in Fullscreen: tilt right to fast-forward, left to rewind, faster the further you tilt (about 4 s per second just past the dead zone, up to 40 s per second at full tilt). Pushing mostly up or down is ignored. No focus is needed, so the D-pad stays free for your buttons.

Nothing to add to your theme. Worth knowing:

- The right stick needs a Playnite whose SDK reports analog stick values (SDK 6.18+, i.e. current Playnite 10). On older Playnite the timeline is mouse-only.
- The stick is only read while a UPS timeline is visible, and only while Playnite is the active window — a game in front never scrubs the music.
- A UPS timeline is picked up the first time its value changes, so within a second of music playing.
- Thin bars are hard to hit with a mouse. Give the bar a tall, transparent hit area and draw the thin line in its template — it stays a `ProgressBar`, so seeking keeps working:

```xml
<!-- 24 px to grab, drawn as a 4 px line; hover/drag thickens it and shows a knob at the playhead -->
<ProgressBar Minimum="0" Maximum="100" Height="24" Focusable="False"
             Value="{PluginSettings Plugin=UniPlaySong, Path=ActiveMediaProgress, Mode=OneWay}">
    <ProgressBar.Template>
        <ControlTemplate TargetType="ProgressBar">
            <Grid Background="Transparent">
                <Border x:Name="PART_Track" Height="4" CornerRadius="2" Background="#33FFFFFF" VerticalAlignment="Center" />
                <Grid x:Name="PART_Indicator" HorizontalAlignment="Left">
                    <Border x:Name="Fill" Height="4" CornerRadius="2" Background="White" VerticalAlignment="Center" />
                    <Ellipse x:Name="Knob" Width="14" Height="14" Fill="White" Opacity="0"
                             HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,-7,0" />
                </Grid>
            </Grid>
            <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="PART_Track" Property="Height" Value="6" />
                    <Setter TargetName="Fill" Property="Height" Value="6" />
                    <Setter TargetName="Knob" Property="Opacity" Value="1" />
                </Trigger>
                <Trigger Property="IsMouseCaptured" Value="True">
                    <Setter TargetName="PART_Track" Property="Height" Value="6" />
                    <Setter TargetName="Fill" Property="Height" Value="6" />
                    <Setter TargetName="Knob" Property="Opacity" Value="1" />
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
    </ProgressBar.Template>
</ProgressBar>
```

`PART_Track` and `PART_Indicator` are the names WPF's `ProgressBar` sizes the fill by — keep them.

**2. A draggable knob — bind a `Slider` two-way.**

```xml
<Slider Minimum="0" Maximum="100" Focusable="False" IsMoveToPointEnabled="True"
        Value="{PluginSettings Plugin=UniPlaySong, Path=ActiveMediaProgress, Mode=TwoWay}" />
```

UPS tells your writes apart from its own once-a-second progress updates, so only a drag or click seeks. A drag lands at most about ten seeks a second. Keep the `Slider` unfocusable in Fullscreen: a focused `Slider` takes D-pad Up/Down as value changes and traps navigation. Right-stick tilt follows `ProgressBar` timelines only, so a page with nothing but a `Slider` seeks by mouse (or add skip buttons, below).

**3. Skip buttons — the `seek` URIs.** Source-aware like `next`/`previous`:

| Command | URI |
|---|---|
| Skip forward / back by N seconds (signed) | `playnite://uniplaysong/seek/10`, `playnite://uniplaysong/seek/-10` |
| Jump to a point (0–100 % of the song) | `playnite://uniplaysong/seekto/50` |

Wire them like any other URI ([Firing a URI from a theme button](#firing-a-uri-from-a-theme-button)). A skip-back /
skip-forward pair for a Fullscreen player:

```xml
<!-- Needs xmlns:commands="clr-namespace:Playnite.Commands;assembly=Playnite" on the root element -->
<StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
    <!-- Back 10 s -->
    <ButtonEx Content="&#xEC78;" FontFamily="{StaticResource FontIcoFont}" FontSize="24"
              Width="56" Height="56" Margin="8,0"
              Command="{x:Static commands:GlobalCommands.NavigateUrlCommand}"
              CommandParameter="playnite://uniplaysong/seek/-10" />
    <!-- Forward 10 s -->
    <ButtonEx Content="&#xEC6E;" FontFamily="{StaticResource FontIcoFont}" FontSize="24"
              Width="56" Height="56" Margin="8,0"
              Command="{x:Static commands:GlobalCommands.NavigateUrlCommand}"
              CommandParameter="playnite://uniplaysong/seek/10" />
</StackPanel>
```

The glyphs are IcoFont's previous/next arrows (Playnite bundles the font as `FontIcoFont`); use any icon, text or your
theme's button style. Other useful parameters:

| Button | `CommandParameter` |
|---|---|
| Back 30 s | `playnite://uniplaysong/seek/-30` |
| Forward 5 s | `playnite://uniplaysong/seek/5` |
| Back to the start | `playnite://uniplaysong/seekto/0` |
| Halfway | `playnite://uniplaysong/seekto/50` |

Decimals work (`seek/2.5`). A seek is kept a second short of the end, so `seekto/100` won't skip the song. Both act
on whatever is playing — UPS music or Spotify — and do nothing for chiptunes, PSF, or when nothing is playing.

**4. A turntable — `UPS_SeekDisc`.** A transparent overlay you lay over your own cover art. Turn it to seek: drag around it with the mouse, or circle the right stick like a jog wheel. One full turn is 30 s; clockwise is forward. It draws nothing — your disc turns by binding a rotation to the element's `ScrubAngle` (degrees turned by seeking):

```xml
<Grid>
    <!-- Your disc: any look you like -->
    <Grid Width="300" Height="300" RenderTransformOrigin="0.5,0.5">
        <Grid.RenderTransform>
            <RotateTransform Angle="{Binding Content.ScrubAngle, ElementName=UPS_SeekDisc}" />
        </Grid.RenderTransform>
        <Ellipse>
            <Ellipse.Fill>
                <ImageBrush ImageSource="{Binding Source, ElementName=MyArt}" Stretch="UniformToFill" />
            </Ellipse.Fill>
        </Ellipse>
    </Grid>
    <!-- The seek surface: same size and place as the disc -->
    <ContentControl x:Name="UPS_SeekDisc" Width="300" Height="300" Focusable="False" />
</Grid>
```

- If your disc also spins on its own (a record turning while music plays), put both turns in a `TransformGroup` — the seek turn bound to `ScrubAngle`, the spin animated — and point the spin's storyboard at `(UIElement.RenderTransform).(TransformGroup.Children)[1].(RotateTransform.Angle)`.
- While a disc is on screen it owns the right stick (circle to turn); timelines go back to tilt-to-scrub when it's gone.
- `MyArt` above is an `Image` elsewhere on the page with its `Source` bound to `NowPlayingAlbumArtPath`. Don't bind `{PluginSettings}` straight into the brush in a Fullscreen **Main** view — see [Troubleshooting](#troubleshooting).

### Music visualizer — `UPS_MusicVisualControl` (v1.9.0+)

An animated visualizer that moves with the music, drawn with **real transparency** over whatever your
view shows behind it. It reacts only to the music UniPlaySong plays — never to game or system sounds.

```xml
<!-- Fill a page or a panel -->
<ContentControl x:Name="UPS_MusicVisualControl" Focusable="False" IsHitTestVisible="False" />
```

- **No setting to turn on.** It draws whenever your theme shows it and UniPlaySong plays through its effects player.
  On the lighter SDL2 player (the default when no effect needs more) it stays empty and costs nothing - **users turn on
  Live Effects** to see it. Mention this to your users if your theme relies on it. It's safe to ship unconditionally.
- **It never takes focus** and doesn't take mouse input, so it can't disturb controller navigation. Put any buttons in
  front of it as usual.
- **Size it the way you want it drawn.** It renders at the element's own size, so it stays sharp; the frame follows
  when the element is resized. One visualizer at a time — if a view has two, only one draws.
- **It runs only while visible.** Collapse it (or hide the page) and it stops; show it and it starts.
- **The centre is left for your cover art.** The visuals keep clear of a disc in the middle of the element — 30 % of
  its height across — so a page can put the album art there:

  ```xml
  <Grid>
      <ContentControl x:Name="UPS_MusicVisualControl" Focusable="False" IsHitTestVisible="False" />
      <!-- Cover art in the centre slot: 30 % of the visualizer's height -->
      <Grid>
          <Grid.RowDefinitions>
              <RowDefinition Height="35*" /><RowDefinition Height="30*" /><RowDefinition Height="35*" />
          </Grid.RowDefinitions>
          <Ellipse Grid.Row="1" HorizontalAlignment="Center"
                   Width="{Binding ActualHeight, RelativeSource={RelativeSource Self}}">
              <Ellipse.Fill>
                  <ImageBrush ImageSource="{Binding Source, ElementName=MyArt}" Stretch="UniformToFill" />
              </Ellipse.Fill>
          </Ellipse>
      </Grid>
  </Grid>
  ```

  (`MyArt` is an `Image` on the page with its `Source` bound to `NowPlayingAlbumArtPath`; see
  [Troubleshooting](#troubleshooting) for why the brush doesn't bind `{PluginSettings}` directly.)
- **Dark backgrounds suit it best.** Black is see-through and bright is solid, so the glow reads strongest over a dimmed
  backdrop.
- **No WPF effects while it runs.** Don't put a `BlurEffect` or `DropShadowEffect` over or around it. The visualizer
  runs in its own process to keep that kind of work off Playnite's UI thread and renderer, and an effect over a surface
  that changes every frame is re-run every frame. The visualizer draws its own glow, bloom and shade, including a soft
  shadow from the centre slot onto its light.

  Effects are fine as a **fallback**, for when the visualizer is off, missing, or UniPlaySong isn't installed. The
  element's `Content.IsRunning` is `True` only while it is drawing, so collapse the fallback on that:

  ```xml
  <Border.Style>
      <Style TargetType="Border">
          <Style.Triggers>
              <DataTrigger Binding="{Binding Content.IsRunning, ElementName=UPS_MusicVisualControl}" Value="True">
                  <Setter Property="Visibility" Value="Collapsed" />
              </DataTrigger>
          </Style.Triggers>
      </Style>
  </Border.Style>
  ```

  Trigger on `True`, not on `False`: without UniPlaySong the binding has nothing to read, and the fallback should still
  show.
- **Three styles.** Your page picks one (below); without a choice it's the Jukebox Fountain:
  - **Jukebox Fountain** (`JukeboxFountain`, the default): beads of light either side of the centre that learn each
    song's beat, sections, chords and bass line.
  - **Glow Bars** (`GlowBars`): slim bars either side of the centre, with a reflection and bloom.
  - **Fountain** (`Fountain`): columns of glowing sparks thrown up and down from the centre line.

  All keep the same centre slot, so a page works with any. Future styles will keep it too.
- **The Jukebox Fountain takes its colours from a picture.** `Palette` picks which:

  | `Palette` | Colours from | When that's missing |
  |---|---|---|
  | `Background` (or none) | the game's background - what fills the screen behind it | the cover, then the now-playing art |
  | `Cover` | the game's cover | the background, then the now-playing art |
  | `Disc` | the now-playing art (`NowPlayingAlbumArtPath`: the song's own art, else the game's cover) | the background |

  Pick the picture your page shows most of: a page over the game's background suits the default; a now-playing page
  with the art in the centre suits `Disc`. With no picture at all it uses its own palette (white to magenta). While
  Spotify plays, its album art is used whatever `Palette` says.
- **Style and palette go in the element's `Tag`**, as `Key=Value` parts separated by `;`. Case and spaces don't
  matter (`Glow Bars` works). A bare word is the palette, as in earlier builds (`Tag="Disc"`). A style UniPlaySong
  doesn't have falls back to the Jukebox Fountain - so a theme naming a style from a newer update still works on older
  ones. Changing the `Tag` at run time (a trigger, say) switches the style live.
- **Your own style, made in the [VisualizerPrototyper](https://ahuddini.github.io/UniPlaySong/VisualizerPrototyper/).**
  Drop in a song, start from a palette or a Showcase look, or make your own colours with **Your colours** (three
  pickers: the gradient's start, middle and end), and tune the motion. Press **Copy style for UPS** and paste it into a
  [Visualizer Style issue](https://github.com/aHuddini/UniPlaySong/issues/new?template=visualizer_style.yml) with a
  name. It ships in a UniPlaySong update; your theme then picks it by that name - `Tag="Style=Midnight Neon"`. Until
  that update, users see the Jukebox Fountain. Styles use UniPlaySong's own engine, so they are values only: colours,
  motion and effects, no code.

  ```xml
  <!-- Glow Bars, colours from the now-playing art -->
  <ContentControl x:Name="UPS_MusicVisualControl" Tag="Style=GlowBars; Palette=Disc"
                  Focusable="False" IsHitTestVisible="False" />
  <!-- the Jukebox Fountain, colours from the now-playing art -->
  <ContentControl x:Name="UPS_MusicVisualControl" Tag="Disc" Focusable="False" IsHitTestVisible="False" />
  ```
- **Cost:** it uses the graphics card while on screen — a few milliseconds a frame on integrated graphics at 1080p.

A complete Fullscreen example page — visualizer, centred spinning cover, seekable timeline, title and transport — is
in the UniPlaySong repository at `native/visualizer/demo-theme/UPSNowPlayingDemoFS-Main.xaml`.

### Music player with a spectrum — `UPS_MusicPlayerControlVisual` (v1.9.0+)

`UPS_MediaControllerOverlay` — the same PS5-style Now-Playing popup, the same buttons and gamepad handling — with a
strip of spectrum bars standing on its seek bar. The bars blend two colours from the album art the popup shows, the
card is the album art, blurred (once per cover, so it costs nothing per frame), under a dark tint of the art's colour,
and the seek bar has a knob at the song's position. Above the title, a pill names the game the song belongs to (or
Spotify); music no game owns - default music, a custom folder - shows none. It keeps a fixed 420 px width.

```xml
<ContentControl x:Name="UPS_MusicPlayerControlVisual" />
```

- **No setting to turn on.** It draws whenever your theme places it.
- **It moves with UniPlaySong's own music** (game music, default music, Radio Mode, chiptunes) **and with Spotify while
  Spotify Live Effects is on.** With nothing to hear it does nothing, and the seek bar shows plain.
- **SDL2 users:** UniPlaySong plays through its lighter SDL2 player unless a feature needs its effects player. On SDL2
  the strip stays plain — **turn on Live Effects** to see it. Mention this to your users if your theme relies on it.
- **The seek bar still seeks** — click, drag, right stick — as on the overlay. The strip never takes the mouse and never
  reaches over or below the bar.
- **One visualizer at a time.** While the popup is on screen it borrows the visualizer's renderer: a
  `UPS_MusicVisualControl` on the page behind it holds its last frame and picks up again when the popup closes.

#### The strip alone, for your own player — `UPS_MusicVisualStrip` (v1.9.0+)

Have your own music player? Put the same spectrum strip above **your** seek bar — nothing else changes:

```xml
<StackPanel>
    <ContentControl x:Name="UPS_MusicVisualStrip" Height="24" Margin="0,0,0,4"
                    Focusable="False" IsHitTestVisible="False" />
    <!-- your seek bar, as it is -->
    <ProgressBar Height="6" Minimum="0" Maximum="100"
                 Value="{PluginSettings Plugin=UniPlaySong, Path=ActiveMediaProgress}" />
</StackPanel>
```

- **Works where Playnite doesn't build the page too** - a template another add-on shows in its own window (a helper
  plugin's music player, say). Name the element `UPS_MusicVisualStrip` and UniPlaySong fills it when it loads.
- **Size it like the bar.** It draws at its own size: give it the bar's width (same column/panel) and a height of
  your choice (24 is what the popup uses). The bars rise from its bottom edge, so put it directly above the bar.
- **Keep its width steady.** A width change restarts it (a moment blank). A bar with the times *below* or beside it in
  fixed-width cells stays put; times that grow and shrink beside the bar shift it.
- **Colours** come from the now-playing art: its two most striking colours, blended up each bar (one-colour art
  gives dark-to-bright shades of it).
- Everything else is as for `UPS_MusicPlayerControlVisual`: no setting, it moves with UniPlaySong's music (and Spotify
  while captured), **SDL2 users need Live Effects on** (on SDL2 the strip stays empty — its space stays), and it borrows
  the renderer from a `UPS_MusicVisualControl` on the page while it's on screen.

---

## Compatibility with PlayniteSound

If your theme already uses PlayniteSound's `Sounds_MusicControl`, add UPS support alongside it. Both elements use the same `Tag` property pattern, so triggers can be identical:

```xml
<ContentControl x:Name="Sounds_MusicControl">
    <!-- your existing PlayniteSound triggers -->
</ContentControl>

<ContentControl x:Name="UPS_MusicControl">
    <!-- copy the same triggers -->
</ContentControl>
```

---

## Best practices

**Do:**

- Use `Tag="False"` as the default state.
- Set `Focusable="False"` to prevent navigation issues.
- Use specific trigger conditions (`Tag="Playing"`) over broad visibility checks (`IsVisible="True"`).
- Test in both Fullscreen and Desktop modes.

**Don't:**

- Don't set `Tag` to values other than `True`/`False`.
- Don't add multiple `UPS_MusicControl` elements (one is enough). For `UPS_MusicControl_PauseGamePlayDefault`, multiple instances are fine — they OR together.
- Don't assume music resumes instantly — there's a fade-in.

---

## Troubleshooting

**Music doesn't pause:**

1. Element name must be exactly `UPS_MusicControl` (or `UPS_MusicControl_PauseGamePlayDefault` for the swap variant).
2. Tag must be `"True"` (string) or `true` (boolean).
3. Enable debug logging in UPS settings → look for `[MusicControl] Tag changed` lines.

**Fullscreen crashes on startup after adding a trigger or brush:**

Playnite builds the Fullscreen **Main** view before extensions load, so a `{PluginSettings}` binding there starts empty and is filled in once UPS loads. On an ordinary property (`Text`, `Source`, `Tag`, `Value`) that works. Used directly as a `DataTrigger`'s `Binding`, or inside a brush (`ImageBrush.ImageSource`), the binding is already sealed by then and Playnite crashes with `Binding cannot be changed after it has been used` (in `playnite.log`). Views built later, like GameDetails, aren't affected. Put the value on an ordinary property and point at that instead:

```xml
<!-- A hidden element carries the value... -->
<FrameworkElement x:Name="UpsPlayState" Visibility="Collapsed"
                  Tag="{PluginSettings Plugin=UniPlaySong, Path=ActiveMediaIsPlaying}" />
<!-- ...and the trigger reads it -->
<DataTrigger Binding="{Binding Tag, ElementName=UpsPlayState}" Value="True"> ... </DataTrigger>
```

For a brush, bind to an `Image` that already shows the art: `ImageSource="{Binding Source, ElementName=MyArt}"`.

**Music doesn't resume:**

1. Tag must flip back to `"False"` when the overlay closes.
2. Check for conflicting triggers that keep Tag stuck at `"True"`.
3. Other pause sources may be active — focus loss, video detection, external audio. Check UPS log.

---

## Compatibility

| | |
|---|---|
| **UniPlaySong** | 1.1.9+ for `UPS_MusicControl`; 1.4.6+ for `{PluginSettings}`; 1.5.2+ for `UPS_BackgroundAudio`; 1.5.3+ for `UPS_MusicControl_PauseGamePlayDefault`; 1.5.7+ for live now-playing bindings (`NowPlayingTitle`/`NowPlayingArtist`/`NowPlayingAlbumArtPath`, plus Spotify-only `NowPlayingAlbum`/`NowPlayingGenre`/`NowPlayingDuration`); 1.5.7+ for the now-playing mini-player elements (UPS_NowPlayingMiniPlayer / UPS_NowPlayingMiniPlayerCompact); 1.5.8+ for the unified media controller elements (UPS_MediaControllerOverlay / UPS_MediaControllerBar / UPS_MediaControllerCompact), the `ActiveMedia*` data bindings, and the source-aware `playpausetoggle`/`next`/`previous`/`togglemute` URIs; 1.9.0+ for seeking (seekable `ActiveMediaProgress` timelines, two-way `Slider`, `seek`/`seekto` URIs, `UPS_SeekDisc`), `UPS_MediaControllerTransport`, `UPS_MusicPlayerControlVisual`, `UPS_MusicVisualStrip`, recent tracks (`UPS_SpotifyRecentTracks`/`UPS_GameRecentTracks`), `NowPlayingArtColor1`/`NowPlayingArtColor2`, and `UPS_MusicVisualControl` |
| **Playnite** | 10.x and 11.x, Fullscreen and Desktop |
| **PlayniteSound** | Coexists — both `Sounds_MusicControl` and `UPS_MusicControl` work in the same theme |
| **ANIKI REMAKE** | Fully supported reference theme |
| **PS5 Experience** (by saVantCZ) | Supported with **Settings → Theme Support → "PS5-Experience theme compatibility" enabled**. The theme drives `UPS_MusicControl_PauseGamePlayDefault` from PS5Core's `ActiveSection`/`IsWelcomeHubActive`; the compat option debounces the focus-driven Tag flicker so default music plays cleanly at the Welcome Hub. |

---

## Support

- **GitHub Issues:** https://github.com/aHuddini/UniPlaySong/issues
- **Feature Requests:** tag with `[Theme Support]`
