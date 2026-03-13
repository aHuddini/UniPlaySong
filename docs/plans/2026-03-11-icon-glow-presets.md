# Icon Glow Presets + Live Slider Fix Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a preset system (Custom/Pulse/Ambient/Neon/Subtle) with a locked read-only dropdown, fix IconGlowSize and IconGlowIntensity sliders so they take effect without switching games, and ensure all sliders are properly wired.

**Architecture:** Add `IconGlowPreset` enum to `Common/Constants.cs`, add `IconGlowPreset` property to `UniPlaySongSettings.cs` that auto-applies preset values on set, add ComboBox to settings XAML that disables sliders when non-Custom, and wire `IconGlowManager` to re-render the glow image when Size/Intensity change while a game is active.

**Tech Stack:** C# .NET 4.6.2, WPF, `UniPlaySongSettings.cs`, `UniPlaySongSettingsView.xaml`, `UniPlaySongSettingsView.xaml.cs`, `IconGlow/IconGlowManager.cs`, `Common/Constants.cs`.

---

## Preset Values Reference

| Setting | Custom (user) | Pulse | Ambient | Neon | Subtle |
|---------|--------------|-------|---------|------|--------|
| IconGlowIntensity | unchanged | 2.5 | 1.2 | 2.8 | 0.8 |
| IconGlowSize | unchanged | 5.0 | 8.0 | 7.0 | 4.0 |
| IconGlowPulseSpeed | unchanged | 1.0 | 3.0 | 1.5 | 2.5 |
| IconGlowAudioSensitivity | unchanged | 3.5 | 1.0 | 2.5 | 0.8 |
| EnableIconGlowPulse | unchanged | true | true | true | true |
| EnableIconGlowSpin | unchanged | false | false | true | false |
| IconGlowSpinSpeed | unchanged | 20.0 | 20.0 | 15.0 | 20.0 |

---

## Task 1: Add IconGlowPreset Enum to Constants.cs

**Files:**
- Modify: `src/Common/Constants.cs`

**Step 1: Find the end of Constants.cs and add the enum**

Open `src/Common/Constants.cs`. Find the last `#endregion` or closing brace of the class. Add before the closing `}` of the namespace (or class, wherever other enums live — check the file for existing enum placement):

```csharp
public enum IconGlowPreset
{
    Custom,  // user-controlled sliders
    Pulse,   // strong beat-locked flash
    Ambient, // slow dreamy breathing
    Neon,    // bright spinning aurora
    Subtle   // barely-there ambient glow
}
```

**Step 2: Build**
```bash
cd "c:/Projects/UniPSound/UniPlaySong"
dotnet build -c Release 2>&1 | tail -5
```
Expected: 0 errors.

---

## Task 2: Add IconGlowPreset Property to UniPlaySongSettings.cs

**Files:**
- Modify: `src/UniPlaySongSettings.cs`

**Step 1: Add backing field and property after `iconGlowSpinSpeed`**

Find the `iconGlowSpinSpeed` backing field (~line 2323). After the `IconGlowSpinSpeed` property block, add:

```csharp
private IconGlowPreset iconGlowPreset = IconGlowPreset.Custom;

// When a preset is selected, its values are applied immediately and sliders are locked.
// Custom leaves all slider values as-is.
public IconGlowPreset IconGlowPreset
{
    get => iconGlowPreset;
    set
    {
        iconGlowPreset = value;
        ApplyIconGlowPreset(value);
        OnPropertyChanged();
        OnPropertyChanged(nameof(IconGlowSlidersEnabled));
    }
}

// True only when preset is Custom — used to enable/disable sliders in XAML
public bool IconGlowSlidersEnabled => iconGlowPreset == IconGlowPreset.Custom;

private void ApplyIconGlowPreset(IconGlowPreset preset)
{
    switch (preset)
    {
        case IconGlowPreset.Pulse:
            IconGlowIntensity = 2.5;
            IconGlowSize = 5.0;
            IconGlowPulseSpeed = 1.0;
            IconGlowAudioSensitivity = 3.5;
            EnableIconGlowPulse = true;
            EnableIconGlowSpin = false;
            IconGlowSpinSpeed = 20.0;
            break;
        case IconGlowPreset.Ambient:
            IconGlowIntensity = 1.2;
            IconGlowSize = 8.0;
            IconGlowPulseSpeed = 3.0;
            IconGlowAudioSensitivity = 1.0;
            EnableIconGlowPulse = true;
            EnableIconGlowSpin = false;
            IconGlowSpinSpeed = 20.0;
            break;
        case IconGlowPreset.Neon:
            IconGlowIntensity = 2.8;
            IconGlowSize = 7.0;
            IconGlowPulseSpeed = 1.5;
            IconGlowAudioSensitivity = 2.5;
            EnableIconGlowPulse = true;
            EnableIconGlowSpin = true;
            IconGlowSpinSpeed = 15.0;
            break;
        case IconGlowPreset.Subtle:
            IconGlowIntensity = 0.8;
            IconGlowSize = 4.0;
            IconGlowPulseSpeed = 2.5;
            IconGlowAudioSensitivity = 0.8;
            EnableIconGlowPulse = true;
            EnableIconGlowSpin = false;
            IconGlowSpinSpeed = 20.0;
            break;
        // Custom: no-op, user controls sliders
    }
}
```

