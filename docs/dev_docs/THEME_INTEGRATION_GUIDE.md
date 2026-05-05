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

### Pause/resume control (original surface)

| Property | Type | Direction | Description |
|---|---|---|---|
| `Tag` | object | Theme → UPS | Set to `"True"` to pause music, `"False"` to resume |
| `VideoIsPlaying` | bool | UPS → Theme | Read-only, reflects pause state (for binding) |
| `ThemeOverlayActive` | bool | UPS → Theme | Read-only, reflects pause state (for binding) |

### User-setting toggles (v1.4.6+)

Four of UPS's most-toggled persistent settings are exposed as **two-way** bindable properties so themes can wire `CheckBox` / `ToggleButton` `IsChecked` directly to them. Setter assignments flow through the existing `UniPlaySongSettings` PropertyChanged → settings-service → playback-coordinator pipeline, so a theme toggle has the same effect — and persistence — as flipping the corresponding setting in the desktop UPS settings dialog.

| Property | Type | Direction | Description |
|---|---|---|---|
| `EnableGameMusic` | bool | TwoWay | Game music enable/disable. False = no game-specific music plays. **Default music still plays as a fallback** unless `EnableDefaultMusic` is also false. |
| `EnableDefaultMusic` | bool | TwoWay | Default music enable/disable. False = no fallback ambient music plays when a game has no music of its own. Pair with `EnableGameMusic` for independent control of both audio layers. |
| `RadioModeEnabled` | bool | TwoWay | Radio Mode — pool-based continuous playback that auto-advances through random games' songs. |
| `PlayOnlyOnGameSelect` | bool | TwoWay | Play music only when the user actively selects a game (suppresses ambient music in list view). |

> **Note on `EnableGameMusic` naming:** the theme-facing property is named `EnableGameMusic` for clarity, but maps to the underlying C# settings field `_settings.EnableMusic` (the legacy name, preserved on the settings type for backward-compat with existing user `config.ini` files). The behavior is unchanged from previous versions — toggling `EnableGameMusic` to false suppresses game music while letting default music continue if `EnableDefaultMusic` is on. To fully silence UPS, toggle both off.

`INotifyPropertyChanged` keeps theme widgets in sync if the user changes the same setting via the UPS desktop settings dialog (or the Fullscreen Extensions menu).

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

### Example 6: Quick Audio Settings Menu (v1.4.6+)

Bind theme `CheckBox` / `ToggleButton` widgets directly to UPS settings — useful for an in-theme audio quick-settings menu like the one in Aniki ReMake. A single hidden `UPS_MusicControl` element acts as the binding source; place it once anywhere in the visual tree.

```xml
<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:UPS="clr-namespace:UniPlaySong.Controls;assembly=UniPlaySong">

    <!-- Hidden binding source. Place once in the tree. -->
    <UPS:MusicControl x:Name="upsAudio" Visibility="Collapsed"/>

    <!-- Toggles bind to UPS settings via TwoWay binding. -->
    <StackPanel>
        <CheckBox Content="Enable Game Music"
                  IsChecked="{Binding ElementName=upsAudio,
                                      Path=EnableGameMusic, Mode=TwoWay}"/>

        <CheckBox Content="Enable Default (Ambient) Music"
                  IsChecked="{Binding ElementName=upsAudio,
                                      Path=EnableDefaultMusic, Mode=TwoWay}"/>

        <CheckBox Content="Radio Mode"
                  IsChecked="{Binding ElementName=upsAudio,
                                      Path=RadioModeEnabled, Mode=TwoWay}"/>

        <CheckBox Content="Play Only on Game Select"
                  IsChecked="{Binding ElementName=upsAudio,
                                      Path=PlayOnlyOnGameSelect, Mode=TwoWay}"/>
    </StackPanel>
</UserControl>
```

**Behavior:**
- User clicks a checkbox → UPS setting flips → setting persists across restarts → music engine reacts mid-playback (e.g., game music stops if `EnableGameMusic` flipped off).
- User flips the same setting in the UPS desktop settings dialog or the Fullscreen Extensions menu → the theme's checkbox state updates live.
- If `UPS_MusicControl` isn't loaded (UPS not installed, theme outside Fullscreen), bindings silently no-op — no crash, the checkboxes just won't do anything. Standard Playnite custom-element fallback.

**Tip — full silence:** toggling only `EnableGameMusic` to false leaves default ambient music playing as a fallback. To fully silence UPS, also toggle `EnableDefaultMusic` off. This split lets a theme offer "music while browsing" toggles separately from "any music at all" toggles.

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
