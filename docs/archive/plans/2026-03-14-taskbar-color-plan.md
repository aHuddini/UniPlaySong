# Music-Reactive Taskbar Color Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tint the Windows taskbar with game-icon-derived colors that shift brightness and temperature based on audio energy.

**Architecture:** A `TaskbarColorManager` orchestrates the feature via a 250ms `DispatcherTimer`, polling `VisualizationDataProvider` for audio energy and delegating taskbar coloring to platform-specific strategies (`Win10TaskbarColorStrategy` using `SetWindowCompositionAttribute`, `Win11TaskbarColorStrategy` using registry accent color). Color source is the shared `IconColorExtractor` cache.

**Tech Stack:** C# / .NET 4.6.2 / WPF, Win32 P/Invoke (`SetWindowCompositionAttribute`, `FindWindow`, `SendMessage`), Windows Registry API

**Design Spec:** `docs/plans/2026-03-14-taskbar-color-design.md`

---

## Chunk 1: Foundation (Settings + Interface + Strategy Implementations)

### Task 1: Add Settings & Enum

**Files:**
- Modify: `src/UniPlaySongSettings.cs` (after line 2404, before `ApplyIconGlowPreset()`)

- [ ] **Step 1: Add the enum and properties**

Add the `TaskbarColorNoMusicMode` enum near the other feature enums (after `IconGlowPreset`), and add the two properties after the last Icon Glow property (`IconGlowSlidersEnabled` at line 2404):

```csharp
public enum TaskbarColorNoMusicMode
{
    Disable,
    Static,
    Pulse
}
```

```csharp
private bool enableTaskbarColor = false;
public bool EnableTaskbarColor
{
    get => enableTaskbarColor;
    set { enableTaskbarColor = value; OnPropertyChanged(); }
}

private TaskbarColorNoMusicMode taskbarColorNoMusicMode = TaskbarColorNoMusicMode.Disable;
public TaskbarColorNoMusicMode TaskbarColorNoMusicMode
{
    get => taskbarColorNoMusicMode;
    set { taskbarColorNoMusicMode = value; OnPropertyChanged(); }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet clean -c Release && dotnet build -c Release
```

Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/UniPlaySongSettings.cs
git commit -m "feat(taskbar-color): add EnableTaskbarColor and TaskbarColorNoMusicMode settings"
```

---

### Task 2: Add Settings UI

**Files:**
- Modify: `src/UniPlaySongSettingsView.xaml` (after List Icon Glow section, ~line 3656)
- Modify: `src/UniPlaySongSettingsView.xaml.cs` (in `ResetExperimentalTab_Click`, after line 268)

- [ ] **Step 1: Add XAML section**

Insert after the SubtleListGlow checkbox and before the closing `</StackPanel>` of the Experimental tab:

```xml
<!-- Music-Reactive Taskbar Color -->
<Separator Margin="0,15"/>
<TextBlock Text="Taskbar Color" FontSize="14" FontWeight="SemiBold" Margin="0,0,0,5"/>

<CheckBox Content="Enable Music-Reactive Taskbar Color (Desktop only)"
          IsChecked="{Binding Settings.EnableTaskbarColor}"
          Margin="0,5,0,5"/>
<TextBlock Text="Tints the Windows taskbar with colors from the selected game's icon. Color shifts with audio energy when music is playing with Live Effects enabled. On Windows 11, this modifies the system accent color which also affects title bars and Start menu."
           FontSize="11"
           Foreground="Gray"
           TextWrapping="Wrap"
           Margin="20,0,0,10"/>

<StackPanel Orientation="Horizontal" Margin="20,5,0,5"
            IsEnabled="{Binding Settings.EnableTaskbarColor}">
    <TextBlock Text="When no music is playing:" VerticalAlignment="Center" Margin="0,0,10,0"/>
    <ComboBox Width="120"
              SelectedIndex="{Binding Settings.TaskbarColorNoMusicMode, Converter={StaticResource IntToComboBoxConverter}}"
              IsEnabled="{Binding Settings.EnableTaskbarColor}">
        <ComboBoxItem Content="Disable"/>
        <ComboBoxItem Content="Static Color"/>
        <ComboBoxItem Content="Pulse"/>
    </ComboBox>
