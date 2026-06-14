# UniPlaySong Theme Integration Guide

How to wire your Playnite Fullscreen theme into UniPlaySong's music playback.

**Special thanks to Mike Aniki** for guidance and testing on this.

---

## What you can do with UPS

| Goal | Tool |
|---|---|
| Pause UPS music while an overlay / video / login screen is up | `UPS_MusicControl` element |
| Swap game music for the user's default music while a custom panel is open (tag editor, sidebar, etc.) — restore game music on close | `UPS_MusicControl_PauseGamePlayDefault` element (v1.5.3+) |
| Bind UPS settings (Enable Music, Radio Mode, Calm Down, etc.) to checkboxes in your theme | `{PluginSettings}` markup (v1.4.6+) |
| Ship a dedicated audio track that UPS plays as default music | `UPS_BackgroundAudio.mp3` in your theme's `audio/` folder (v1.5.2+) |
| Custom media-control buttons (play, pause, skip, volume) | `playnite://uniplaysong/...` URIs (v1.3.10+) |

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
| `PlayOnlyOnGameSelect` | bool | When false, music plays while browsing too |
| `CalmDownModeEnabled` | bool | v1.5.0+ — gentle muffle + dim over 1.5s (great for late-night browsing toggles) |

Any property on `UniPlaySongSettings` is bindable this way — these five are just the most useful for a quick-options menu.

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

**When to use which:**

- `UPS_MusicControl` — state-driven pause/resume tied to UI visibility
- URI — explicit user actions (a "Skip" button on a media bar)

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

**Music doesn't resume:**

1. Tag must flip back to `"False"` when the overlay closes.
2. Check for conflicting triggers that keep Tag stuck at `"True"`.
3. Other pause sources may be active — focus loss, video detection, external audio. Check UPS log.

---

## Compatibility

| | |
|---|---|
| **UniPlaySong** | 1.1.9+ for `UPS_MusicControl`; 1.4.6+ for `{PluginSettings}`; 1.5.2+ for `UPS_BackgroundAudio`; 1.5.3+ for `UPS_MusicControl_PauseGamePlayDefault` |
| **Playnite** | 10.x and 11.x, Fullscreen and Desktop |
| **PlayniteSound** | Coexists — both `Sounds_MusicControl` and `UPS_MusicControl` work in the same theme |
| **ANIKI REMAKE** | Fully supported reference theme |

---

## Support

- **GitHub Issues:** https://github.com/aHuddini/UniPlaySong/issues
- **Feature Requests:** tag with `[Theme Support]`
