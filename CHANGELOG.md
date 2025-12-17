# UniPlaySong - Changelog

## Version 1.0.7

### Features
- **Silence Trimming**: Automatically remove leading silence from audio files using FFmpeg
  - Configurable silence detection threshold and duration
  - Enhances fade effects for seamless music preview transitions
  - Smart file naming that preserves operation order (normalize → trim or trim → normalize)
  - Option to skip already-trimmed files
  - Minimum silence duration threshold to avoid trimming very short silences
  - Works seamlessly with audio normalization - files can be both normalized and trimmed

## Version 1.0.6

### Features
- **Audio Normalization**: Normalize all game music to consistent volume levels using FFmpeg
  - Uses standard FFmpeg only (FFmpeg-normalize not required)
  - EBU R128 standard support with customizable target loudness, true peak, and loudness range
  - Bulk normalization support for entire game libraries
  - Option to preserve original files or replace them
  - Accessible from both desktop and fullscreen modes
- **Fullscreen Controller Support**: Complete Xbox controller support for managing music in fullscreen mode
  - Download music directly from fullscreen using controller
  - Delete songs with controller-friendly dialogs
  - Normalize audio files from fullscreen menu
  - Set primary songs without leaving fullscreen
- **Native Music Integration**: Use Playnite's native background music as default music option
- **Improved Settings**: Better organization and clearer options

### Improvements
- Enhanced native music suppression reliability
- Better theme compatibility for fullscreen modes
- Improved error handling and stability

## Version 1.0.5

### Features
- **Native Music Control**: Option to use Playnite's native music as default or suppress it
- **Settings Improvements**: New "Use Native Playnite Music as Default" option

### Improvements
- Optimized native music handling
- Better compatibility with themes that have login screens
- Improved music playback reliability

## Version 1.0.4

### Features
- **Primary Song System**: Set a "primary" song that plays first when selecting a game
- **Universal Fullscreen Support**: Works with any Playnite theme without modifications
- **Improved File Management**: Better file dialogs and error handling

### Bug Fixes
- Fixed music not stopping when switching to games without music
- Fixed music not playing when switching back to games with music
- Fixed file locking issues when selecting music files

