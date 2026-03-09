# Icon Glow Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a pulsating colored glow border around the selected game's small icon in Playnite Desktop mode, with audio-reactive pulse (NAudio) or fixed gentle pulse (SDL2).

**Architecture:** A new `IconGlow/` module with three classes: `TileFinder` (visual tree walking), `IconColorExtractor` (dominant color from icon bitmap), and `IconGlowManager` (orchestrator — listens for game selection, injects/removes glow border, drives opacity via DispatcherTimer at ~60fps). The manager reads amplitude from `VisualizationDataProvider.Current` when NAudio is active, falls back to sinusoidal pulse for SDL2.

**Tech Stack:** C# / .NET 4.6.2, WPF VisualTreeHelper, BitmapSource pixel access, DispatcherTimer, existing VisualizationDataProvider infrastructure.

---

## Task 1: Create `TileFinder` — Visual tree walker for PART_ImageIcon

**Files:**
- Create: `src/IconGlow/TileFinder.cs`

**Step 1: Create the file**

Create `src/IconGlow/TileFinder.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;

namespace UniPlaySong.IconGlow
{
    // Finds PART_ImageIcon in Playnite's Desktop visual tree.
    // Searches Details View list/header and Grid View sidebar header.
    public static class TileFinder
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Finds the PART_ImageIcon Image control for the currently visible selected game.
        // Returns null if not found (e.g., Fullscreen mode, or icon not rendered yet).
        public static Image FindSelectedGameIcon(DependencyObject root)
        {
            if (root == null) return null;

            try
            {
                // Search for PART_ImageIcon in the visual tree
                return FindChildByName<Image>(root, "PART_ImageIcon");
            }
            catch (System.Exception ex)
            {
                Logger.Error(ex, "[IconGlow] Error finding game icon in visual tree");
                return null;
            }
        }

        // Finds the parent DockPanel of the given element (needed for border injection).
        public static DockPanel FindParentDockPanel(DependencyObject child)
        {
            if (child == null) return null;

            try
            {
                var parent = VisualTreeHelper.GetParent(child);
                while (parent != null)
                {
                    if (parent is DockPanel dockPanel)
                        return dockPanel;
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error(ex, "[IconGlow] Error finding parent DockPanel");
            }
            return null;
        }

        private static T FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T element && element.Name == name)
                    return element;

                var result = FindChildByName<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
}
```

**Step 2: Build to verify**

```bash
dotnet build -c Release
```
Expected: `Build succeeded. 0 Error(s)`

**Step 3: Commit**

```bash
git add src/IconGlow/TileFinder.cs
git commit -m "feat: add TileFinder for locating PART_ImageIcon in visual tree"
```

---

## Task 2: Create `IconColorExtractor` — Dominant color from game icon

**Files:**
- Create: `src/IconGlow/IconColorExtractor.cs`

**Step 1: Create the file**