</StackPanel>
```

Note: Check if `IntToComboBoxConverter` exists in the codebase. If not, bind via `SelectedItem` with enum values or use the pattern already used for other ComboBox enum bindings in this file (search for existing ComboBox bindings in the Experimental tab).

- [ ] **Step 2: Add reset handler entries**

In `ResetExperimentalTab_Click` (after the last Icon Glow reset, before `ShowButtonFeedback`):

```csharp
s.EnableTaskbarColor = false;
s.TaskbarColorNoMusicMode = TaskbarColorNoMusicMode.Disable;
```

- [ ] **Step 3: Build to verify**

```bash
dotnet clean -c Release && dotnet build -c Release
```

- [ ] **Step 4: Commit**

```bash
git add src/UniPlaySongSettingsView.xaml src/UniPlaySongSettingsView.xaml.cs
git commit -m "feat(taskbar-color): add Experimental tab UI section"
```

---

### Task 3: Create ITaskbarColorStrategy Interface

**Files:**
- Create: `src/IconGlow/ITaskbarColorStrategy.cs`

- [ ] **Step 1: Create the interface**

```csharp
using System;
using System.Windows.Media;

namespace UniPlaySong.IconGlow
{
    // Platform-specific taskbar coloring strategy.
    // Win10: SetWindowCompositionAttribute on Shell_TrayWnd
    // Win11: Registry accent color + WM_SETTINGCHANGE broadcast
    interface ITaskbarColorStrategy : IDisposable
    {
        bool IsSupported { get; }
        void Apply(Color color);
        void Restore();
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet clean -c Release && dotnet build -c Release
```

- [ ] **Step 3: Commit**

```bash
git add src/IconGlow/ITaskbarColorStrategy.cs
git commit -m "feat(taskbar-color): add ITaskbarColorStrategy interface"
```

---

### Task 4: Create Win10TaskbarColorStrategy

**Files:**
- Create: `src/IconGlow/Win10TaskbarColorStrategy.cs`

- [ ] **Step 1: Create the Win10 strategy**

This class owns its own P/Invoke declarations (doesn't modify `DialogHelper`'s private visibility).

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Playnite.SDK;

namespace UniPlaySong.IconGlow
{
    // Applies taskbar color on Windows 10 via SetWindowCompositionAttribute.
    // Uses ACCENT_ENABLE_GRADIENT (undocumented value 1) on Shell_TrayWnd.
    // Only affects primary monitor taskbar (Shell_SecondaryTrayWnd not handled).
    class Win10TaskbarColorStrategy : ITaskbarColorStrategy
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private IntPtr _taskbarHwnd;
        private bool _originalSaved;
        private AccentPolicy _originalPolicy;

        public bool IsSupported { get; private set; } = true;

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute; // WCA_ACCENT_POLICY = 19
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public uint AccentFlags;
            public uint GradientColor; // AABBGGRR
            public int AnimationId;
        }

        // AccentState values
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_GRADIENT = 1; // undocumented — solid color tint

        public void Apply(Color color)
        {
            try
            {
                if (!EnsureTaskbarHwnd()) return;

                if (!_originalSaved)
                {
                    // Save original state (ACCENT_DISABLED is the default for most systems)
                    _originalPolicy = new AccentPolicy
                    {
                        AccentState = ACCENT_DISABLED,
                        AccentFlags = 0,
                        GradientColor = 0,
                        AnimationId = 0
                    };
                    _originalSaved = true;
                }

                // ABGR format: Alpha=0xCC (mostly opaque), then BGR
                uint abgr = (uint)(0xCC << 24 | color.B << 16 | color.G << 8 | color.R);
                var policy = new AccentPolicy
                {
                    AccentState = ACCENT_ENABLE_GRADIENT,
                    AccentFlags = 2, // draw-left-right gradient flag
                    GradientColor = abgr,
                    AnimationId = 0
                };

                SetAccentPolicy(policy);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[TaskbarColor] Win10 Apply failed");
                IsSupported = false;
            }
        }

        public void Restore()
        {
            try
            {
                if (_originalSaved && _taskbarHwnd != IntPtr.Zero)
                    SetAccentPolicy(_originalPolicy);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[TaskbarColor] Win10 Restore failed");
            }
        }

        public void Dispose()
        {
            Restore();
        }

        private bool EnsureTaskbarHwnd()
        {
            if (_taskbarHwnd != IntPtr.Zero) return true;

            _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (_taskbarHwnd == IntPtr.Zero)
            {
                Logger.Warn("[TaskbarColor] Shell_TrayWnd not found");
                IsSupported = false;
                return false;
            }
            return true;
        }

        private void SetAccentPolicy(AccentPolicy policy)
        {
            int size = Marshal.SizeOf(policy);
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = 19, // WCA_ACCENT_POLICY
                    Data = ptr,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(_taskbarHwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet clean -c Release && dotnet build -c Release
```

- [ ] **Step 3: Commit**

```bash
git add src/IconGlow/Win10TaskbarColorStrategy.cs
git commit -m "feat(taskbar-color): add Win10 strategy (SetWindowCompositionAttribute)"
```

---

### Task 5: Create Win11TaskbarColorStrategy

**Files:**
- Create: `src/IconGlow/Win11TaskbarColorStrategy.cs`

- [ ] **Step 1: Create the Win11 strategy**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;
using Playnite.SDK;

namespace UniPlaySong.IconGlow
{
    // Applies taskbar color on Windows 11 via registry accent color.
    // Modifies system-wide accent (title bars, Start menu, taskbar).
    // Rate-limited via perceptual color distance to avoid shell thrashing.
    class Win11TaskbarColorStrategy : ITaskbarColorStrategy
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private const string PersonalizePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string DwmPath = @"SOFTWARE\Microsoft\Windows\DWM";

