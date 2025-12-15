# UniPSong Extension Review and Improvement Recommendations

**Date**: 2025-12-08  
**Status**: Review & Recommendations  
**Version**: Based on v1.0.5 codebase

## Executive Summary

This document provides a comprehensive review of the UniPSong extension and identifies key improvements to enhance user flexibility and fullscreen controller-friendly experience. The review focuses on two main areas:

1. **Song Randomization & Playback Flexibility** - Adding randomization options similar to PlayniteSound
2. **Controller-Friendly Dialog Interfaces** - Making download dialogs fully navigable via controller in fullscreen mode

---

## Part 1: Song Randomization & Playback Flexibility

### Current State Analysis

#### Current Implementation (`MusicPlaybackService.SelectSongToPlay`)

The current song selection logic is very basic:
- Primary song plays once on first selection for a game directory
- After primary song, always selects the first available song
- No randomization options
- Songs continue playing until manually changed

**Code Reference**: `Services/MusicPlaybackService.cs:654-685`

```csharp
private string SelectSongToPlay(Game game, List<string> songs, bool isNewGame)
{
    // Primary song logic
    // Always returns first song after primary
    return songs.FirstOrDefault();
}
```

#### PlayniteSound Comparison

PlayniteSound offers two randomization settings:
- **`RandomizeOnEverySelect`**: Randomizes song when selecting a different game
- **`RandomizeOnMusicEnd`**: Randomizes song when current song ends (loops to next random song)

**Code Reference**: `src/PlayniteSound/PlayniteSounds.cs:1027-1063`

### Recommended Improvements

#### 1. Add Randomization Settings

Add two new settings to `UniPlaySongSettings.cs`:

```csharp
/// <summary>
/// Randomize song selection when selecting a different game
/// When enabled, a random song will be selected each time you select a game (after primary song plays)
/// </summary>
public bool RandomizeOnEverySelect { get; set; } = false;

/// <summary>
/// Randomize song selection when current song ends
/// When enabled, after the current song finishes, a new random song will be selected from available songs
/// </summary>
public bool RandomizeOnMusicEnd { get; set; } = true; // Default true for variety
```

#### 2. Update Song Selection Logic

Modify `MusicPlaybackService.SelectSongToPlay()` to support randomization:

**Implementation Strategy**:
1. Track the previously played song to avoid immediate repeats
2. Implement randomization when `RandomizeOnEverySelect` is enabled and game changes
3. Implement randomization when `RandomizeOnMusicEnd` is enabled and song ends
4. Ensure primary song still plays first (if set)
5. Avoid repeating the same song immediately (unless only one song available)

**Code Changes Required**:
- `Services/MusicPlaybackService.cs`:
  - Add `_previousSongPath` field to track last played song
  - Modify `SelectSongToPlay()` to implement randomization logic
  - Handle `OnMediaEnded` event to trigger randomization on song end

#### 3. Handle Song End Event for Randomization

Currently, `OnMediaEnded` exists but may not handle randomization. Update to:
- Check if `RandomizeOnMusicEnd` is enabled
- If enabled, select a new random song (avoiding immediate repeats)
- Start playing the new song with fade-in

#### 4. Preview Mode Integration

When preview mode restarts a song (after preview duration), optionally randomize instead of restarting the same song:
- Add option: "Randomize on preview restart" (only applies when preview mode is enabled)
- Or: When preview restarts, check if we should randomize to next song instead

### Implementation Details

#### Modified Files

1. **`UniPlaySongSettings.cs`**
   - Add `RandomizeOnEverySelect` property (default: false)
   - Add `RandomizeOnMusicEnd` property (default: true)