Create `src/IconGlow/IconColorExtractor.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Playnite.SDK;

namespace UniPlaySong.IconGlow
{
    // Extracts the dominant color from a game icon's BitmapSource.
    // Samples pixels on a grid, clusters by hue, picks the most vibrant result.
    // Caches per game ID to avoid re-sampling on repeated selection.
    public class IconColorExtractor
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static readonly Color DefaultGlowColor = Color.FromRgb(100, 149, 237); // Cornflower blue

        private readonly Dictionary<Guid, Color> _cache = new Dictionary<Guid, Color>();

        // Extracts dominant color from the icon's bitmap. Returns cached result if available.
        public Color GetDominantColor(Guid gameId, ImageSource imageSource)
        {
            if (_cache.TryGetValue(gameId, out var cached))
                return cached;

            var color = ExtractFromBitmap(imageSource as BitmapSource);
            _cache[gameId] = color;
            return color;
        }

        public void ClearCache() => _cache.Clear();

        private Color ExtractFromBitmap(BitmapSource bitmap)
        {
            if (bitmap == null)
                return DefaultGlowColor;

            try
            {
                // Convert to a known pixel format for consistent access
                var formatted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int width = formatted.PixelWidth;
                int height = formatted.PixelHeight;

                if (width == 0 || height == 0)
                    return DefaultGlowColor;

                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                formatted.CopyPixels(pixels, stride, 0);

                // Sample every 4th pixel for performance
                int step = 4;
                int bestHue = 0;
                double bestScore = 0;
                int[] hueBuckets = new int[360];
                double[] hueScores = new double[360];
                double totalR = 0, totalG = 0, totalB = 0;
                int colorCount = 0;

                for (int y = 0; y < height; y += step)
                {
                    for (int x = 0; x < width; x += step)
                    {
                        int offset = y * stride + x * 4;
                        byte b = pixels[offset];
                        byte g = pixels[offset + 1];
                        byte r = pixels[offset + 2];
                        byte a = pixels[offset + 3];

                        if (a < 128) continue; // Skip transparent pixels

                        var hsv = RgbToHsv(r, g, b);

                        // Skip near-grayscale pixels (low saturation or very dark/bright)
                        if (hsv.saturation < 0.15 || hsv.value < 0.1 || hsv.value > 0.95)
                            continue;

                        int hue = (int)hsv.hue % 360;
                        double score = hsv.saturation * hsv.value;
                        hueBuckets[hue]++;
                        hueScores[hue] += score;

                        totalR += r;
                        totalG += g;
                        totalB += b;
                        colorCount++;
                    }
                }

                if (colorCount == 0)
                    return DefaultGlowColor;

                // Find the hue bucket with highest combined score (frequency * vibrancy)
                // Use a sliding window of 30 degrees for hue clustering
                double bestWindowScore = 0;
                int bestWindowCenter = 0;

                for (int center = 0; center < 360; center++)
                {
                    double windowScore = 0;
                    for (int offset = -15; offset <= 15; offset++)
                    {
                        int idx = (center + offset + 360) % 360;
                        windowScore += hueScores[idx];
                    }
                    if (windowScore > bestWindowScore)
                    {
                        bestWindowScore = windowScore;
                        bestWindowCenter = center;
                    }
                }

                // Build average color from pixels in the winning hue window
                double avgR = 0, avgG = 0, avgB = 0;
                int winCount = 0;

                for (int y = 0; y < height; y += step)
                {
                    for (int x = 0; x < width; x += step)
                    {
                        int pxOffset = y * stride + x * 4;
                        byte pb = pixels[pxOffset];
                        byte pg = pixels[pxOffset + 1];
                        byte pr = pixels[pxOffset + 2];
                        byte pa = pixels[pxOffset + 3];

                        if (pa < 128) continue;

                        var hsv = RgbToHsv(pr, pg, pb);
                        if (hsv.saturation < 0.15) continue;

                        int hue = (int)hsv.hue % 360;
                        int diff = Math.Abs(hue - bestWindowCenter);
                        if (diff > 180) diff = 360 - diff;
                        if (diff > 15) continue;

                        avgR += pr;
                        avgG += pg;
                        avgB += pb;
                        winCount++;
                    }
                }

                if (winCount == 0)
                    return DefaultGlowColor;

                // Boost saturation slightly for a more vivid glow
                byte finalR = (byte)Math.Min(255, avgR / winCount * 1.2);
                byte finalG = (byte)Math.Min(255, avgG / winCount * 1.2);
                byte finalB = (byte)Math.Min(255, avgB / winCount * 1.2);

                return Color.FromRgb(finalR, finalG, finalB);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[IconGlow] Error extracting dominant color from icon");
                return DefaultGlowColor;
            }
        }

        private static (double hue, double saturation, double value) RgbToHsv(byte r, byte g, byte b)
        {
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;

            double hue = 0;
            if (delta > 0)
            {
                if (max == rd) hue = 60 * (((gd - bd) / delta) % 6);
                else if (max == gd) hue = 60 * (((bd - rd) / delta) + 2);
                else hue = 60 * (((rd - gd) / delta) + 4);
            }
            if (hue < 0) hue += 360;

            double saturation = max > 0 ? delta / max : 0;
            return (hue, saturation, max);
        }
    }
}
```

