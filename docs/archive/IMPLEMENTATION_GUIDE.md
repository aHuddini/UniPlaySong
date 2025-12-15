# UniPlaySong Implementation Guide

## Overview

This document provides comprehensive technical documentation for the UniPlaySong Playnite extension, including implementation details, build processes, troubleshooting, and lessons learned from development.

**⚠️ IMPORTANT**: See `ANALYSIS.md` (in same directory) for comprehensive analysis, architecture comparisons, and development principles. This document focuses on technical implementation details.

## Table of Contents

1. [Project Structure](#project-structure)
2. [Build and Packaging](#build-and-packaging)
3. [Key Technical Solutions](#key-technical-solutions)
4. [Settings Implementation](#settings-implementation)
5. [File Path Selection Issue Resolution](#file-path-selection-issue-resolution)
6. [Common Issues and Solutions](#common-issues-and-solutions)
7. [Best Practices](#best-practices)
8. [Critical Development Principles](#critical-development-principles)
9. [Critical Known Issues](#critical-known-issues)
10. [Recent Changes](#recent-changes-2025-11-29)

---

## Project Structure

### Core Components

```
UniPSong/
├── UniPlaySong.cs                 # Main plugin class (GenericPlugin)
├── UniPlaySongSettings.cs          # Settings model (ObservableObject)
├── UniPlaySongSettingsViewModel.cs # Settings view model (ISettings)
├── UniPlaySongSettingsView.xaml    # Settings UI (XAML)
├── UniPlaySongSettingsView.xaml.cs # Settings UI code-behind
├── Common/
│   ├── RelayCommand.cs            # ICommand implementation for MVVM
│   └── PrimarySongManager.cs      # Primary song metadata management
├── Models/                         # Data models (Album, Song, etc.)
├── Downloaders/                    # Music download implementations
│   ├── IDownloader.cs
│   ├── IDownloadManager.cs
│   ├── KHInsiderDownloader.cs
│   ├── YouTubeDownloader.cs
│   └── DownloadManager.cs
├── Services/                       # Business logic services
│   ├── IMusicPlayer.cs
│   ├── IMusicPlaybackService.cs
│   ├── MusicPlayer.cs
│   ├── MusicPlaybackService.cs
│   └── GameMusicFileService.cs
├── Monitors/                       # UI integration
│   ├── WindowMonitor.cs
│   └── GameContextBindingFactory.cs
└── Menus/                          # Menu handlers
    ├── GameMenuHandler.cs
    └── MainMenuHandler.cs
```

### Architecture Principles

1. **Separation of Concerns**: Each module has a single responsibility
   - `Menus/` handles UI menu actions
   - `Services/` handles business logic
   - `Downloaders/` handles external data sources
   - `Monitors/` handles UI integration

2. **Dependency Injection**: Services are injected via constructors
3. **Interface-Based Design**: Services use interfaces for testability
4. **MVVM Pattern**: Settings UI follows Model-View-ViewModel pattern

---

## Build and Packaging

### Prerequisites

- **.NET Framework 4.6.2 Developer Pack**: Required for building
- **Visual Studio 2019+**: Recommended IDE
- **Playnite SDK**: Automatically restored via NuGet
- **NuGet Package Source**: `https://api.nuget.org/v3/index.json` must be enabled

### Build Process

1. **Open Solution**: Open `UniPlaySong.sln` in Visual Studio
2. **Restore Packages**: NuGet packages restore automatically on build
3. **Build Configuration**: Use `Release` configuration for packaging
4. **Build Command**: 
   ```powershell
   dotnet build UniPlaySong.csproj -c Release
   ```
   Or use Visual Studio's Build menu

### Packaging Script

The `package_extension.ps1` script automates extension packaging:

```powershell
.\package_extension.ps1
```

**What it does:**
1. Cleans previous package directory
2. Builds the project in Release mode
3. Copies extension files (`extension.yaml`, `icon.png`, `LICENSE`)
4. Copies the compiled DLL (`UniPlaySong.dll`)
5. Copies dependencies (`HtmlAgilityPack.dll`)
6. **Excludes** `Playnite.SDK.dll` (provided by Playnite at runtime)
7. Creates a `.zip` archive
8. Renames to `.pext` format

**Package Format:**
```
{ExtensionId}_{Version}.pext
Example: UniPlaySong.a1b2c3d4-e5f6-7890-abcd-ef1234567890_1_0_0.pext
```

### Installation

1. Open Playnite
2. Go to **Add-ons → Extensions**
3. Click **"Add extension"**
4. Select the `.pext` file
5. Restart Playnite if needed

---

## Key Technical Solutions

### 1. ObservableObject Implementation

**Issue**: Playnite SDK uses `System.Collections.Generic.ObservableObject` from the SDK, not a custom implementation.

**Solution**: Use `Playnite.SDK.ObservableObject` (which is actually `System.Collections.Generic.ObservableObject` from the Playnite SDK assembly).

**Usage**:
```csharp
public class UniPlaySongSettings : ObservableObject
{
    private string ffmpegPath = string.Empty;
    
    public string FFmpegPath
    {
        get => ffmpegPath;
        set
        {
            ffmpegPath = value ?? string.Empty;
            OnPropertyChanged(); // Inherited from ObservableObject
        }
    }
}
```

### 2. HttpClient Overload Resolution

**Issue**: .NET Framework 4.6.2 has ambiguous `HttpClient` method overloads.

**Solution**: Use explicit `HttpRequestMessage` and `HttpCompletionOption`:

```csharp
var request = new HttpRequestMessage(HttpMethod.Get, url);
var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead);
```

### 3. MediaPlayer State Management

**Issue**: `MediaState` enum doesn't exist in WPF's `MediaPlayer`.

**Solution**: Use `ClockState` from `System.Windows.Media.Animation`:

```csharp
using System.Windows.Media.Animation;

// Instead of MediaState.Play
_mediaPlayer.Clock.Controller.Begin();
// Check state with ClockState.Active
```

### 4. Universal Theme Integration

**Issue**: Different Playnite themes have different data context structures.

**Solution**: Use `PriorityBinding` with multiple fallback paths:

```csharp
var binding = new PriorityBinding();
binding.Bindings.Add(new Binding("SelectedGameDetails.Game.Game"));
binding.Bindings.Add(new Binding("SelectedGame"));
binding.Bindings.Add(new Binding("Game"));
```

### 5. Menu Item Separation

**Issue**: Menu logic mixed with plugin class.

**Solution**: Separate into dedicated handler classes:

```csharp
// In UniPlaySong.cs
private GameMenuHandler _gameMenuHandler;
private MainMenuHandler _mainMenuHandler;

public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
{
    return _gameMenuHandler?.GetMenuItems(args.Games) ?? new List<GameMenuItem>();
}
```

---

## Settings Implementation

### Settings Model (`UniPlaySongSettings.cs`)

Inherits from `ObservableObject` and implements properties with change notifications:

```csharp
public class UniPlaySongSettings : ObservableObject
{
    private string ffmpegPath = string.Empty;
    
    public string FFmpegPath
    {
        get => ffmpegPath;
        set
        {
            ffmpegPath = value ?? string.Empty;
            OnPropertyChanged(); // Critical for UI updates
        }
    }
}
```

### Settings View Model (`UniPlaySongSettingsViewModel.cs`)

Implements `ISettings` interface required by Playnite:

```csharp
public class UniPlaySongSettingsViewModel : ObservableObject, ISettings
{
    public UniPlaySongSettings Settings { get; set; }
    
    public void BeginEdit() { /* Called when settings open */ }
    public void CancelEdit() { /* Revert changes */ }
    public void EndEdit() { /* Save changes */ }
    public bool VerifySettings(out List<string> errors) { /* Validation */ }
}
```

### Settings View (`UniPlaySongSettingsView.xaml`)

Uses data binding to connect UI to view model:

```xml
<TextBox Text="{Binding Settings.FFmpegPath}" IsReadOnly="False"/>
<Button Command="{Binding BrowseForFFmpegFile}"/>
```

### Command Implementation

Uses `RelayCommand` for MVVM pattern:

```csharp
public ICommand BrowseForFFmpegFile => new RelayCommand<object>((a) =>
{
    var filePath = PlayniteApi.Dialogs.SelectFile("ffmpeg|ffmpeg.exe");
    if (!string.IsNullOrWhiteSpace(filePath))
    {
        // Create new Settings object to trigger property change
        var newSettings = new UniPlaySongSettings
        {
            // Copy all properties...
            FFmpegPath = filePath
        };
        Settings = newSettings;
    }
});
```

---

## File Path Selection Issue Resolution

### The Problem

When users clicked "Browse..." and selected a file, the path wasn't appearing in the TextBox, even though:
- The dialog was opening correctly
- The file was being selected
- The property was being set in code

### Initial Attempts

1. **First Attempt**: Used `OpenFileDialog` directly
   - **Issue**: Not compatible with Playnite's dialog system
   - **Result**: Dialog didn't integrate properly

2. **Second Attempt**: Used `PlayniteApi.Dialogs.SelectFile()` with Click events
   - **Issue**: Property changes weren't propagating to UI
   - **Result**: Path set in code but TextBox didn't update

3. **Third Attempt**: Switched to Command binding (matching PlayniteSound)
   - **Issue**: Still not updating UI
   - **Result**: Same problem persisted

### Root Cause Analysis

The issue was with **WPF data binding and nested property change notifications**.

When you have a binding like `{Binding Settings.FFmpegPath}`:
1. WPF subscribes to property change notifications on the **view model's `Settings` property**
2. WPF also needs to subscribe to property changes on the **`Settings` object's `FFmpegPath` property**

**The Problem**: When you set `Settings.FFmpegPath = filePath`:
- The `FFmpegPath` setter calls `OnPropertyChanged()` on the `Settings` object
- But WPF's binding to `Settings.FFmpegPath` might not be properly subscribed to nested property changes
- The view model's `Settings` property reference doesn't change, so WPF doesn't re-evaluate the binding chain

### Why PlayniteSound Works

PlayniteSound likely works because:
1. Their Settings object might have different property change notification wiring
2. They might be using a different ObservableObject implementation
3. Their binding might be set up differently in XAML
4. They might be triggering additional notifications we weren't aware of

### The Solution

**Create a new Settings object** and assign it to the `Settings` property:

```csharp
var newSettings = new UniPlaySongSettings
{
    EnableMusic = Settings.EnableMusic,
    AutoPlayOnSelection = Settings.AutoPlayOnSelection,
    MusicVolume = Settings.MusicVolume,
    YtDlpPath = Settings.YtDlpPath,
    FFmpegPath = filePath  // New value
};
Settings = newSettings;  // This triggers OnPropertyChanged on view model
```

**Why This Works**:
1. Assigning a new object to `Settings` triggers `OnPropertyChanged()` on the view model
2. WPF sees the `Settings` property changed and re-evaluates the entire binding path
3. The binding `{Binding Settings.FFmpegPath}` is re-established with the new object
4. The UI updates correctly

### Key Lessons

1. **Nested Property Bindings**: WPF bindings to nested properties (`Settings.FFmpegPath`) require careful handling of property change notifications
2. **Object Reference Changes**: Sometimes changing the object reference is more reliable than changing nested properties
3. **MVVM Best Practices**: Command binding is correct, but property change propagation needs careful attention
4. **Debugging Data Binding**: When bindings don't work, check:
   - Is `OnPropertyChanged()` being called?
   - Is the property name correct?
   - Is the DataContext set correctly?
   - Does changing the parent object reference help?

---

## Common Issues and Solutions

### Issue: Extension Fails to Install

**Error**: "The extension failed to install and claimed the package was done incorrectly"

**Solutions**:
1. Ensure `Playnite.SDK.dll` is **NOT** included in the package (Playnite provides it)
2. Check `extension.yaml` version format (must be `X.Y.Z`, not `X.Y.Z-suffix`)
3. Verify all required files are included (`extension.yaml`, DLL, dependencies)

### Issue: Build Errors - Missing References

**Error**: "The type or namespace name 'X' could not be found"

**Solutions**:
1. Ensure NuGet package source `https://api.nuget.org/v3/index.json` is enabled
2. Restore NuGet packages: `dotnet restore` or Visual Studio's "Restore NuGet Packages"
3. Install .NET Framework 4.6.2 Developer Pack

### Issue: Settings Not Saving

**Symptoms**: Settings changes don't persist after closing Playnite

**Solutions**:
1. Verify `EndEdit()` calls `SavePluginSettings(settings)`
2. Check that settings are loaded in constructor: `LoadPluginSettings<UniPlaySongSettings>()`
3. Ensure settings class is serializable (no `[DontSerialize]` attributes on properties that should save)

### Issue: Menu Items Not Appearing

**Symptoms**: Right-click menu or main menu items don't show

**Solutions**:
1. Verify `GetGameMenuItems()` or `GetMainMenuItems()` returns non-empty list
2. Check that menu handlers are initialized in constructor
3. Ensure `Properties.HasSettings = true` is set for settings menu

### Issue: Music Not Playing

**Symptoms**: Music doesn't play when selecting games

**Solutions**:
1. Check `OnGameSelected` event handler is attached
2. Verify music files exist in game's music directory
3. Check `MusicPlaybackService` is initialized
4. Verify `EnableMusic` setting is enabled (removed `AutoPlayOnSelection` - now redundant)

### Issue: Music Plays During Login Screen

**Symptoms**: Music plays during login screen transition in fullscreen mode

**Status**: ✅ **RESOLVED** in v1.0.3.0 - See `ANALYSIS.md` for details

**Current Workaround**: Code attempts to detect library views and skip music if view is "unknown", but this is not reliable.

**Resolution**: Input-based login skip implemented in v1.0.3.0. See `ANALYSIS.md` for implementation details.

---

## Best Practices

### 1. Error Handling

Always wrap external operations in try-catch:

```csharp
try
{
    var filePath = PlayniteApi.Dialogs.SelectFile("*.exe");
    // Process file
}
catch (Exception ex)
{
    PlayniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
}
```

### 2. Logging

Use Playnite's logger for debugging:

```csharp
private static readonly ILogger logger = LogManager.GetLogger();
logger.Info("Operation started");
logger.Error(ex, "Operation failed");
```

### 3. Property Change Notifications

Always call `OnPropertyChanged()` in property setters:

```csharp
public string MyProperty
{
    get => _myProperty;
    set
    {
        if (_myProperty != value)
        {
            _myProperty = value;
            OnPropertyChanged();
        }
    }
}
```

### 4. Command Implementation

Use `RelayCommand` for MVVM commands:

```csharp
public ICommand MyCommand => new RelayCommand<object>((parameter) =>
{
    // Command logic
});
```

### 5. Settings Persistence

Always load settings in constructor and save in `EndEdit()`:

```csharp
public UniPlaySongSettingsViewModel(UniPlaySong plugin)
{
    var savedSettings = plugin.LoadPluginSettings<UniPlaySongSettings>();
    Settings = savedSettings ?? new UniPlaySongSettings();
}

public void EndEdit()
{
    plugin.SavePluginSettings(Settings);
}
```

### 6. Separation of Concerns

Keep plugin class focused on Playnite integration:
- Menu items → `MenuHandlers/`
- Business logic → `Services/`
- UI integration → `Monitors/`
- Data access → `Downloaders/`

---

## Critical Development Principles

### ⚠️ IMPORTANT: PlayniteSound is the Reference Implementation

**Before implementing ANY feature or fix:**

1. **ALWAYS check PlayniteSound first** (`src/PlayniteSound/`) - it is the **SUPERIOR** source code
2. **If PlayniteSound doesn't add unnecessary logic, neither should we**
3. **If PlayniteSound uses a simple approach, use the same simple approach**
4. **If PlayniteSound doesn't check for something, don't add that check unless absolutely necessary**

**Key Principle**: Keep the codebase simple and focused. PlayniteSound has been battle-tested and refined over years. Follow its patterns.

### Code Review Approach for Future Agents

**When debugging or implementing features, review the codebase like a senior software developer and engineer with great success in fixing bugs:**

1. **Start with PlayniteSound**: Understand how it solves the problem
2. **Compare implementations**: Look at what we're doing vs. what PlayniteSound does
3. **Identify differences**: Any deviation from PlayniteSound should have a clear, justified reason
4. **Simplify first**: Before adding complexity, see if PlayniteSound's simpler approach works
5. **Test incrementally**: Make small changes and test, don't rewrite large sections at once

**See `ANALYSIS.md` (in same directory) for comprehensive analysis, lessons learned, and development principles.**

## Critical Known Issues

### Login Screen vs. Library View Music Selection Issue

**Status**: ⚠️ **UNRESOLVED** - High Priority

**Problem**: Music plays during the login screen transition in fullscreen mode (especially with ANIKI REMAKE theme), when it should only play after the user reaches the library/grid view.

**Current Behavior**:
- Music sometimes plays during login screen transition
- `GetMainModel()` returns null/"unknown" during login screen
- View detection is unreliable during transitions

**Attempted Solutions (All Failed)**:
1. Time-based delays after mode switch
2. View name detection for login screens
3. Skip first selection setting
4. ActiveView property change monitoring
5. Library view detection (Grid/Details/List)

**See `ANALYSIS.md` (in same directory) for comprehensive analysis and development principles.**

## Recent Changes (2025-11-29)

### Settings Simplification
- **Removed**: "Auto-play music when selecting games" checkbox (redundant)
- **Simplified**: Single "Enable Music" master switch
- **Updated**: All logic now only checks `EnableMusic` setting

### Library View Detection
- Added check for library views before playing music
- Fullscreen: Checks for "Grid", "Details", "List"
- Desktop: Checks for "Library"
- Skips music if view is "unknown" or not a library view
- **Note**: Still not working reliably - needs further investigation

### UI Cleanup
- Removed fullscreen-specific settings from UI
- Simplified settings interface
- Focus on core functionality

## Future Development Notes

### Known Limitations

1. **Login Screen Music Issue**: Music may play during login screen transitions (see Critical Known Issues above)
2. **File Path Selection**: Requires creating new Settings object (workaround for nested binding)
3. **Theme Compatibility**: Some themes may require additional binding paths
4. **View Detection**: `GetMainModel()` may return null/"unknown" during transitions

### Potential Improvements

1. **Fix login screen music issue** - **HIGHEST PRIORITY**
2. Settings UI: Could use more sophisticated validation and error display
3. Download Progress: Could add progress indicators for downloads
4. Music Preview: Could add preview functionality before setting primary song
5. Batch Operations: Could support batch setting primary songs for multiple games

### Testing Recommendations

1. **Test login screen behavior** - Critical for fullscreen themes with login screens
2. Test with multiple Playnite themes (Desktop and Fullscreen)
3. Test with games that have no music files
4. Test with games that have many music files
5. Test settings persistence across Playnite restarts
6. Test error handling with invalid file paths
7. Test mode switching (desktop ↔ fullscreen)

---

## References

- [Playnite API Documentation](https://api.playnite.link/)
- [Playnite Extension Tutorial](https://playnite.link/docs/tutorials/extensions/)
- [WPF Data Binding](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/data/data-binding-overview)
- [MVVM Pattern](https://docs.microsoft.com/en-us/archive/msdn-magazine/2009/february/patterns-wpf-apps-with-the-model-view-viewmodel-design-pattern)
- **PlayniteSound Source Code** (`src/PlayniteSound/`) - **PRIMARY REFERENCE**

---

**Last Updated**: 2025-11-29
**Version**: 1.0.0
**See Also**: 
- `ANALYSIS.md` (in same directory) - Comprehensive analysis, architecture comparisons, and development principles
- `ARCHITECTURE.md` (in same directory) - System architecture and component overview