        private bool _originalSaved;
        private int _originalColorPrevalence;
        private int _originalColorizationColor;
        private Color _lastAppliedColor;
        private bool _hasApplied;

        public bool IsSupported { get; private set; } = true;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        private const uint WM_SETTINGCHANGE = 0x001A;

        public void Apply(Color color)
        {
            try
            {
                // Rate limit: skip if color hasn't changed perceptually
                if (_hasApplied && ColorDistance(color, _lastAppliedColor) < 3.0)
                    return;

                SaveOriginalIfNeeded();

                // ABGR format for registry
                int abgr = (0xFF << 24) | (color.B << 16) | (color.G << 8) | color.R;

                using (var dwmKey = Registry.CurrentUser.OpenSubKey(DwmPath, true))
                {
                    if (dwmKey == null) { IsSupported = false; return; }
                    dwmKey.SetValue("ColorizationColor", unchecked((int)(0xFF000000 | (uint)(color.R << 16 | color.G << 8 | color.B))), RegistryValueKind.DWord);
                    dwmKey.SetValue("ColorizationAfterglow", unchecked((int)(0xFF000000 | (uint)(color.R << 16 | color.G << 8 | color.B))), RegistryValueKind.DWord);
                }

                using (var perKey = Registry.CurrentUser.OpenSubKey(PersonalizePath, true))
                {
                    if (perKey == null) { IsSupported = false; return; }
                    perKey.SetValue("ColorPrevalence", 1, RegistryValueKind.DWord);
                }

                // Broadcast to force shell to pick up changes
                SendMessage(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet");

                _lastAppliedColor = color;
                _hasApplied = true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[TaskbarColor] Win11 Apply failed");
                IsSupported = false;
            }
        }

        public void Restore()
        {
            try
            {
                if (!_originalSaved) return;

                using (var dwmKey = Registry.CurrentUser.OpenSubKey(DwmPath, true))
                {
                    dwmKey?.SetValue("ColorizationColor", _originalColorizationColor, RegistryValueKind.DWord);
                    dwmKey?.SetValue("ColorizationAfterglow", _originalColorizationColor, RegistryValueKind.DWord);
                }

                using (var perKey = Registry.CurrentUser.OpenSubKey(PersonalizePath, true))
                {
                    perKey?.SetValue("ColorPrevalence", _originalColorPrevalence, RegistryValueKind.DWord);
                }

                SendMessage(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet");
                _hasApplied = false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[TaskbarColor] Win11 Restore failed");
            }
        }

        public void Dispose()
        {
            Restore();
        }

        private void SaveOriginalIfNeeded()
        {
            if (_originalSaved) return;

            try
            {
                using (var dwmKey = Registry.CurrentUser.OpenSubKey(DwmPath))
                    _originalColorizationColor = (int)(dwmKey?.GetValue("ColorizationColor") ?? 0);

                using (var perKey = Registry.CurrentUser.OpenSubKey(PersonalizePath))
                    _originalColorPrevalence = (int)(perKey?.GetValue("ColorPrevalence") ?? 0);

                _originalSaved = true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[TaskbarColor] Win11 failed to save original state");
            }
        }

        // Simple perceptual color distance (weighted Euclidean in RGB)
        private static double ColorDistance(Color a, Color b)
        {
            double dr = a.R - b.R;
            double dg = a.G - b.G;
            double db = a.B - b.B;
            return Math.Sqrt(dr * dr * 0.3 + dg * dg * 0.59 + db * db * 0.11);
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet clean -c Release && dotnet build -c Release
```

- [ ] **Step 3: Commit**

```bash
git add src/IconGlow/Win11TaskbarColorStrategy.cs
git commit -m "feat(taskbar-color): add Win11 strategy (registry accent color)"
```

---

## Chunk 2: TaskbarColorManager + Integration

### Task 6: Create TaskbarColorManager

**Files:**
- Create: `src/IconGlow/TaskbarColorManager.cs`

This is the main orchestrator. It:
1. Selects the right strategy based on OS version
2. Extracts game icon color via `IconColorExtractor`
3. Polls `VisualizationDataProvider` for audio energy
4. Computes the target color via HSV brightness+temperature mapping
5. Handles no-music modes (Disable/Static/Pulse)
6. Manages crash recovery via persisted state files

- [ ] **Step 1: Create the manager**

```csharp
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Audio;
using UniPlaySong.Services;

namespace UniPlaySong.IconGlow
{
    // Tints the Windows taskbar with game-icon-derived colors.
    // Audio energy shifts brightness and temperature when music plays.
    // Polls at ~4Hz (250ms). Delegates to platform-specific ITaskbarColorStrategy.
    class TaskbarColorManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly SettingsService _settingsService;
        private readonly IconColorExtractor _colorExtractor;
        private readonly IPlayniteAPI _api;
        private readonly string _pluginDataPath;

        private UniPlaySongSettings _settings => _settingsService.Current;

        private ITaskbarColorStrategy _strategy;
        private DispatcherTimer _timer;
        private Color _baseColor = Color.FromRgb(100, 149, 237); // default cornflower blue
        private double _smoothedEnergy;
        private DateTime _pulseStartTime;
        private bool _attached;

        private const string ActiveFlagFile = "taskbar_active";
        private const string OriginalStateFile = "taskbar_original_state.json";

        public TaskbarColorManager(SettingsService settingsService, IconColorExtractor colorExtractor,
            IPlayniteAPI api, string pluginDataPath)
        {
            _settingsService = settingsService;
            _colorExtractor = colorExtractor;
            _api = api;
            _pluginDataPath = pluginDataPath;
        }

        public void Attach()
        {
            if (_attached) return;

            // Select strategy based on OS version
            int build = Environment.OSVersion.Version.Build;
            _strategy = build >= 22000
                ? (ITaskbarColorStrategy)new Win11TaskbarColorStrategy()
                : new Win10TaskbarColorStrategy();

            if (!_strategy.IsSupported)
            {
                Logger.Warn($"[TaskbarColor] Strategy not supported on build {build}");
                return;
            }

            // Crash recovery: if we crashed last time, restore original state
            RecoverFromCrash();

            _attached = true;
            _pulseStartTime = DateTime.UtcNow;

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            _settingsService.Current.PropertyChanged += OnSettingChanged;
        }

        public void Detach()
        {
            if (!_attached) return;

            StopTimer();
            _strategy?.Restore();
            _strategy?.Dispose();
            _strategy = null;

            _settingsService.Current.PropertyChanged -= OnSettingChanged;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

            // Clean up crash recovery files
            DeleteFile(ActiveFlagFile);
            DeleteFile(OriginalStateFile);

            _attached = false;
        }

        public void OnGameSelected(Game game)
        {
            if (!_attached || !_settings.EnableTaskbarColor) return;

            if (game == null)
            {
                HandleNoMusic();
                return;
            }

            // Extract color from game icon via database path (no visual tree needed)
            try
            {
                var iconPath = game.Icon != null ? _api.Database.GetFullFilePath(game.Icon) : null;
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(iconPath);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    var (primary, _) = _colorExtractor.GetGlowColors(game.Id, bitmap);
                    _baseColor = primary;
                }
                else
                {
                    _baseColor = Color.FromRgb(100, 149, 237);
                }
            }
            catch
            {
                _baseColor = Color.FromRgb(100, 149, 237);
            }

            _smoothedEnergy = 0;
            EnsureTimerRunning();
        }

        private void OnSettingChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UniPlaySongSettings.EnableTaskbarColor))
            {
                if (!_settings.EnableTaskbarColor)
                {
                    StopTimer();
                    _strategy?.Restore();
                }
            }
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (!_settings.EnableTaskbarColor || _strategy == null || !_strategy.IsSupported)
            {
                StopTimer();
                _strategy?.Restore();
                return;
            }

            double intensity = ComputeIntensity();
            var targetColor = MapColor(_baseColor, intensity);

            // Write crash recovery flag on first apply
            WriteActiveFlag();

            _strategy.Apply(targetColor);
        }

