# Icon Glow Feature Design

## Goal

Add a pulsating colored glow border around the selected game's small icon (`PART_ImageIcon`) in Playnite Desktop mode. The glow color is extracted from the game's icon art. When NAudio is active, the pulse is audio-reactive (driven by amplitude). When SDL2 is active, the pulse is a fixed gentle sinusoidal cycle (optional, can be disabled for a static glow).

## Architecture

### Module Structure

```
src/IconGlow/
├── IconGlowManager.cs      — Orchestrator (lifecycle, timer, amplitude reading)
├── IconColorExtractor.cs    — Extracts dominant color from game icon art
└── TileFinder.cs            — Visual tree walker for locating PART_ImageIcon
```

### Component Responsibilities

**IconGlowManager** — Created in `UniPlaySong.cs` during `OnApplicationStarted()` when `EnableIconGlow` is true. Listens for game selection changes. On selection: uses TileFinder to locate the icon, IconColorExtractor for color, wraps the icon in a Border, drives opacity via DispatcherTimer at ~60fps (~16ms interval). Reads amplitude from `VisualizationDataProvider` (NAudio) or runs fixed pulse (SDL2). Cleans up on deselection or disposal.

**IconColorExtractor** — Pure utility. Takes a `BitmapSource` from the icon's `Source` property, samples a grid of pixels (every 4th pixel), tallies hue clusters, returns the most vibrant/saturated color. Caches results per game ID. Falls back to a default accent color (soft blue) if no icon or monochrome.

**TileFinder** — Encapsulates `VisualTreeHelper` logic. Finds `PART_ImageIcon` in the currently selected game's view. Handles Details View list items (variable size), Details View header (48x48), and Grid View sidebar header (32x32). Follows existing patterns from `ControllerOverlay.cs` and `MediaElementsMonitor.cs`.

## Rendering

### Target Element

The small game icon (`PART_ImageIcon`) — an `Image` control inside a `DockPanel`, present in Desktop Details View and Grid View sidebar.

### Glow Border Injection

1. Find `PART_ImageIcon` via VisualTreeHelper
2. Remove the `Image` from its parent `DockPanel`
3. Create a `Border` with:
   - `BorderBrush`: `SolidColorBrush` of the extracted dominant color
   - `BorderThickness`: 2
   - `CornerRadius`: 4
   - `Opacity`: driven by timer (0.3–0.8 range)
4. Set the `Image` as `Border.Child`
5. Insert the `Border` back into the `DockPanel` at the same position
6. Tag with `Name = "UPS_IconGlow"` for reliable find/remove

### Pulse Modes

**NAudio (audio-reactive):** Each timer tick reads RMS/peak amplitude from `VisualizationDataProvider`, maps to 0.3–0.8 opacity range. Glow brightens on loud passages, dims on quiet.

**SDL2 (fixed pulse):** `opacity = 0.3 + 0.25 * (1 + sin(2π * elapsed / 2.0))` — gentle ~2 second cycle. Gated by `EnableIconGlowPulse` setting; when off, glow is static at 0.5 opacity.

## Settings

Added to `UniPlaySongSettings.cs`, under Experimental tab:

- `EnableIconGlow` (bool, default `false`) — Master toggle
- `EnableIconGlowPulse` (bool, default `true`) — SDL2 fixed pulse on/off

## Scope & Limitations

- **Desktop only** — Fullscreen mode doesn't render `PART_ImageIcon`. Fullscreen glow (on cover art thumbnail) deferred to future work.
- **No WPF animations** — All opacity changes driven by DispatcherTimer at ~60fps, no Storyboard/DoubleAnimation.
- **Single tile** — Only one glow border exists at a time (the selected game).
- **Virtualization** — If user scrolls away, the glow border disappears with the recycled container. Re-injected on next selection change.

## Integration

```
UniPlaySong.cs (OnApplicationStarted)
  → creates IconGlowManager(playniteApi, settings, vizDataProvider?)
    → IconGlowManager uses TileFinder (visual tree)
    → IconGlowManager uses IconColorExtractor (color)
    → IconGlowManager reads VisualizationDataProvider (NAudio amplitude, nullable)
```

## Dependencies

- `VisualizationDataProvider` (existing, NAudio-only) — for audio-reactive mode
- `VisualTreeHelper` (WPF built-in) — for finding PART_ImageIcon
- `BitmapSource` pixel access (WPF built-in) — for color extraction
- No external libraries required
