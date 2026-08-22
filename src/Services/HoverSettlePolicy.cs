namespace UniPlaySong.Services
{
    // Pure decision for the PS5-style hover settle delay. No state, no threading, no timers.
    // Mirrors RadioPlayThroughPolicy so the rule is unit-testable without constructing
    // MusicPlaybackCoordinator or a player.
    public static class HoverSettlePolicy
    {
        // True when a game selection should wait for the selection to rest before its music
        // starts, instead of playing immediately.
        //
        // The delay exists to stop a fast library scroll tearing down the music already playing
        // to load tracks it abandons a moment later. It therefore only applies when there is
        // something to protect:
        //
        //   isPlaying == false  ->  never defer. Waiting on silence turns music into more
        //                           silence, which is strictly worse than starting now.
        //   isFullscreen == false -> never defer. Desktop selection is click-driven rather than
        //                           scrolled, so a delay there reads as lag.
        public static bool ShouldDefer(bool enabled, bool isFullscreen, bool isPlaying)
        {
            if (!enabled) return false;
            if (!isFullscreen) return false;
            if (!isPlaying) return false;
            return true;
        }

        // Clamps a configured delay into the supported range. Settings already clamps on set,
        // but a value deserialized from an older or hand-edited config bypasses the setter.
        public static double ClampSeconds(double seconds)
        {
            if (seconds < Common.Constants.MinHoverSettleSeconds) return Common.Constants.MinHoverSettleSeconds;
            if (seconds > Common.Constants.MaxHoverSettleSeconds) return Common.Constants.MaxHoverSettleSeconds;
            return seconds;
        }

        // How long the timer should actually wait, given that the transition it triggers is not
        // instant. Switching away from the playing track fades it out first, so a timer armed for
        // the full slider value puts the new music at slider + fadeOut + fadeIn - a 3s setting
        // landed at roughly 4.5s, which reads as the slider lying.
        //
        // The slider means "how long until the game's music starts". Spending the fade-out inside
        // that budget makes it mean that: the old track begins fading at (slider - fadeOut) and the
        // new one starts exactly on the slider mark. The fade-in then runs from there, because a
        // track that starts fading in IS the music starting.
        //
        // Never goes below MinHoverSettleSeconds - a long fade-out must not collapse the wait to
        // nothing, which would restore the scroll-chop the feature exists to prevent.
        public static double TimerSeconds(double settleSeconds, double fadeOutSeconds)
        {
            var settle = ClampSeconds(settleSeconds);
            if (fadeOutSeconds <= 0) return settle;

            var armed = settle - fadeOutSeconds;
            return armed < Common.Constants.MinHoverSettleSeconds
                ? Common.Constants.MinHoverSettleSeconds
                : armed;
        }
    }
}
