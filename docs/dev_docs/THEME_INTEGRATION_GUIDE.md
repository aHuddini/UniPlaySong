# UniPlaySong Theme Integration Guide

This guide explains how theme developers can integrate with UniPlaySong to control music playback during overlays, intro screens, and other custom UI elements.

## Overview

UniPlaySong exposes a **PluginControl** called `UPS_MusicControl` that allows themes to pause and resume music playback through XAML bindings. This follows the same pattern as PlayniteSound's `Sounds_MusicControl`.

**🎉 Special Thanks**: Huge thanks to **Mike Aniki** for his guidance and extensive testing help on this!

## Quick Start

Add this to your theme XAML:

```xml
<ContentControl x:Name="UPS_MusicControl" />
```

Then use XAML triggers to set `Tag="True"` (pause music) or `Tag="False"` (resume music).

## Control Reference

### Pause/resume control (UPS_MusicControl element)

| Property | Type | Description |
|---|---|---|
| `Tag` | object | Set to `"True"` to pause music, `"False"` to resume |
| `VideoIsPlaying` | bool | Read-only, reflects pause state (for binding) |
| `ThemeOverlayActive` | bool | Read-only, reflects pause state (for binding) |

These bind via the custom `<ContentControl x:Name="UPS_MusicControl"/>` element a theme places in its visual tree (see Examples 1–5 below). They drive the multi-source pause stack inside UPS to silence music while a theme overlay is active.

### Bindable settings via Playnite's `{PluginSettings}` markup (v1.4.6+)

For toggleable user settings (enable/disable music, radio mode, etc.), Playnite's first-class `{PluginSettings}` markup extension is the preferred approach — themes don't need any custom UPS element in their visual tree, the binding is deferred until plugins load (so themes don't crash when UPS isn't installed), and the syntax matches the pattern used by other Playnite plugins like Aniki Helper, Theme Options, and ExtraMetadataLoader.

**Markup syntax:**
```xml
<CheckBox IsChecked="{PluginSettings Plugin=UniPlaySong,
                                     Path=EnableMusic, Mode=TwoWay}"/>
```

UPS exposes its settings object under `Plugin=UniPlaySong, SettingsRoot=Settings`, so any property on `UniPlaySongSettings` is bindable directly via `Path=<PropertyName>`. The five most-toggled settings:

| `Path=` value | Type | Behavior when set true (or false) |
|---|---|---|
| `EnableMusic` | bool | Game-specific music stops when false. Default music continues if `EnableDefaultMusic` is true (UPS's longstanding fallback behavior — toggle both off for full silence). |
| `EnableDefaultMusic` | bool | Fallback ambient music stops when false. |
| `RadioModeEnabled` | bool | Disables continuous pool-based playback when false. |
| `PlayOnlyOnGameSelect` | bool | Music plays whenever browsing when false, not just on game selection. |
| `CalmDownModeEnabled` | bool | When true (v1.5.0+), applies a post-mixer low-pass filter + volume attenuation over a 1.5s S-curve fade. Useful for late-night browsing toggles. Auto-switches the player backend to NAudio when enabled; no song-restart needed (the processor lives on the persistent mixer chain). |

Other `UniPlaySongSettings` properties are also bindable via this mechanism — these five are simply the most useful ones for an audio quick-settings menu. See `src/UniPlaySongSettings.cs` for the full list.

**Why prefer this over a custom element:**

1. **No theme crash on missing UPS.** `{PluginSettings}` defers resolution until `ExtensionsLoaded` fires; if UPS isn't installed or is an older version that doesn't register settings support, bindings silently no-op. Themes that placed `<UPS:MusicControl>` directly with `xmlns:UPS=clr-namespace:UniPlaySong.Controls;assembly=UniPlaySong` would crash at theme-parse time because plugin assemblies load AFTER theme XAML.
2. **No xmlns boilerplate or assembly references** in theme XAML.
3. **Two-way sync** with the desktop UPS settings dialog — flipping a setting from one updates the other live (`UniPlaySongSettings` raises `INotifyPropertyChanged`).
4. **Persistence handled by UPS automatically** — assignments flow through UPS's existing PropertyChanged → settings-service → playback-coordinator pipeline.

See Example 6 below for a complete quick-audio-settings menu.

## Usage Examples

### Example 1: Simple Binding

Pause music when an overlay is visible:

```xml
<ContentControl x:Name="UPS_MusicControl"
    Tag="{Binding ElementName=MyOverlay, Path=IsVisible}" />
```

### Example 2: DataTrigger Style

Pause music during specific states:

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

### Example 3: MultiDataTrigger

Pause only when trailer is visible AND has audio:

```xml
<ContentControl x:Name="UPS_MusicControl">
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Setter Property="Tag" Value="False"/>
            <Style.Triggers>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding ElementName=TrailerContainer, Path=Opacity}" Value="1"/>
                        <Condition Binding="{Binding ElementName=VideoPlayer, Path=Content.IsPlayerMuted}" Value="False"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Tag" Value="True"/>
                </MultiDataTrigger>
            </Style.Triggers>
        </Style>
    </ContentControl.Style>
</ContentControl>
```

### Example 4: Login/Welcome Screen

Pause music during login and welcome screens:

```xml
<ContentControl x:Name="UPS_MusicControl">
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Setter Property="Tag" Value="False"/>
            <Style.Triggers>
                <!-- Pause during login screen -->
                <DataTrigger Binding="{Binding ElementName=LoginScreen, Path=Visibility}" Value="Visible">
                    <Setter Property="Tag" Value="True"/>
                </DataTrigger>

                <!-- Pause during welcome hub -->
                <DataTrigger Binding="{Binding ElementName=WelcomeHub, Path=IsOpen}" Value="True">
                    <Setter Property="Tag" Value="True"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ContentControl.Style>
</ContentControl>
```

### Example 5: ANIKI REMAKE Integration

Here is how UPS_MusicControl can be used to support a theme like ANIKI REMAKE:

```xml
<ContentControl x:Name="UPS_MusicControl">
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Setter Property="Tag" Value="False"/>
            <Style.Triggers>
                <!-- Introduction video, trailer, settings, and welcome control -->
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
                        <Condition Binding="{Binding ElementName=ExtraMetadataLoader_VideoLoaderControl_NoControls_Sound, Path=Content.IsPlayerMuted}" Value="False" />
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Tag" Value="True" />
                </MultiDataTrigger>

                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding ElementName=TrailerContainer, Path=Opacity}" Value="1"/>
                        <Condition Binding="{Binding ElementName=ExtraMetadataLoader_VideoLoaderControl_NoControls_Sound, Path=Content.IsPlayerMuted}" Value="True" />
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Tag" Value="False"/>
                </MultiDataTrigger>

                <DataTrigger Binding="{Binding ElementName=AcceuilSettings, Path=Visibility}" Value="Visible">
                    <Setter Property="Tag" Value="True" />
                </DataTrigger>

                <DataTrigger Binding="{Binding ElementName=WelcomeControl, Path=Tag}" Value="False">
                    <Setter Property="Tag" Value="True" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ContentControl.Style>
</ContentControl>
```

### UPS_MusicControl_PauseGamePlayDefault — Swap to default music (v1.5.3+)

A sibling element to `UPS_MusicControl`. Where `UPS_MusicControl` pauses **everything** when `Tag=True` (game music + default music both), `UPS_MusicControl_PauseGamePlayDefault` instead **swaps the current game's own music out for the user's selected default-music source** (Bundled Ambient, Random Game, Custom Folder — whatever they have chosen) while `Tag=True`, and restores game music when `Tag=False`.

**Use case:** background music keeps playing while the user interacts with a custom panel (tag editor, settings sidebar, property pane) — without stopping the music entirely or letting the current game's track stutter out and back in.

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

- `Tag="True"` → game's own songs are skipped; default music starts (or continues from the default-music branch UPS already manages)
- `Tag="False"` → game's own music returns on the next playback decision
- Multiple instances stack via OR — if **any** active instance has `Tag=True`, the override is active
- Works alongside `UPS_MusicControl` — they are independent flags. A pause request from `UPS_MusicControl` will still pause everything regardless of the override

Equivalent `{PluginSettings}` binding (no element required in the visual tree):

```xml
<CheckBox IsChecked="{PluginSettings Plugin=UniPlaySong,
                                    Path=ForceDefaultMusicOverride,
                                    Mode=TwoWay}"/>
```

Both forms target the same `ForceDefaultMusicOverride` runtime flag. The flag is `[JsonIgnore]` so it always starts `false` on Playnite startup — no risk of a previous session leaving the override stuck on.

### Example 6: Quick Audio Settings Menu (v1.4.6+) — `{PluginSettings}` Markup

For an in-theme audio quick-settings menu (toggles for "Enable Game Music," "Radio Mode," etc.), use Playnite's `{PluginSettings}` markup extension. **No `<UPS:MusicControl>` element is required** — the theme binds directly to UPS's settings by name, and the binding gracefully no-ops if UPS isn't installed.

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

    <!-- v1.5.0+ — low-pass + volume attenuation w/ 1.5s S-curve fade -->
    <CheckBox Content="Calm Down Mode"
              IsChecked="{PluginSettings Plugin=UniPlaySong,
                                         Path=CalmDownModeEnabled, Mode=TwoWay}"/>
