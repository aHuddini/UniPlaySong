namespace UniPlaySong.Models
{
    // Represents different sources that can pause music playback.
    // Allows multiple independent systems to pause music without conflicts.
    public enum PauseSource
    {
        Video,                      // Video playback is active (game trailers, cutscenes)
        Manual,                     // User manually paused playback
        Settings,                   // Settings are being changed (e.g., volume adjustment)
        ViewChange,                 // View mode is changing (desktop to fullscreen or vice versa)
        DefaultMusicPreservation,   // Default music position is being preserved when switching to game music
        ThemeOverlay,               // Theme overlay is active (set by MusicControl from theme Tag bindings)
        FocusLoss,                  // Playnite window lost focus to another application (PauseOnFocusLoss setting)
        Minimized,                  // Playnite window is minimized (PauseOnMinimize setting)
        SystemTray,                 // Playnite is hidden in the system tray (PauseWhenInSystemTray setting)
        GameStarting,               // Game is launching — pause until game closes (PauseOnGameStart setting)
        SystemLock                  // Windows session is locked (Win+L) (PauseOnSystemLock setting)
    }
}
