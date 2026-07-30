namespace UniPlaySong.Services
{
    // Pure decision for "Play Only on Game Select" while Radio Mode is on. No state, no threading.
    // Mirrors RadioPlayThroughPolicy / SpotifyRadioDecision so the rule is unit-testable without
    // constructing MusicPlaybackService.
    //
    // Both radio branches in PlayGameMusic return early (Spotify radio suppresses UPS; pool radio
    // keeps playing and ignores game switches), which sits ABOVE the PlayOnlyOnGameSelect gate in the
    // normal-playback path — so before this the setting was unreachable whenever radio was on. This
    // decides whether those branches instead fall through to normal game playback.
    public static class RadioGameSelectPolicy
    {
        // True when the radio (UPS pool or Spotify) must yield to the selected game's own music.
        // Radio is the browsing/ambient layer, exactly the role default music plays when radio is off:
        // List view -> radio, Details view -> the selected game's music, back to List -> radio resumes.
        public static bool ShouldYieldToSelectedGame(
            bool isFullscreenDetailsView, int selectedGameSongCount, UniPlaySongSettings settings)
        {
            if (settings == null)
                return false;

            // Both switches must be on. RadioModeEnabled covers Spotify radio too, since
            // SpotifyRadioMode is derived (RadioModeEnabled && RadioMusicSource == Spotify).
            if (!settings.PlayOnlyOnGameSelect || !settings.RadioModeEnabled)
                return false;

            // Fullscreen-only concept; callers pass false in Desktop mode, where the radio keeps
            // playing exactly as it did before.
            if (!isFullscreenDetailsView)
                return false;

            // Nothing to yield to — a game with no songs leaves the radio playing rather than
            // dropping into silence.
            return selectedGameSongCount > 0;
        }
    }
}
