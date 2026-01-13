# UniPlaySong Theme Integration Guide

This guide explains how theme developers can integrate with UniPlaySong to control music playback during overlays, intro screens, and other custom UI elements.

## Overview

UniPlaySong exposes a **PluginControl** called `UPS_MusicControl` that allows themes to pause and resume music playback through XAML bindings. This follows the same pattern as PlayniteSound's `Sounds_MusicControl`.

## Quick Start

Add this to your theme XAML:

```xml
<ContentControl x:Name="UPS_MusicControl" />
```

Then use XAML triggers to set `Tag="True"` (pause music) or `Tag="False"` (resume music).

## Control Reference

| Property | Type | Description |
|----------|------|-------------|
| `Tag` | object | Set to `"True"` to pause music, `"False"` to resume |
| `VideoIsPlaying` | bool | Read-only, reflects pause state (for binding) |
| `ThemeOverlayActive` | bool | Read-only, reflects pause state (for binding) |

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

## Support

- **GitHub Issues**: https://github.com/aHuddini/UniPlaySong/issues
- **Feature Requests**: Tag with `[Theme Support]` label