**Step 2: Build to verify**

```bash
dotnet build -c Release
```
Expected: `Build succeeded. 0 Error(s)`

**Step 3: Commit**

```bash
git add src/IconGlow/IconColorExtractor.cs
git commit -m "feat: add IconColorExtractor — dominant color from game icon bitmap"
```

---

## Task 3: Add settings properties for Icon Glow

**Files:**
- Modify: `src/UniPlaySongSettings.cs` — add 2 new properties near Experimental section
- Modify: `src/UniPlaySongSettingsView.xaml` — add UI controls to Experimental tab
- Modify: `src/UniPlaySongSettingsView.xaml.cs` — add to ResetExperimentalTab_Click

**Step 1: Add backing fields and properties to UniPlaySongSettings.cs**

Find the Experimental section in `src/UniPlaySongSettings.cs` (search for other experimental settings or the end of the settings properties). Add:

```csharp
private bool enableIconGlow = false;
private bool enableIconGlowPulse = true;

// Pulsating glow border around the selected game's icon in Desktop mode.
// Color is extracted from the game's icon art. Audio-reactive when NAudio is active.
public bool EnableIconGlow
{
    get => enableIconGlow;
    set { enableIconGlow = value; OnPropertyChanged(); }
}

// When SDL2 backend is active (no audio data), enable a gentle fixed-rate pulse.
// When off, the glow is static. Ignored when NAudio is active (always audio-reactive).
public bool EnableIconGlowPulse
{
    get => enableIconGlowPulse;
    set { enableIconGlowPulse = value; OnPropertyChanged(); }
}
```

**Step 2: Add XAML controls to the Experimental tab**

In `src/UniPlaySongSettingsView.xaml`, find the Experimental tab content. Add:

```xml
<!-- Icon Glow -->
<CheckBox Content="Enable Icon Glow (Desktop only)"
          IsChecked="{Binding Settings.EnableIconGlow}"
          Margin="0,12,0,4"
          ToolTip="Adds a pulsating colored border around the selected game's icon. Color is extracted from the game's icon art. Audio-reactive when Live Effects/Visualizer is enabled." />
<CheckBox Content="Enable pulse animation (SDL2 fallback)"
          IsChecked="{Binding Settings.EnableIconGlowPulse}"
          Margin="20,0,0,4"
          IsEnabled="{Binding Settings.EnableIconGlow}"
          ToolTip="When Live Effects/Visualizer is off, the glow pulses at a gentle fixed rate. Disable for a static glow." />
```

**Step 3: Update ResetExperimentalTab_Click**

In `src/UniPlaySongSettingsView.xaml.cs`, inside the Experimental tab reset handler, add:

```csharp
s.EnableIconGlow = false;
s.EnableIconGlowPulse = true;
```

**Step 4: Build to verify**

```bash
dotnet build -c Release
```
Expected: `Build succeeded. 0 Error(s)`

**Step 5: Commit**

```bash
git add src/UniPlaySongSettings.cs src/UniPlaySongSettingsView.xaml src/UniPlaySongSettingsView.xaml.cs
git commit -m "feat: add EnableIconGlow and EnableIconGlowPulse settings"
```

---

## Task 4: Create `IconGlowManager` — Orchestrator

**Files:**
- Create: `src/IconGlow/IconGlowManager.cs`

**Step 1: Create the file**