**Step 2: Build**
```bash
dotnet build -c Release 2>&1 | tail -5
```
Expected: 0 errors.

---

## Task 3: Add Preset ComboBox to Settings XAML

**Files:**
- Modify: `src/UniPlaySongSettingsView.xaml`

**Step 1: Find the Icon Glow section header**

Find this block (~line 3535):
```xml
<Grid Margin="0,0,0,5">
    <TextBlock Text="Icon Glow" FontSize="14" FontWeight="SemiBold" VerticalAlignment="Center"/>
    <Button Content="Reset Icon Glow" HorizontalAlignment="Right" VerticalAlignment="Center"
            Click="ResetIconGlow_Click" Padding="8,3" FontSize="11"/>
</Grid>
<CheckBox Content="Enable Icon Glow (Desktop only)"
          IsChecked="{Binding Settings.EnableIconGlow}"
```

**Step 2: Add preset ComboBox after the Enable checkbox and before the pulse checkbox**

After the `<CheckBox Content="Enable Icon Glow (Desktop only)" .../>` element and before the `<CheckBox Content="Enable pulse animation .../>`, insert:

```xml
<DockPanel Margin="20,8,0,8" IsEnabled="{Binding Settings.EnableIconGlow}">
    <TextBlock Text="Preset:" VerticalAlignment="Center" Width="130"/>
    <ComboBox SelectedValue="{Binding Settings.IconGlowPreset}"
              SelectedValuePath="Tag"
              Width="160" HorizontalAlignment="Left">
        <ComboBoxItem Content="Custom" Tag="{x:Static local:IconGlowPreset.Custom}"/>
        <ComboBoxItem Content="Pulse" Tag="{x:Static local:IconGlowPreset.Pulse}"/>
        <ComboBoxItem Content="Ambient" Tag="{x:Static local:IconGlowPreset.Ambient}"/>
        <ComboBoxItem Content="Neon" Tag="{x:Static local:IconGlowPreset.Neon}"/>
        <ComboBoxItem Content="Subtle" Tag="{x:Static local:IconGlowPreset.Subtle}"/>
    </ComboBox>
</DockPanel>
```

**Step 3: Add `local` namespace to XAML root if not already present**

Find the opening `<UserControl` tag at the top of the file. Check if it already has `xmlns:local="clr-namespace:UniPlaySong"`. If not, add it:
```xml
xmlns:local="clr-namespace:UniPlaySong"
```

**Step 4: Disable sliders when preset is not Custom**

For each of the following DockPanels (Glow Intensity, Glow Size, Pulse Speed, Audio Sensitivity, spin checkbox, spin speed slider), change their `IsEnabled` binding from `{Binding Settings.EnableIconGlow}` to a MultiBinding or use a converter. The simplest approach: bind to `Settings.IconGlowSlidersEnabled` AND `Settings.EnableIconGlow` using a style trigger.

Since MultiBinding with AND logic requires a converter, use the simpler approach: wrap all four slider DockPanels and the two pulse/spin checkboxes in a single `StackPanel` with `IsEnabled` bound to `Settings.IconGlowSlidersEnabled`:

Find the `<CheckBox Content="Enable pulse animation` element. Wrap everything from that checkbox down to (and including) the last Audio Sensitivity DockPanel closing tag in:

```xml
<StackPanel IsEnabled="{Binding Settings.IconGlowSlidersEnabled}">
    <!-- all pulse/spin checkboxes and sliders go here -->
</StackPanel>
```

Note: The outer `IsEnabled="{Binding Settings.EnableIconGlow}"` on each element inside can remain — WPF propagates IsEnabled downward, so both conditions must be true.

**Step 5: Build**
```bash
dotnet build -c Release 2>&1 | tail -5
```
Expected: 0 errors (XAML errors show at runtime, not compile time — verify visually).

---

## Task 4: Update Reset Handlers in UniPlaySongSettingsView.xaml.cs

**Files:**
- Modify: `src/UniPlaySongSettingsView.xaml.cs`

**Step 1: Update ResetIconGlow_Click to reset preset to Custom**

Find `ResetIconGlow_Click`. Add `s.IconGlowPreset = IconGlowPreset.Custom;` as the first line after the null check.

**Step 2: Update ResetExperimentalTab_Click similarly**

Find `ResetExperimentalTab_Click`. Add `s.IconGlowPreset = IconGlowPreset.Custom;` as the first line after the null check.

**Step 3: Build**
```bash
dotnet build -c Release 2>&1 | tail -5
```
Expected: 0 errors.

---

## Task 5: Fix Size/Intensity Live Update in IconGlowManager

**Files:**
- Modify: `src/IconGlow/IconGlowManager.cs`

**Problem:** `IconGlowSize` and `IconGlowIntensity` are only read in `ApplyGlowInternal` (called once per game selection). Moving the sliders has no effect until the next game switch.

**Fix:** Store the active game, subscribe to `Settings.PropertyChanged`, and re-apply glow when Size or Intensity changes while a game is active.

**Step 1: Add `_activeGame` field**

Near the other private fields (~line 26), add:
```csharp
private Game _activeGame;
```

**Step 2: Store game reference in OnGameSelected**

In `OnGameSelected`, before the `BeginInvoke` call, add:
```csharp
_activeGame = _settings.EnableIconGlow ? game : null;
```

**Step 3: Subscribe to PropertyChanged in constructor**

In the constructor `IconGlowManager(UniPlaySongSettings settings, ...)`, add:
```csharp
_settings.PropertyChanged += OnSettingsChanged;
```

**Step 4: Add OnSettingsChanged handler**

Add this method after the constructor:
```csharp
private void OnSettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
{
    if (e.PropertyName != nameof(UniPlaySongSettings.IconGlowSize) &&
        e.PropertyName != nameof(UniPlaySongSettings.IconGlowIntensity))
        return;

    if (_activeGame == null) return;

    Application.Current?.Dispatcher?.BeginInvoke(
        DispatcherPriority.Loaded,
        new Action(() => ApplyGlow(_activeGame)));
}
```

**Step 5: Unsubscribe in Destroy**

In the `Destroy()` method, add before `RemoveGlow()`:
```csharp
_settings.PropertyChanged -= OnSettingsChanged;
```

**Step 6: Build and package**
```bash
cd "c:/Projects/UniPSound/UniPlaySong"
dotnet clean -c Release
dotnet build -c Release 2>&1 | tail -5
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1 2>&1 | tail -3
```
Expected: 0 errors, package created.

**Step 7: Commit**
```bash
git add src/Common/Constants.cs src/UniPlaySongSettings.cs src/UniPlaySongSettingsView.xaml src/UniPlaySongSettingsView.xaml.cs src/IconGlow/IconGlowManager.cs docs/plans/2026-03-11-icon-glow-presets.md
git commit -m "-Add Icon Glow presets (Pulse/Ambient/Neon/Subtle/Custom), fix live Size/Intensity slider update"
```

---

## Verification Checklist

- [ ] Selecting "Pulse" preset grays out all sliders and applies pulse values
- [ ] Selecting "Custom" re-enables all sliders
- [ ] Moving Size slider while a game is selected re-renders the glow immediately (within ~100ms)
- [ ] Moving Intensity slider re-renders immediately
- [ ] AudioSensitivity slider still works in real-time (no regression)
- [ ] SpinSpeed slider still works in real-time (no regression)
- [ ] Reset Icon Glow button sets preset back to Custom and re-enables sliders
- [ ] Preset persists across Playnite restarts (serialized in settings JSON)