</StackPanel>
```

> **Label vs. property name:** the underlying setting is `EnableMusic` (kept stable for binding compatibility), but UPS's own Fullscreen Extensions menu surfaces it as `Enable Game Music` to make the layer split obvious next to `Enable Default Music`. Themes are free to use either label — pick whichever fits the theme's wording. The `Path=EnableMusic` part is what matters for the binding.

**Behavior:**
- Two-way sync — user clicks the checkbox → setting flips → setting persists across restart → music engine reacts mid-playback. Conversely, flipping the same setting from the UPS desktop dialog or the Fullscreen Extensions menu updates the theme's checkbox state live.
- If UPS is uninstalled or older than v1.4.6: `{PluginSettings}` silently no-ops. The theme's checkboxes appear unchecked and clicking them does nothing — no crash, no error.
- Pattern matches what Aniki Helper and other Playnite plugins use for theme-bindable settings.
- Toggles work even when no game card is focused (welcome hub, search overlay, filter pages, the home view between filters). UPS falls back to "the most-recently played game" for context, so the toggle reacts immediately rather than waiting for the user to navigate to a game.

**Tip — full silence:** toggling only `EnableMusic` to false leaves default ambient music playing as a fallback (UPS's longstanding behavior; intentional). To fully silence UPS, also toggle `EnableDefaultMusic` off. Themes can offer "music while browsing" toggles (just `EnableMusic`) separately from "any music at all" toggles (both).

#### Two settings-write paths inside UPS (developer note)

UPS receives setting updates through two distinct event lanes, and `{PluginSettings}` writes use a different lane than the desktop settings dialog:

| Trigger | Mechanism | Event |
|---|---|---|
| Desktop dialog (Settings → UniPlaySong) | `BeginEdit` → mutate clone → `EndEdit` → `SettingsService.UpdateSettings(newClone)` | `SettingsChanged` (whole-settings replaced, exposes `OldSettings` + `NewSettings` for diffing) |
| Theme `{PluginSettings}` markup | Direct property setter on the live settings instance via `INotifyPropertyChanged` | `SettingPropertyChanged` (per-property, just `PropertyName`) |
| Fullscreen Extensions menu (Menu → Extensions → UniPlaySong) | `UpdateSettingsFromMenu` clones → mutates → calls `UpdateSettings` (same as desktop dialog) | `SettingsChanged` (same as desktop) |

**Why this matters:** until v1.4.6, only `VideoIsPlaying` and `ThemeOverlayActive` were routed through the per-property lane. Every other property write from a theme `{PluginSettings}` binding flipped the value successfully (the property setter ran, two-way sync worked) but no playback handler reacted, because the dialog-side handler only fires on the `SettingsChanged` event. v1.4.6 added explicit per-property handlers in `OnSettingsServicePropertyChanged` for four bindable settings (`EnableMusic`, `EnableDefaultMusic`, `RadioModeEnabled`, `PlayOnlyOnGameSelect`); v1.5.0 extended this to `CalmDownModeEnabled` (which can require a SDL2→NAudio backend swap when toggled ON). Theme writes now trigger the same playback decisions the dialog path does.

**For theme authors:** you don't need to do anything different — both lanes converge on the same playback behavior. This note is here so plugin authors who fork UPS or build similar plugins understand the dual-event-lane architecture and add per-property handlers when they expose new settings to themes.

#### Aniki ReMake — verified integration (v1.4.6 reference)

This is the exact paste-in used for the Aniki ReMake theme's audio quick-settings panel. Drop these four `<CheckBoxEx>` elements into `Views/AdditionalViews/QuickOptionsView.xaml`, inside the existing **Audio ControlCenter** `<Grid x:Name="Volume">`, immediately after the `Fullscreen.BackgroundVolume` slider's closing `</Grid>` and before the panel's closing `</StackPanel>`. Reusing the theme's own `SettingsSectionCheckbox` resource keeps the new toggles visually consistent with Mike's existing volume controls:

```xml
<!-- UniPlaySong audio toggles — bind via {PluginSettings}.        -->
<!-- Plugin=UniPlaySong is registered via AddSettingsSupport       -->
<!-- with SettingsRoot=Settings; Path is the property name only.   -->
<!-- Gracefully no-ops if UPS isn't installed (no crash).          -->
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