Create `src/IconGlow/IconGlowManager.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Audio;

namespace UniPlaySong.IconGlow
{
    // Orchestrates the icon glow effect: finds the game icon, extracts color,
    // injects a glow border, and drives its opacity via DispatcherTimer.
    public class IconGlowManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string GlowBorderName = "UPS_IconGlow";

        private readonly UniPlaySongSettings _settings;
        private readonly IconColorExtractor _colorExtractor = new IconColorExtractor();
        private readonly Logging.FileLogger _fileLogger;

        private DispatcherTimer _glowTimer;
        private Border _currentGlowBorder;
        private Image _currentIcon;
        private DockPanel _currentDockPanel;
        private int _originalIconIndex;
        private DateTime _pulseStartTime;

        public IconGlowManager(UniPlaySongSettings settings, Logging.FileLogger fileLogger = null)
        {
            _settings = settings;
            _fileLogger = fileLogger;
        }

        // Called from OnGameSelected when a game is selected in Desktop mode.
        public void OnGameSelected(Game game)
        {
            if (!_settings.EnableIconGlow)
            {
                RemoveGlow();
                return;
            }

            // Defer slightly to let the visual tree update after selection
            Application.Current?.Dispatcher?.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => ApplyGlow(game)));
        }

        // Removes any existing glow and stops the timer.
        public void RemoveGlow()
        {
            StopTimer();

            if (_currentGlowBorder != null && _currentDockPanel != null && _currentIcon != null)
            {
                try
                {
                    // Unwrap: remove border from dock panel, restore icon directly
                    _currentGlowBorder.Child = null;
                    _currentDockPanel.Children.Remove(_currentGlowBorder);
                    _currentDockPanel.Children.Insert(_originalIconIndex, _currentIcon);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[IconGlow] Error removing glow border");
                }
            }

            _currentGlowBorder = null;
            _currentIcon = null;
            _currentDockPanel = null;
        }

        public void Destroy()
        {
            RemoveGlow();
            _colorExtractor.ClearCache();
        }

        private void ApplyGlow(Game game)
        {
            RemoveGlow();

            if (game == null) return;

            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null) return;

            // Find the icon in the visual tree
            var icon = TileFinder.FindSelectedGameIcon(mainWindow);
            if (icon == null)
            {
                _fileLogger?.Debug("[IconGlow] PART_ImageIcon not found in visual tree");
                return;
            }

            // Find the parent DockPanel
            var dockPanel = TileFinder.FindParentDockPanel(icon);
            if (dockPanel == null)
            {
                _fileLogger?.Debug("[IconGlow] Parent DockPanel not found for icon");
                return;
            }

            // Extract dominant color from icon
            var color = _colorExtractor.GetDominantColor(game.Id, icon.Source);

            // Create glow border
            var glowBorder = new Border
            {
                Name = GlowBorderName,
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Opacity = 0.5,
                Padding = new Thickness(0)
            };

            // Wrap the icon: remove from dock panel, put in border, put border in dock panel
            _originalIconIndex = dockPanel.Children.IndexOf(icon);
            dockPanel.Children.Remove(icon);
            glowBorder.Child = icon;
            dockPanel.Children.Insert(_originalIconIndex, glowBorder);

            _currentGlowBorder = glowBorder;
            _currentIcon = icon;
            _currentDockPanel = dockPanel;
            _pulseStartTime = DateTime.UtcNow;

            _fileLogger?.Debug($"[IconGlow] Applied glow to {game.Name} (color: #{color.R:X2}{color.G:X2}{color.B:X2})");

            StartTimer();
        }

        private void StartTimer()
        {
            if (_glowTimer != null) return;

            _glowTimer = new DispatcherTimer(DispatcherPriority.Render, Application.Current.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60fps
            };
            _glowTimer.Tick += OnGlowTick;
            _glowTimer.Start();
        }

        private void StopTimer()
        {
            if (_glowTimer != null)
            {
                _glowTimer.Stop();
                _glowTimer.Tick -= OnGlowTick;
                _glowTimer = null;
            }
        }

        private void OnGlowTick(object sender, EventArgs e)
        {
            if (_currentGlowBorder == null) return;

            double opacity;
            var vizProvider = VisualizationDataProvider.Current;

            if (vizProvider != null)
            {
                // NAudio mode: audio-reactive
                vizProvider.GetLevels(out float peak, out float rms);
                // Map RMS (typically 0.0-0.5 range) to opacity 0.3-0.8
                opacity = 0.3 + Math.Min(0.5, rms * 2.0) * 1.0;
            }
            else if (_settings.EnableIconGlowPulse)
            {
                // SDL2 mode: fixed sinusoidal pulse (~2s cycle)
                double elapsed = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
                opacity = 0.3 + 0.25 * (1.0 + Math.Sin(2.0 * Math.PI * elapsed / 2.0));
            }
            else
            {
                // Static glow
                opacity = 0.5;
                StopTimer(); // No need to keep ticking for static
                _currentGlowBorder.Opacity = opacity;
                return;
            }

            _currentGlowBorder.Opacity = Math.Max(0.3, Math.Min(0.8, opacity));
        }
    }
}
```

