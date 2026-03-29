# UniPlaySong v1.3.9 Release Notes

### New Feature
- **Play Only on Game Select [Fullscreen Mode]**: Default/ambient music plays while browsing the game grid. Game-specific music only plays when you open a game's detail view. Automatically reverts to default music when returning to the grid. Enable in Settings > Playback.

### Controller SDK Migration
- **XInput → Playnite SDK (SDL2)**: All controller dialogs migrated from XInput polling to Playnite SDK event-driven input. Xbox, PlayStation, Switch Pro, and generic controllers are now supported for UPS Fullscreen functionality.
- **Faster D-Pad Navigation**: Debounce reduced from 300ms to 150ms for snappier scrolling in all controller dialogs.

> **Note:** The controller SDK migration is a major refactor. If you experience controller input issues in Fullscreen mode, please report them on [GitHub Issues](https://github.com/aHuddini/UniPlaySong/issues).

### Fixes
- **Amplify Dialog**: Music now continues playing after amplifying a song and closing the dialog.
- **Delete Dialog**: B button no longer triggers unwanted delete confirmation when closing.
- **OGG Audio Editing**: Amplify and Trim audio features now fully support .ogg format (waveform loading, gain adjustment, trimming).
- **Confirmation Dialog**: Rounded button styles and proper D-pad focus navigation.
