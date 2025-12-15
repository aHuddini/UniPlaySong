using Playnite.SDK.Models;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Coordinates music playback decisions, state management, and skip logic
    /// Centralizes all "should play" logic to prevent timing issues and complexity
    /// </summary>
    public interface IMusicPlaybackCoordinator
    {
        /// <summary>
        /// Central gatekeeper - determines if music should play for a given game
        /// All music playback decisions go through this method
        /// </summary>
        bool ShouldPlayMusic(Game game);
        
        /// <summary>
        /// Handles game selection event - coordinates skip logic and playback
        /// </summary>
        void HandleGameSelected(Game game, bool isFullscreen);
        
        /// <summary>
        /// Handles login screen dismissal (controller/keyboard input)
        /// </summary>
        void HandleLoginDismiss();
        
        /// <summary>
        /// Handles view changes (user left login screen)
        /// </summary>
        void HandleViewChange();
        
        /// <summary>
        /// Handles video state changes (pause/resume music when video plays)
        /// </summary>
        void HandleVideoStateChange(bool isPlaying);
        
        /// <summary>
        /// Gets whether this is the first game selection
        /// </summary>
        bool IsFirstSelect();
        
        /// <summary>
        /// Gets whether login skip is currently active
        /// </summary>
        bool IsLoginSkipActive();
        
        /// <summary>
        /// Resets the first select flag (for testing or manual reset)
        /// </summary>
        void ResetFirstSelect();
        
        /// <summary>
        /// Sets login skip active state (for testing or manual control)
        /// </summary>
        void SetLoginSkipActive(bool active);
        
        /// <summary>
        /// Resets skip state when switching modes (for SkipFirstSelectionAfterModeSwitch)
        /// Should be called when switching to fullscreen mode
        /// </summary>
        void ResetSkipStateForModeSwitch();

        /// <summary>
        /// Updates the settings reference (called when settings are saved)
        /// </summary>
        void UpdateSettings(UniPlaySongSettings newSettings);
    }
}