        private double ComputeIntensity()
        {
            try
            {
                var vizProvider = VisualizationDataProvider.Current;
                if (vizProvider != null)
                {
                    vizProvider.GetLevels(out float peak, out float rms);
                    double energy = rms * 0.7 + peak * 0.3;
                    double alpha = energy > _smoothedEnergy ? 0.3 : 0.08;
                    _smoothedEnergy += (energy - _smoothedEnergy) * alpha;
                    return _smoothedEnergy;
                }
            }
            catch { } // ObjectDisposedException if provider is disposed

            // No audio — handle based on mode
            var mode = _settings.TaskbarColorNoMusicMode;
            if (mode == TaskbarColorNoMusicMode.Pulse)
            {
                double elapsed = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
                return 0.3 + 0.2 * Math.Sin(2.0 * Math.PI * elapsed / 3.0); // ~3s period
            }
            if (mode == TaskbarColorNoMusicMode.Static)
                return 0.4; // medium brightness

            // Disable mode — should have been caught earlier, but restore just in case
            StopTimer();
            _strategy?.Restore();
            return 0;
        }

        // HSV-based color mapping: brightness + subtle temperature shift
        private static Color MapColor(Color baseColor, double energy)
        {
            var (hue, sat, val) = RgbToHsv(baseColor.R, baseColor.G, baseColor.B);

            // Brightness: floor 0.15 → ceiling 0.55
            double newVal = 0.15 + energy * 0.40;

            // Saturation: boost at peaks
            double newSat = Math.Min(1.0, sat + energy * 0.3);

            // Hue: subtle warm shift (toward orange) at high energy
            double newHue = hue - energy * 15.0;
            if (newHue < 0) newHue += 360;

            var (r, g, b) = HsvToRgb(newHue, newSat, newVal);
            return Color.FromRgb(r, g, b);
        }