<!-- v1.5.0+ — low-pass filter + volume drop w/ smooth 1.5s S-curve fade -->
<CheckBoxEx Margin="0,10,0,0"
            HorizontalAlignment="Stretch"
            VerticalAlignment="Center"
            Style="{DynamicResource SettingsSectionCheckbox}"
            Content="Calm Down Mode"
            IsChecked="{PluginSettings Plugin=UniPlaySong, Path=CalmDownModeEnabled, Mode=TwoWay}" />
```

Notes for adapting this to other themes:

- **`CheckBoxEx` vs `CheckBox`**: `CheckBoxEx` is Playnite's controller-friendly variant (focus traversal works with D-Pad). Most Fullscreen themes use it. If your theme uses plain `CheckBox`, swap the element name — the `IsChecked` binding is identical.
- **`Style="{DynamicResource ...}"`**: substitute whatever checkbox style your theme exposes (Aniki ReMake calls it `SettingsSectionCheckbox`; other themes may name it differently). The `{PluginSettings}` binding doesn't care about the visual style.
- **Restart Playnite** after editing theme XAML — Fullscreen XAML is parsed at startup, so structural changes don't pick up via live-reload.

## How It Works

1. **Tag Changes**: When the `Tag` property changes, UniPlaySong detects it via a property change callback
2. **Pause Sources**: Each pause reason is tracked independently using a multi-source pause system
3. **Fade Effects**: Music fades out smoothly when paused, fades in when resumed
4. **No Conflicts**: Theme pause requests don't conflict with other pause reasons (focus loss, video detection, etc.)

### Technical Flow

```
Theme sets Tag="True"
    → MusicControl.OnTagChanged()
    → MusicControl.UpdateMute()
    → Settings.ThemeOverlayActive = true
    → MusicPlaybackCoordinator.HandleThemeOverlayChange(true)
    → MusicPlaybackService.AddPauseSource(ThemeOverlay)
    → MusicFader.Pause() [smooth fade-out]
    → Music pauses

Theme sets Tag="False"
    → MusicControl.OnTagChanged()
    → MusicControl.UpdateMute()
    → Settings.ThemeOverlayActive = false
    → MusicPlaybackCoordinator.HandleThemeOverlayChange(false)
    → MusicPlaybackService.RemovePauseSource(ThemeOverlay)
    → MusicFader.Resume() [smooth fade-in]
    → Music resumes
```

## Best Practices

### Do

- Use `Tag="False"` as the default state
- Set `Focusable="False"` to prevent navigation issues
- Use specific trigger conditions rather than broad visibility checks
- Test with both fullscreen and desktop modes

### Don't

- Don't set Tag to values other than `"True"` or `"False"` (or boolean equivalents)
- Don't use multiple `UPS_MusicControl` elements (one is sufficient)
- Don't assume music will resume instantly (there's a fade-in duration)

## Compatibility

- **Minimum Version**: UniPlaySong 1.1.9+
- **Playnite**: 10.x and 11.x (Fullscreen and Desktop modes)
- **ANIKI REMAKE**: Fully supported with special welcome hub and overlay handling
- **PlayniteSound Pattern**: Compatible with themes that use `Sounds_MusicControl`

## Migration from PlayniteSound

If your theme uses `Sounds_MusicControl`, you can add UniPlaySong support alongside:

```xml
<!-- PlayniteSound control -->
<ContentControl x:Name="Sounds_MusicControl">
    <!-- existing triggers -->
