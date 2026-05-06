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

UPS exposes its settings object under `Plugin=UniPlaySong, SettingsRoot=Settings`, so any property on `UniPlaySongSettings` is bindable directly via `Path=<PropertyName>`. The four most-toggled settings:

| `Path=` value | Type | Behavior when set false |
|---|---|---|
| `EnableMusic` | bool | Game-specific music stops. Default music continues if `EnableDefaultMusic` is true (UPS's longstanding fallback behavior — toggle both off for full silence). |
| `EnableDefaultMusic` | bool | Fallback ambient music stops. |
| `RadioModeEnabled` | bool | Disables continuous pool-based playback. |
| `PlayOnlyOnGameSelect` | bool | Music plays whenever browsing, not just on game selection. |

Other `UniPlaySongSettings` properties are also bindable via this mechanism — these four are simply the most useful ones for an audio quick-settings menu. See `src/UniPlaySongSettings.cs` for the full list.

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
</StackPanel>
```

> **Label vs. property name:** the underlying setting is `EnableMusic` (kept stable for binding compatibility), but UPS's own Fullscreen Extensions menu surfaces it as `Enable Game Music` to make the layer split obvious next to `Enable Default Music`. Themes are free to use either label — pick whichever fits the theme's wording. The `Path=EnableMusic` part is what matters for the binding.

**Behavior:**
- Two-way sync — user clicks the checkbox → setting flips → setting persists across restart → music engine reacts mid-playback. Conversely, flipping the same setting from the UPS desktop dialog or the Fullscreen Extensions menu updates the theme's checkbox state live.
- If UPS is uninstalled or older than v1.4.6: `{PluginSettings}` silently no-ops. The theme's checkboxes appear unchecked and clicking them does nothing — no crash, no error.
- Pattern matches what Aniki Helper and other Playnite plugins use for theme-bindable settings.

**Tip — full silence:** toggling only `EnableMusic` to false leaves default ambient music playing as a fallback (UPS's longstanding behavior; intentional). To fully silence UPS, also toggle `EnableDefaultMusic` off. Themes can offer "music while browsing" toggles (just `EnableMusic`) separately from "any music at all" toggles (both).

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

## Active Theme Music (v1.3.11+)

UniPlaySong can detect and play your theme's background music through its own audio pipeline — with fade-in, volume control, and no conflicts with Playnite's native SDL2 player. Users enable this via **Settings > General > Default Music Source > "Use active fullscreen theme's background music (If Available)."**

### How to Add Background Music to Your Theme

Place an audio file named `background.mp3` in your theme's `audio/` directory:

```
YourTheme_GUID/
├── audio/
│   ├── background.mp3      ← UPS detects this file
│   ├── activation.wav       (optional: UI sounds)
│   └── navigation.wav       (optional: UI sounds)
├── Views/
│   └── Main.xaml
└── theme.yaml
```

**Requirements:**
- File must be named `background` with one of these extensions: `.mp3`, `.ogg`, `.wav`, `.flac`
- File must be in the `audio/` subdirectory of your theme root
- Works with both user-installed themes (`%AppData%/Roaming/Playnite/Themes/Fullscreen/`) and the built-in default theme

**That's it.** No XAML changes, no plugin references, no code. UPS will automatically find and play the file when the user selects this option.

### How It Works

1. UPS reads the active fullscreen theme ID from Playnite's settings
2. Scans the theme's directory for `audio/background.*`
3. Plays the file through UPS's audio pipeline (NAudio or SDL2) with fade-in, volume integration, and proper pause/resume handling
4. Playnite's native SDL2 playback of the same file is suppressed to avoid overlap

### Supported Active Theme Music

| Theme | Has `background.mp3` | Works with Active Theme Music |
|-------|----------------------|-------------------------------|
| Playnite Default | Yes | Yes |
| Solaris | Yes | Yes |
| ANIKI REMAKE | No (uses WPF MediaElements) | No — use other default music options |
| Your theme | Add `audio/background.mp3` | Yes |

### Notes for Theme Developers

- **No conflict with UPS_MusicControl**: The `UPS_MusicControl` pause/resume system works independently. If your theme uses overlays or intros that pause music, those still work as expected.
- **No conflict with Playnite's native playback**: When Active Theme Music is enabled, UPS suppresses Playnite's native SDL2 playback of `background.mp3` and handles it directly. Users without UPS (or with a different default music option) will hear the file through Playnite's native player as usual.
- **Login screen audio**: If your theme relies on `background.mp3` as ambient login screen music, note that UPS intially suppresses native playback when transitioning to fullscreen. Users who want theme login audio should not use UPS's suppress option, or the theme should use WPF `MediaElement` for login-specific audio (like ANIKI REMAKE does).

## Support

- **GitHub Issues**: https://github.com/aHuddini/UniPlaySong/issues
- **Feature Requests**: Tag with `[Theme Support]` label
