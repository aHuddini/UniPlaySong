using Playnite.SDK.Models;

namespace UniPlaySong.Services
{
    // Coordinates music playback decisions, state management, and skip logic
    public interface IMusicPlaybackCoordinator
    {
        // Central gatekeeper — all music playback decisions go through this
        bool ShouldPlayMusic(Game game);
        
        // Handles game selection event — coordinates skip logic and playback
        void HandleGameSelected(Game game, bool isFullscreen);
        
        // Handles login screen dismissal (controller/keyboard input)
        void HandleLoginDismiss();
        
        // Handles view changes (user left login screen)
        void HandleViewChange();
        
        // Handles video state changes from MediaElementsMonitor (pause/resume music when video plays)
        void HandleVideoStateChange(bool isPlaying);

        // Handles theme overlay state changes (pause/resume music; separate from video to prevent conflicts)
        void HandleThemeOverlayChange(bool isActive);

        bool IsFirstSelect();
        
        bool IsLoginSkipActive();
        
        void ResetFirstSelect();
        
        void SetLoginSkipActive(bool active);
        
        // Resets skip state when switching to fullscreen mode
        void ResetSkipStateForModeSwitch();

        // Updates the settings reference (called when settings are saved)
        void UpdateSettings(UniPlaySongSettings newSettings);
    }
}