        private void HandleNoMusic()
        {
            if (_settings.TaskbarColorNoMusicMode == TaskbarColorNoMusicMode.Disable)
            {
                StopTimer();
                _strategy?.Restore();
            }
            else
            {
                // Static or Pulse — keep timer running
                EnsureTimerRunning();
            }
        }

        private void EnsureTimerRunning()
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        private void StopTimer()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            try { _strategy?.Restore(); } catch { }
        }

        // --- Crash recovery ---

        private void WriteActiveFlag()
        {
            try
            {
                var path = Path.Combine(_pluginDataPath, ActiveFlagFile);
                if (!File.Exists(path))
                    File.WriteAllText(path, "active");
            }
            catch { }
        }

        private void RecoverFromCrash()
        {
            try
            {
                var flagPath = Path.Combine(_pluginDataPath, ActiveFlagFile);
                if (!File.Exists(flagPath)) return;

                Logger.Info("[TaskbarColor] Detected unclean shutdown, restoring taskbar");
                _strategy?.Restore();
                DeleteFile(ActiveFlagFile);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[TaskbarColor] Crash recovery failed");
            }
        }

        private void DeleteFile(string fileName)
        {
            try
            {
                var path = Path.Combine(_pluginDataPath, fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        // --- HSV utilities (duplicated from IconColorExtractor to avoid changing visibility) ---

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

        private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;

            double r1, g1, b1;
            if (h < 60)       { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
            else              { r1 = c; g1 = 0; b1 = x; }

            return (
                (byte)Math.Min(255, (r1 + m) * 255),
                (byte)Math.Min(255, (g1 + m) * 255),
                (byte)Math.Min(255, (b1 + m) * 255)
            );
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet clean -c Release && dotnet build -c Release
```

- [ ] **Step 3: Commit**

```bash
git add src/IconGlow/TaskbarColorManager.cs
git commit -m "feat(taskbar-color): add TaskbarColorManager orchestrator"
```

---

### Task 7: Wire Up in UniPlaySong.cs

**Files:**
- Modify: `src/UniPlaySong.cs`

- [ ] **Step 1: Add field**

Near the `_listHoverGlowManager` field declaration:

```csharp
private IconGlow.TaskbarColorManager _taskbarColorManager;
```

- [ ] **Step 2: Create and attach in OnApplicationStarted**

After `_listHoverGlowManager` creation (~line 560), add:

```csharp
_taskbarColorManager = new IconGlow.TaskbarColorManager(
    _settingsService, _iconGlowManager.ColorExtractor, _api, GetPluginUserDataPath());
_taskbarColorManager.Attach();
```

- [ ] **Step 3: Wire OnGameSelected**

In the `OnGameSelected` dispatcher block (near line 556 where `_iconGlowManager?.OnGameSelected` is called), add:

```csharp
_taskbarColorManager?.OnGameSelected(selectedGame);
```

- [ ] **Step 4: Add cleanup in OnApplicationStopped**

**Before** `_iconGlowManager?.Destroy()` (line 933), add:

```csharp
_taskbarColorManager?.Detach();
_taskbarColorManager = null;
```

- [ ] **Step 5: Build and package**

```bash
dotnet clean -c Release && dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

- [ ] **Step 6: Commit**

```bash
git add src/UniPlaySong.cs
git commit -m "feat(taskbar-color): wire up TaskbarColorManager lifecycle"
```

---

### Task 8: Manual Testing Checklist

- [ ] **Step 1: Verify feature toggle**
  - Enable "Music-Reactive Taskbar Color" in Experimental settings
  - Taskbar should tint to the selected game's icon color
  - Disable the toggle — taskbar should restore to original

- [ ] **Step 2: Verify audio reactivity (NAudio)**
  - Enable Live Effects or Visualizer (forces NAudio backend)
  - Play music — taskbar brightness should shift with audio energy
  - Quiet sections → dimmer, loud sections → brighter with subtle warm shift

- [ ] **Step 3: Verify no-music modes**
  - Set "When no music" to Static — taskbar holds a medium-brightness game color
  - Set to Pulse — taskbar gently breathes (~3s period)
  - Set to Disable — taskbar restores to default

- [ ] **Step 4: Verify crash recovery**
  - Enable the feature, kill Playnite via Task Manager
  - Relaunch Playnite — taskbar should be restored (check logs for "[TaskbarColor] Detected unclean shutdown")

- [ ] **Step 5: Verify SDL2 fallback**
  - Disable Live Effects and Visualizer (SDL2 backend)
  - Taskbar should fall through to no-music mode behavior

- [ ] **Step 6: Final commit with version note**

```bash
git add -A
git commit -m "feat(taskbar-color): complete Music-Reactive Taskbar Color feature"
```
