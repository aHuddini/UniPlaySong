# UniPlaySong - Project Structure

## Overview

UniPlaySong is a simplified, focused Playnite extension that provides a console-like game preview experience through music. It's designed to be:
- **Simple**: Easy to set up and use
- **Universal**: Works with any Playnite theme (desktop and fullscreen)
- **Focused**: Provides music previews when hovering/selecting games (console-like experience)
- **User Experience First**: Prioritizes the browsing experience over complex management features

## Project Structure

```
UniPSong/
├── UniPlaySong.cs                      # Main plugin class
├── UniPlaySongSettings.cs              # Settings model
├── UniPlaySongSettingsViewModel.cs     # Settings view model (ISettings implementation)
├── UniPlaySongSettingsView.xaml        # Settings UI (XAML)
├── UniPlaySongSettingsView.xaml.cs     # Settings UI code-behind
├── AssemblyInfo.cs                     # Assembly metadata
├── extension.yaml                      # Extension manifest
├── UniPlaySong.csproj                  # Project file
├── UniPlaySong.sln                     # Solution file
├── package_extension.ps1               # Packaging script
├── icon.png                            # Extension icon
├── LICENSE                             # License file
├── README.md                           # Project documentation
├── .gitignore                          # Git ignore rules
├── Common/                              # Common utilities (to be added)
├── Models/                              # Data models (to be added)
└── Services/                           # Service classes (to be added)
```

## Key Files

### Core Plugin Files

- **UniPlaySong.cs**: Main plugin class that inherits from `GenericPlugin`
  - Handles Playnite events (OnGameSelected, OnApplicationStarted, etc.)
  - Provides settings access
  - Entry point for the extension

- **UniPlaySongSettings.cs**: Settings model
  - Contains all user-configurable settings
  - Implements `ObservableObject` for property change notifications

- **UniPlaySongSettingsViewModel.cs**: Settings view model
  - Implements `ISettings` interface required by Playnite
  - Handles loading/saving settings
  - Provides validation

- **UniPlaySongSettingsView.xaml**: Settings UI
  - WPF UserControl for settings interface
  - Bound to SettingsViewModel

### Configuration Files

- **extension.yaml**: Extension manifest
  - Defines extension ID, name, version, module
  - Required by Playnite for extension recognition

- **UniPlaySong.csproj**: Project configuration
  - .NET Framework 4.6.2 target
  - PlayniteSDK dependency
  - CopyLocalLockFileAssemblies enabled for packaging

- **AssemblyInfo.cs**: Assembly metadata
  - GUID for the extension
  - Version information

## Extension Details

- **ID**: `UniPlaySong.a1b2c3d4-e5f6-7890-abcd-ef1234567890`
- **Name**: UniPlaySong
- **Version**: 1.0.0
- **Type**: GenericPlugin
- **Target Framework**: .NET Framework 4.6.2

## Next Steps

### Phase 1: Core Preview Experience
1. Implement music playback when hovering/selecting games
2. Add game-specific music directory structure
3. Create music playback service with smooth transitions
4. Implement GameContextBindingFactory for universal fullscreen support

### Phase 2: Music Download
1. Integrate music downloaders (YouTube, KHInsider, etc.)
2. Create simple download UI for game soundtracks
3. Add download queue and progress tracking

### Phase 3: Music Selection
1. Implement primary song system (learned from PlayniteSound)
2. Add simple music selection UI (choose which song plays for each game)
3. Support multiple songs per game with randomization

### Phase 4: Polish & Compatibility
1. Test with multiple themes (ANIKI REMAKE, default, etc.)
2. Ensure smooth playback transitions
3. Optimize for console-like browsing experience

## Best Practices Applied

From PlayniteSound Mod codebase:
- ✅ Proper extension structure and manifest
- ✅ Settings implementation with ISettings interface
- ✅ CopyLocalLockFileAssemblies for dependency packaging
- ✅ Proper namespace organization
- ✅ ObservableObject for settings binding

To be implemented:
- GameContextBindingFactory for universal fullscreen support (Phase 1)
- Primary song system for game previews (Phase 3)
- Music downloader integration for soundtracks (Phase 2)
- Clean, focused user interface for console-like experience
- Smooth music transitions when browsing games

## Building and Packaging

1. **Build**: Open `UniPlaySong.sln` in Visual Studio and build in Release mode
2. **Package**: Run `.\package_extension.ps1` from the UniPSong directory
3. **Install**: Install the generated `.pext` file in Playnite

## Dependencies

- **PlayniteSDK** (6.11.0.0): Required for Playnite integration
- **Microsoft.CSharp** (4.7.0): Required for C# compilation

Additional dependencies will be added as features are implemented.

