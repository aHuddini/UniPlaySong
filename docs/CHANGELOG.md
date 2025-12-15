# UniPlaySong Changelog

## Version 1.0.6 (2025-12-15) - Continued

### Fullscreen Mode with Xbox Controller Support (Complete) 🎮
**Major Feature**: Complete controller-friendly interface for managing game music entirely from fullscreen mode

UniPlaySong now provides complete music management capabilities directly from fullscreen mode using an Xbox controller. Access all features via the extension menu: Select a game → Menu button → Extensions → UniPSound → Choose your action.

**Access Method:**
- Select a game in fullscreen mode
- Press Menu/Context button on Xbox controller (or right-click)
- Navigate to Extensions → UniPSound
- Select from available controller-optimized options

**Features:**
- **Download Music (🎮 Mode)**: Download individual tracks/albums directly from fullscreen menu using Xbox controller
  - Browse KHInsider and YouTube sources with controller navigation
  - Preview tracks with X/Y buttons (automatically pauses game music during preview)
  - Download with A button, navigate back with B button
  - Seamless navigation between dialogs without losing focus or breaking immersion
- **Set Primary Song (🎮 Mode)**: File picker optimized for controller navigation
- **Remove Primary Song (🎮 Mode)**: Quick removal with controller
- **Delete Songs (🎮 Mode)**: Safe deletion with confirmation dialog
- **Normalize Selected Music**: Audio normalization accessible directly from fullscreen menu

**Controller Mappings**:
- **A Button**: Select/Confirm/Download
- **B Button**: Back/Cancel
- **X/Y Buttons**: Preview audio (automatically pauses game music)
- **D-Pad**: Navigate lists
- **LB/RB**: Page through results
- **LT/RT**: Jump to top/bottom of lists

**UI Design**:
- Material Design interface optimized for TV/monitor viewing distances
- Controller-friendly navigation and button prompts
- Large, readable text and clear visual hierarchy
- Smooth transitions between dialogs

### Audio Normalization Feature (Complete)
- Two-pass FFmpeg loudnorm audio normalization to EBU R128 standard
- Normalize all music files or selected games' music
- Space saver mode: Replace originals directly (no backup)
- Preservation mode: Create normalized files with suffix, preserve originals in PreservedOriginals folder
- Restore original files from preserved backups
- Delete preserved originals to free disk space
- Fullscreen menu integration: "Normalize Selected Music" option
- Progress dialog with real-time updates and cancellation support
- Skip already normalized files option
- Configurable target loudness (-16 LUFS default), true peak (-1.5 dBTP), loudness range (11 LU)
- File locking prevention: Stops music playback before normalization
- Comprehensive error handling and logging

### Technical Improvements
- Added `AudioNormalizationService` with separation of concerns architecture
- Added `NormalizationProgressDialog` for progress display
- Enhanced settings UI with normalization configuration
- Fullscreen menu integration for normalization
- Service-based architecture for testability

---

### Song Randomization Features
- Added `RandomizeOnEverySelect` setting: Randomizes song selection when switching games (default: disabled)
- Added `RandomizeOnMusicEnd` setting: Randomizes song selection when current song ends (default: enabled)
- Smart randomization that avoids immediate song repeats
- UI controls in settings for both randomization options
- Enhanced song selection logic following PlayniteSound patterns
- Primary songs still play first, then randomization takes over (if enabled)
- Randomization only applies to game music (not default music)
- Comprehensive logging for randomization events

### Technical Improvements
- Added `_previousSongPath` and `_currentGame` tracking fields
- Enhanced `SelectSongToPlay()` method with randomization logic
- Updated `OnMediaEnded()` event handler for song-end randomization
- Improved state management for game transitions

---

## Version 1.0.5 (2025-12-13)

### Preview Mode for Game Music
- Added preview mode feature to limit playback duration for game-specific music
- Songs restart after specified duration (15-300 seconds, default 30s)
- Perfect for cycling through long music tracks when browsing your game library
- Only affects game-specific music - default music continues to loop normally
- Configurable via new "Preview Mode" section in settings

---

## Version 1.0.4 (2025-12-07)

### Default Music Position Preservation
- Default music now preserves playback position when switching between games
- Smooth fade transitions when switching between game music and default music
- Default music continues seamlessly when switching between games with no music

### Fixes
- Fixed skip behavior when launching fullscreen mode
- Download progress now shows inline within the dialog window
- Fixed title text color and improved navigation

---

## Version 1.0.3.2 (2025-12-01)

### Material Design Integration
- Modern dialog design with Material Design 4.7.0
- Improved visual consistency and user experience

### Improvements
- Fixed fullscreen window transparency
- Added back button navigation in dialogs
- Improved search performance
- Fixed YouTube search (now appends "OST" automatically)

---

## Version 1.0.3.1 (2025-11-30)

### Fixes
- Fixed fullscreen fade-out issue

---

## Version 1.0.3.0 (2025-11-30)

### Major Features
- Theme Compatible Login Skip: Waits for keyboard/controller input before playing music
- Seamless music transitions with preloading
- Customizable fade-in and fade-out durations (0.05 - 10.0 seconds)
- SDL2 audio player with WPF fallback

### Fixes
- Fixed music not playing after login screen
- Fixed lag when switching games rapidly
- Improved fade reliability

