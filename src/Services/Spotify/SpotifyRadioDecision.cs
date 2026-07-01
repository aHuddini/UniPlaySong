namespace UniPlaySong.Services.Spotify
{
    // The two-flag radio state. UPS only ever resumes a pause it OWNS (a lifecycle pause);
    // an external Spotify pause is recorded and respected until the user resumes.
    public struct SpotifyRadioState
    {
        public bool LifecyclePausedByUps;   // UPS paused Spotify for game launch / video / lock / etc.
        public bool UserPausedExternally;   // user paused Spotify in the Spotify app; hands-off.
    }

    public enum SpotifyRadioAction { None, Play, Pause }

    // Pure radio decision. No SMTC, no threading. Given the current inputs, return the control
    // action to issue and the next flag state.
    public static class SpotifyRadioDecision
    {
        public static (SpotifyRadioAction action, SpotifyRadioState next) Decide(
            bool radioOn, bool lifecyclePaused, bool spotifyIsPlaying, SpotifyRadioState prev)
        {
            var next = prev;

            // Radio off: never command Spotify; clear ownership flags.
            if (!radioOn)
            {
                next.LifecyclePausedByUps = false;
                next.UserPausedExternally = false;
                return (SpotifyRadioAction.None, next);
            }

            // Lifecycle pause in effect: pause and record that UPS owns this pause.
            if (lifecyclePaused)
            {
                next.LifecyclePausedByUps = true;
                return (SpotifyRadioAction.Pause, next);
            }

            // Not lifecycle-paused. If UPS owned a lifecycle pause, clear it and resume.
            if (prev.LifecyclePausedByUps)
            {
                next.LifecyclePausedByUps = false;
                next.UserPausedExternally = false; // a UPS-driven resume takes the wheel back
                return (SpotifyRadioAction.Play, next);
            }

            // No lifecycle pause and UPS didn't own one. Look at Spotify's actual state.
            if (spotifyIsPlaying)
            {
                // Playing (user resumed, or normal) → clear any external-pause record; do nothing.
                next.UserPausedExternally = false;
                return (SpotifyRadioAction.None, next);
            }

            // Spotify is paused but UPS didn't pause it → the user paused externally. Respect it.
            // Record it and issue NO resume. This also covers radio-engage where Spotify is already
            // paused by the user: UPS will not force it.
            next.UserPausedExternally = true;
            return (SpotifyRadioAction.None, next);
        }
    }
}