</ContentControl>

<!-- UniPlaySong control (same triggers) -->
<ContentControl x:Name="UPS_MusicControl">
    <!-- copy same triggers -->
</ContentControl>
```

Both controls use the same `Tag` property pattern, so triggers can be identical.

## Troubleshooting

### Music doesn't pause

1. Check that the control is named exactly `UPS_MusicControl`
2. Verify Tag is being set to `"True"` (string) or `true` (boolean)
3. Check UniPlaySong logs for `[MusicControl] Tag changed` entries

### Music doesn't resume

1. Ensure Tag is being set back to `"False"` when overlay closes
2. Check for conflicting triggers that might keep Tag="True"
3. Verify no other pause sources are active (check logs for pause source info)

### Debugging

Enable debug logging in UniPlaySong settings to see:
- `[MusicControl] Instance created`
- `[MusicControl] Tag changed: False -> True`
- `[MusicControl] Setting ThemeOverlayActive=true`
- `HandleThemeOverlayChange: ThemeOverlayActive=true - adding ThemeOverlay pause source`

## External Control via URI (v1.3.10+)

Themes can also control playback using Playnite's `playnite://` URI protocol. This is useful for custom media control buttons embedded in theme layouts.

**URI Format:** `playnite://uniplaysong/{command}`

| Command | URI |
|---------|-----|
| Play / Resume | `playnite://uniplaysong/play` |
| Pause | `playnite://uniplaysong/pause` |
| Toggle Play/Pause | `playnite://uniplaysong/playpausetoggle` |
| Skip to Next Song | `playnite://uniplaysong/skip` |
| Restart Current Song | `playnite://uniplaysong/restart` |
| Stop Playback | `playnite://uniplaysong/stop` |
| Set Volume (0-100) | `playnite://uniplaysong/volume/50` |

**When to use URIs vs UPS_MusicControl:**
- Use `UPS_MusicControl` for overlay/video pause/resume (automatic, state-driven via XAML bindings)
- Use URIs for explicit user actions like media control buttons (fire-and-forget)

## Active Theme UPS Audio (v1.3.11+, reworked in v1.5.2)

UniPlaySong can play a dedicated audio file from your theme through its own audio pipeline — with fade-in, volume control, and no conflicts with Playnite's built-in SDL player. Users enable this via **Settings → Playback → Default Music Source → "Use active theme's UPS audio file (advanced — theme support required)."**

### v1.5.2 — Why the filename changed

In v1.5.1 and earlier, UPS detected `background.{mp3,ogg,wav,flac}` in a theme's `audio/` folder. **Playnite's built-in SDL player opens the same file in fullscreen mode**, which caused:

- File-handle contention (one player gets a partial read or zero-length stream)
- Playback glitches, looping artifacts, and silence
- The infamous "0.16-second loop" reported by users on Aniki ReMake (Aniki ships an intentionally short `background.mp3` because its theme handles audio internally)

The fix as of v1.5.2 is a **strict filename convention**. UPS now ONLY reads `UPS_BackgroundAudio.{mp3,ogg,wav,flac}` from the theme's `audio/` folder. There is no fallback to `background.*` — that file belongs to Playnite, period.

### How to Add UPS Audio to Your Theme

Place a separate audio file named `UPS_BackgroundAudio.{mp3,ogg,wav,flac}` in your theme's `audio/` directory:

```
YourTheme_GUID/
├── audio/
│   ├── background.mp3         ← Playnite's SDL player reads this (unchanged)
│   ├── UPS_BackgroundAudio.mp3 ← UPS reads this (new in v1.5.2)
│   ├── activation.wav          (optional: UI sounds)
│   └── navigation.wav          (optional: UI sounds)
├── Views/
│   └── Main.xaml
└── theme.yaml
```