2. **`UniPlaySongSettingsView.xaml`**
   - Add checkboxes for new randomization settings
   - Group with preview mode settings (they're related)

3. **`Services/MusicPlaybackService.cs`**
   - Add `_previousSongPath` field
   - Modify `SelectSongToPlay()` method
   - Update `OnMediaEnded()` handler

#### Algorithm for Randomization

```csharp
private string SelectSongToPlay(Game game, List<string> songs, bool isNewGame, UniPlaySongSettings settings)
{
    if (songs.Count == 0) return null;
    
    // 1. Continue current song if still valid (existing logic)
    if (!string.IsNullOrWhiteSpace(_currentSongPath) && songs.Contains(_currentSongPath))
    {
        return _currentSongPath;
    }
    
    // 2. Primary song logic (existing)
    var primarySong = GetPrimarySongIfApplicable(game, songs, isNewGame);
    if (primarySong != null) return primarySong;
    
    // 3. Randomization logic
    if (songs.Count > 1)
    {
        bool shouldRandomize = false;
        
        // Randomize on every select (when game changes)
        if (isNewGame && settings?.RandomizeOnEverySelect == true)
        {
            shouldRandomize = true;
        }
        
        if (shouldRandomize)
        {
            // Select random song, avoiding immediate repeat
            var random = new Random();
            string selected;
            do
            {
                selected = songs[random.Next(songs.Count)];
            }
            while (selected == _previousSongPath && songs.Count > 1);
            
            _previousSongPath = selected;
            return selected;
        }
    }
    
    // 4. Default: First song
    var firstSong = songs.FirstOrDefault();
    _previousSongPath = firstSong;
    return firstSong;
}
```

#### Song End Randomization

```csharp
private void OnMediaEnded(object sender, EventArgs e)
{
    if (_currentSettings?.RandomizeOnMusicEnd == true && _currentGame != null)
    {
        // Get available songs for current game
        var songs = _fileService.GetAvailableSongs(_currentGame);
        if (songs.Count > 1)
        {
            // Select random song (avoiding immediate repeat)
            var random = new Random();
            string nextSong;
            do
            {
                nextSong = songs[random.Next(songs.Count)];
            }
            while (nextSong == _currentSongPath && songs.Count > 1);
            
            // Play next random song
            LoadAndPlayFile(nextSong);
            return;
        }
    }
    
    // Default behavior: restart current song or stop
    // (existing logic)
}
```

### User Experience Benefits

1. **More Variety**: Users hear different songs when browsing games
2. **Automatic Rotation**: Songs automatically change when they end (if enabled)
3. **Flexibility**: Users can choose their preferred randomization behavior
4. **Console-like Experience**: Mimics console game music systems that randomize tracks

---

## Part 2: Controller-Friendly Dialog Interfaces

### Current State Analysis

#### Existing Analysis Document

There is already a comprehensive analysis document: `docs/archive/CONTROLLER_FRIENDLY_INTERFACE_ANALYSIS.md`

**Key Findings**:
- ❌ No explicit controller support
- ❌ No focus management for controller navigation
- ❌ No visual indicators for focused elements
- ❌ TextBox not easily accessible via controller
- ✅ Material Design UI (good foundation)

#### Current Dialog Structure

**`Views/DownloadDialogView.xaml`**:
1. Title Bar
2. Search Box (TextBox + SEARCH button)
3. Results List (ListBox)
4. Progress Bar
5. Action Buttons (BACK, CANCEL, CONFIRM/DOWNLOAD)

#### Controller Navigation Challenges

1. **Text Input**: No easy way to type search terms with controller
2. **List Navigation**: ListBox may not properly handle d-pad/analog stick
3. **Button Navigation**: Tab order may not be intuitive
4. **Visual Feedback**: No clear focus indicators
5. **Multi-Select**: Selecting multiple songs is cumbersome

### Recommended Improvements

#### Phase 1: Basic Controller Support (High Priority)

##### 1.1 Focus Management

**Implementation**:
- Ensure ListBox receives focus on dialog load
- Map controller d-pad/analog stick to ListBox navigation
- Implement proper Tab order: List → Buttons (skip Search in controller mode)

**Code Changes**:
- `Views/DownloadDialogView.xaml.cs`: Add focus management on load
- Handle `KeyDown` events for controller input
- Map controller buttons to appropriate actions

##### 1.2 Visual Focus Indicators

**Implementation**:
- Add Material Design focus styles to ListBox items
- Add focus rectangles to buttons
- Ensure high contrast for visibility

**XAML Changes**:
```xaml
<ListBox.ItemContainerStyle>
    <Style TargetType="ListBoxItem" BasedOn="{StaticResource MaterialDesignListBoxItem}">
        <Setter Property="FocusVisualStyle">
            <Setter.Value>
                <Style TargetType="Control">
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate>
                                <Border BorderBrush="#2196F3" 
                                        BorderThickness="2" 
                                        CornerRadius="4"
                                        Margin="-2"/>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </Setter.Value>
        </Setter>
        <!-- Existing styles -->
    </Style>
</ListBox.ItemContainerStyle>
```

##### 1.3 Controller Button Mapping

**Recommended Mapping**:
- **A/X Button** (Enter): Confirm/Download
- **B/Circle Button** (Escape): Cancel/Back
- **Y/Triangle Button**: Preview current item (if available)
- **X/Square Button**: Select All/Deselect All (multi-select mode)
- **D-pad/Left Stick**: Navigate list (Up/Down)
- **Shoulder Buttons (LB/RB)**: Page Up/Down (jump 10 items)
- **Triggers (LT/RT)**: Jump to Top/Bottom of list

**Implementation**:
- Detect controller input in `DownloadDialogView.xaml.cs`
- Map to appropriate KeyDown events or direct actions
- Use Playnite's input handling if available, or implement custom detection

##### 1.4 Search Box Alternatives for Controller

**Option A: Quick Filter Buttons (Recommended)**
- Add A-Z, 0-9 filter buttons above the list
- Controller navigates to filter button
- Filters list by starting letter
- Faster than typing for many users

**Option B: Hide Search in Controller Mode**
- Detect if controller is primary input
- Hide search box when controller detected
- Show all results, navigate with d-pad

**Option C: Virtual On-Screen Keyboard**
- Show OSK when TextBox receives focus in controller mode
- Controller navigates OSK keys
- More complex but allows full search functionality

**Recommendation**: Start with **Option A** (Quick Filter Buttons) as it's a good balance of functionality and simplicity.

#### Phase 2: Enhanced Navigation (Medium Priority)

##### 2.1 Multi-Select Improvements

**Add**:
- "Select All" / "Deselect All" buttons (shown only in multi-select mode)
- Selection count display: "3 of 15 selected"
- Visual feedback for selected items (different background color)

##### 2.2 Button Hints

**Add**:
- On-screen button hints showing controller mappings
- Context-sensitive hints (change based on focused element)
- Example: "Press A to confirm | B to cancel | Y to preview"

##### 2.3 Smooth Scrolling

**Implementation**:
- Auto-scroll list to keep focused item visible
- Smooth animations when navigating long lists
- Ensure focused item is always in viewport

#### Phase 3: Advanced Features (Low Priority)

##### 3.1 Virtual Keyboard

**Implementation**:
- Custom WPF control for on-screen keyboard
- Controller navigation of keyboard
- Auto-show when TextBox receives focus in controller mode

##### 3.2 Auto-Preview

**Option**:
- Auto-preview song when item receives focus (optional, with setting)
- Stop preview when navigating away
- Useful for quickly sampling songs

### Implementation Files

#### Modified Files

1. **`Views/DownloadDialogView.xaml`**
   - Add focus styles to ListBox items
   - Add focus styles to buttons
   - Add filter buttons (A-Z) above list (optional)
   - Add selection count display
   - Add button hints panel

2. **`Views/DownloadDialogView.xaml.cs`**
   - Add focus management on window load
   - Add controller input detection
   - Map controller buttons to actions
   - Handle filter button clicks

3. **`ViewModels/DownloadDialogViewModel.cs`**
   - Add filter functionality
   - Add selection count property
   - Add "Select All" / "Deselect All" commands

### Technical Implementation Notes

#### Controller Input Detection

**Option 1: Use Playnite API** (if available)
```csharp
// Check if controller is connected/active
// Use Playnite's input handling
```

**Option 2: Custom Detection via WPF**
```csharp
// Listen for KeyDown events
// Map controller buttons to Key codes
// XInput API for direct controller access (if needed)
```

#### Focus Management

```csharp
private void Window_Loaded(object sender, RoutedEventArgs e)
{
    // Focus the ListBox for controller navigation
    if (ResultsListBox != null && ResultsListBox.Items.Count > 0)
    {
        ResultsListBox.Focus();
        Keyboard.Focus(ResultsListBox);
        
        // Select first item
        if (ResultsListBox.Items.Count > 0)
        {
            ResultsListBox.SelectedIndex = 0;
        }
    }
}
```

#### Quick Filter Buttons

```xaml
<!-- Filter Buttons Row -->
<ItemsControl Grid.Row="1.5" 
              ItemsSource="{Binding FilterButtons}"
              Margin="0,8,0,8">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <StackPanel Orientation="Horizontal"/>
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Button Content="{Binding}" 
                    Command="{Binding DataContext.FilterCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                    CommandParameter="{Binding}"
                    Margin="2"
                    Width="32"
                    Height="32"/>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

---

## Implementation Priority

### High Priority (Implement First)

1. ✅ **Song Randomization Settings** - **COMPLETED in v1.0.6**
   - ✅ Add `RandomizeOnEverySelect` and `RandomizeOnMusicEnd` settings
   - ✅ Implement randomization logic in `SelectSongToPlay()`
   - ✅ Handle song end event for randomization
   - ✅ UI controls and documentation

2. 🔄 **Basic Controller Support** - **NEXT PRIORITY**
   - Focus management (ListBox focus on load)
   - Visual focus indicators
   - Controller button mapping (A/B/Y/X buttons)
   - D-pad/analog stick list navigation

### Medium Priority (Implement Next)

3. ⚠️ **Enhanced Navigation**
   - Quick filter buttons (A-Z) for search
   - Multi-select improvements (Select All/Deselect All)
   - Button hints display
   - Selection count display

4. ⚠️ **Dialog Polish**
   - Smooth scrolling
   - Better visual feedback
   - Improved button layouts

### Low Priority (Future Enhancements)

5. 💡 **Advanced Features**
   - Virtual on-screen keyboard
   - Auto-preview on focus
   - Haptic feedback (if supported)

---

## Testing Considerations

### Song Randomization Testing

1. **Test Cases**:
   - Primary song plays first (if set)
   - Randomization on game selection works
   - Randomization on song end works
   - No immediate song repeats (unless only one song)
   - Works with preview mode

2. **Edge Cases**:
   - Only one song available
   - No songs available
   - Switching between games rapidly
   - Preview mode restart behavior

### Controller Support Testing

1. **Controller Types**:
   - Xbox controllers
   - PlayStation controllers
   - Generic controllers

2. **Navigation Tests**:
   - List navigation with d-pad
   - Button presses work correctly
   - Focus indicators are visible
   - Multi-select with controller
   - Filter buttons work

3. **Fullscreen Mode**:
   - Dialogs appear correctly
   - Controller navigation works
   - No mouse/keyboard required
   - Visual feedback is clear

---

## Comparison with PlayniteSound

### Current State (v1.0.6)

| Feature | PlayniteSound | UniPSong |
|---------|--------------|----------|
| Randomize on selection | ✅ Yes | ✅ Yes |
| Randomize on loop | ✅ Yes | ✅ Yes |
| Controller support | ❌ No | ❌ No |
| Preview mode | ❌ No | ✅ Yes |
| Primary song | ✅ Yes | ✅ Yes |

### Target State (After Improvements)

| Feature | PlayniteSound | UniPSong (Target) |
|---------|--------------|-------------------|
| Randomize on selection | ✅ Yes | ✅ Yes |
| Randomize on loop | ✅ Yes | ✅ Yes |
| Controller support | ❌ No | ✅ Yes |
| Preview mode | ❌ No | ✅ Yes |
| Primary song | ✅ Yes | ✅ Yes |
| Controller-friendly dialogs | ❌ No | ✅ Yes |

---

## Conclusion

The recommended improvements will significantly enhance UniPSong's flexibility and usability:

1. **Song Randomization**: Gives users more variety and control over playback behavior, matching PlayniteSound's flexibility while maintaining UniPSong's unique features.

2. **Controller Support**: Makes UniPSong truly fullscreen-friendly, allowing users to download and manage music without leaving controller-only mode. This differentiates UniPSong from PlayniteSound and aligns with Playnite's console-like experience.

The phased implementation approach allows for incremental improvements while maintaining stability. Starting with high-priority features (randomization and basic controller support) provides immediate value, while subsequent phases add polish and advanced features.

---

## References

- Existing Analysis: `docs/archive/CONTROLLER_FRIENDLY_INTERFACE_ANALYSIS.md`
- PlayniteSound Randomization: `src/PlayniteSound/PlayniteSounds.cs:1027-1063`
- Current Song Selection: `Services/MusicPlaybackService.cs:654-685`
- Dialog View: `Views/DownloadDialogView.xaml`
- Settings: `UniPlaySongSettings.cs`