**Step 2: Build to verify**

```bash
dotnet build -c Release
```
Expected: `Build succeeded. 0 Error(s)`

**Step 3: Commit**

```bash
git add src/IconGlow/IconGlowManager.cs
git commit -m "feat: add IconGlowManager — orchestrates glow border lifecycle and pulse"
```

---

## Task 5: Wire IconGlowManager into UniPlaySong.cs

**Files:**
- Modify: `src/UniPlaySong.cs`

**Step 1: Add the field**

Find the private fields near the other service declarations. Add:

```csharp
private IconGlow.IconGlowManager _iconGlowManager;
```

**Step 2: Create the manager in OnApplicationStarted**

In `OnApplicationStarted()`, after other service initialization, add:

```csharp
if (IsDesktop && _settings.EnableIconGlow)
{
    _iconGlowManager = new IconGlow.IconGlowManager(_settings, _fileLogger);
}
```

**Step 3: Call from OnGameSelected**

In `OnGameSelected()` (line 310), add at the end of the method:

```csharp
if (_iconGlowManager != null && IsDesktop)
{
    _iconGlowManager.OnGameSelected(game);
}
```

**Step 4: Clean up on disposal**

Find the plugin's `Dispose()` or cleanup method. Add:

```csharp
_iconGlowManager?.Destroy();
_iconGlowManager = null;
```

**Step 5: Build and package**

```bash
dotnet clean -c Release && dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```
Expected: `Build succeeded. 0 Error(s)` then `PACKAGE CREATED SUCCESSFULLY`

**Step 6: Commit**

```bash
git add src/UniPlaySong.cs
git commit -m "feat: wire IconGlowManager into UniPlaySong lifecycle"
```

---

## Task 6: Manual testing checklist

After building and packaging, install the extension in Playnite and verify:

- [ ] Enable `EnableIconGlow` in Settings → Experimental. Restart Playnite.
- [ ] Select a game in Desktop Grid View — confirm a colored border appears around the game icon in the sidebar header.
- [ ] Select a game in Desktop Details/List View — confirm the colored border appears around the game icon in the list item.
- [ ] Switch between games — confirm the old glow is removed and new glow appears with the new game's dominant color.
- [ ] With Live Effects/Visualizer ON (NAudio) — confirm the glow pulses in response to the music's amplitude.
- [ ] With Live Effects/Visualizer OFF (SDL2) and `EnableIconGlowPulse` ON — confirm a gentle fixed-rate pulse (~2s cycle).
- [ ] With Live Effects/Visualizer OFF and `EnableIconGlowPulse` OFF — confirm the glow is static (no pulsing).
- [ ] Disable `EnableIconGlow` — confirm no glow appears.
- [ ] Games with no icon — confirm fallback to cornflower blue glow color.
- [ ] Scroll rapidly in a list — confirm no crashes or visual glitches from virtualization.

---

## Verification

After all tasks complete, run the full build and verify:

```bash
dotnet clean -c Release && dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

Grep sanity checks:
```bash
# GlowManager should only be created in OnApplicationStarted
grep -n "new IconGlow.IconGlowManager" src/UniPlaySong.cs

# OnGameSelected should call the glow manager
grep -n "_iconGlowManager" src/UniPlaySong.cs

# TileFinder should search for PART_ImageIcon
grep -n "PART_ImageIcon" src/IconGlow/TileFinder.cs
```