**Requirements:**
- File must be named `UPS_BackgroundAudio` (case-sensitive on case-sensitive filesystems; UPS matches case-insensitively but the canonical form is `UPS_BackgroundAudio`)
- One of these extensions: `.mp3`, `.ogg`, `.wav`, `.flac`
- File must be in the `audio/` subdirectory of your theme root
- Works with both user-installed themes (`%AppData%/Roaming/Playnite/Themes/Fullscreen/`) and the built-in default theme

**The two files can contain the same audio or different audio — your call.** Theme devs who want a unified experience ship the same track twice; theme devs who want a different ambient track when UPS is installed ship two different files.

### How It Works

1. UPS reads the active fullscreen theme ID from Playnite's settings
2. Scans the theme's `audio/` directory for `UPS_BackgroundAudio.{mp3,ogg,wav,flac}`
3. If found, plays the file through UPS's audio pipeline (NAudio or SDL2) with fade-in, volume integration, and proper pause/resume handling
4. If not found, UPS does NOT fall back to `background.*` — the user sees a state panel in the settings UI explaining the theme doesn't support this option, with a one-click "create UPS audio from theme's background file" helper if a `background.*` file is present

### Settings UI States (what the user sees)

The Active Theme UPS Audio radio button in Settings → Playback has four mutually exclusive states the user sees beneath it, depending on what UPS finds in the active theme:

| State | When | What the user sees |
|---|---|---|
| **Ready** | `UPS_BackgroundAudio.*` found in theme audio folder | ✓ "Detected: UPS_BackgroundAudio in `<theme name>`" |
| **CanBeCreated** | No UPS file, but `background.*` exists | ⚠ Warning + button: "Create UPS_BackgroundAudio from theme's background file" |
| **Unsupported** | Neither file present | ✗ "Not supported by your current theme — ask theme dev to add UPS_BackgroundAudio.*" |
| **NotApplicable** | No fullscreen theme could be resolved (Desktop-only, lookup failed) | ℹ "This option applies in Fullscreen mode." |

The **CanBeCreated** state is user-friendly fallback for the common case where a theme ships `background.mp3` but hasn't been updated for UPS yet. The user clicks the button, UPS copies the file as `UPS_BackgroundAudio.{same-extension}`, and from then on UPS plays the copy without touching the original.

### Supported Active Theme UPS Audio (v1.5.2)

| Theme | Ships `UPS_BackgroundAudio.*` | Works out-of-the-box | Works with copy-helper button |
|---|---|---|---|
| Playnite Default | No (ships `background.mp3` only) | No | Yes |
| Solaris | No (ships `background.mp3` only) | No | Yes |
| ANIKI REMAKE | No (uses WPF MediaElements; ships intentionally-short `background.mp3` stub) | No | Not recommended — the stub is 0.16 seconds. Use a different default music source (BundledPreset / Shades of Orange recommended) |
| Your theme | Add `audio/UPS_BackgroundAudio.mp3` | Yes | n/a |

### Notes for Theme Developers

- **No conflict with `UPS_MusicControl`**: The pause/resume system from `UPS_MusicControl` works independently of this audio file.
- **No conflict with Playnite's built-in SDL player**: Because UPS reads its own dedicated file, Playnite's SDL player continues to handle `background.mp3` as usual. Users with UPS installed but a different default music source selected still hear Playnite's normal theme audio.
- **Login screen audio**: If your theme uses `background.mp3` for login-screen ambient music (Playnite handles that natively), UPS does NOT interfere. UPS only activates Active Theme UPS Audio when the user is at the game library, not the login screen. Themes that handle login audio via WPF `MediaElement` (like Aniki ReMake) continue to work as before.
- **Migration for existing themes**: If your theme historically shipped `background.mp3` for the v1.5.1-and-earlier Active Theme Music feature, the simplest migration is to ship the same file twice — once as `background.mp3` (for Playnite + legacy UPS users) and once as `UPS_BackgroundAudio.mp3` (for v1.5.2+ UPS users). Or rely on users clicking the copy-helper button.

## Support

- **GitHub Issues**: https://github.com/aHuddini/UniPlaySong/issues
- **Feature Requests**: Tag with `[Theme Support]` label
