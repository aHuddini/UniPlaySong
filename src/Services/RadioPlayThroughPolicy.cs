using System.Collections.Generic;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    // Pure decision for the "Radio plays through games" exception. No state, no threading.
    // Mirrors the SpotifyRadioDecision pattern so the rule is unit-testable without
    // constructing MusicPlaybackService.
    public static class RadioPlayThroughPolicy
    {
        // Sources a game session raises. These are the only ones the exception suppresses.
        // Manual, SystemLock, Video, ThemeOverlay, Dashboard, Jingle, NsfPreview, Settings
        // and ViewChange are deliberately absent — they must keep pausing.
        private static readonly HashSet<PauseSource> GameSessionSources = new HashSet<PauseSource>
        {
            PauseSource.GameStarting, PauseSource.FocusLoss, PauseSource.Minimized,
            PauseSource.SystemTray, PauseSource.Idle, PauseSource.ExternalAudio,
        };

        // True when this pause source must be ignored because Radio Mode is playing
        // through an active game session.
        public static bool ShouldSuppress(
            PauseSource source, bool gameSessionActive, bool isInRadioMode, UniPlaySongSettings settings)
        {
            if (settings == null || !gameSessionActive)
                return false;

            if (!settings.RadioPlaysThroughGames || !settings.RadioModeEnabled)
                return false;

            // Radio must actually be what's playing: the UPS pool (isInRadioMode) or Spotify
            // radio. When radio yields to an installed game's own music, isInRadioMode is
            // false and this returns false, so game music still pauses normally.
            if (!isInRadioMode && !settings.SpotifyRadioMode)
                return false;

            return GameSessionSources.Contains(source);
        }
    }
}
