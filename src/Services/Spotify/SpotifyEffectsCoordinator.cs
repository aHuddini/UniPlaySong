using System;

namespace UniPlaySong.Services.Spotify
{
    // Owns the Spotify live-effects/visualizer lifecycle and THE SAFETY INVARIANT:
    // Spotify's dry output is muted iff we are producing effected output. Any stop of
    // effected output unmutes immediately. Collaborators are injected as funcs so the
    // invariant is unit-testable without real audio/COM.
    public class SpotifyEffectsCoordinator
    {
        private readonly Func<bool> _isLiveEffects, _isApplyToSpotify, _isVisualizer, _isSpotifyActive, _isOsCapable, _isCalmDown;
        private readonly Func<bool, bool> _setMuted;      // returns success
        private readonly Func<bool> _startCapture;        // returns success
        private readonly Action _stopCapture, _startEffectedOutput, _stopEffectedOutput;

        private bool _capturing, _effecting, _muted;

        public SpotifyEffectsCoordinator(
            Func<bool> isLiveEffects, Func<bool> isApplyToSpotify, Func<bool> isVisualizer,
            Func<bool> isSpotifyActive, Func<bool> isOsCapable,
            Func<bool, bool> setMuted, Func<bool> startCapture, Action stopCapture,
            Action startEffectedOutput, Action stopEffectedOutput,
            Func<bool> isCalmDown = null)
        {
            _isLiveEffects = isLiveEffects; _isApplyToSpotify = isApplyToSpotify; _isVisualizer = isVisualizer;
            _isSpotifyActive = isSpotifyActive; _isOsCapable = isOsCapable;
            _setMuted = setMuted; _startCapture = startCapture; _stopCapture = stopCapture;
            _startEffectedOutput = startEffectedOutput; _stopEffectedOutput = stopEffectedOutput;
            _isCalmDown = isCalmDown ?? (() => false);
        }

        // Re-reads all conditions and transitions to the correct state. Idempotent.
        public void Evaluate()
        {
            bool spotify = _isSpotifyActive() && _isOsCapable();
            // Effected output (duck Spotify + play our processed copy) is wanted when either
            // Live-Effects-to-Spotify OR Calm Down is on — both need the mute-and-replace path.
            bool wantEffects = spotify && ((_isLiveEffects() && _isApplyToSpotify()) || _isCalmDown());
            bool wantCapture = spotify && (wantEffects || _isVisualizer());

            // 1. Tear down effected output first if it's no longer wanted (invariant: unmute on stop).
            if (_effecting && !wantEffects) StopEffected();

            // 2. Stop capture if not wanted.
            if (_capturing && !wantCapture) { _stopCapture(); _capturing = false; }

            // 3. Start capture if wanted.
            if (!_capturing && wantCapture) _capturing = _startCapture();

            // 4. Start effected output if wanted and capturing. Mute FIRST; if mute fails, don't start.
            if (wantEffects && _capturing && !_effecting)
            {
                if (_setMuted(true)) { _muted = true; _startEffectedOutput(); _effecting = true; }
                // mute failed -> stay dry (no doubled audio)
            }

            // 5. While effecting, re-assert the duck on every evaluate: Spotify (re)creates audio
            //    sessions on output-device switches and lazily per device, and a fresh session
            //    spawns at that device's stored app volume — UNducked, doubling the audio until
            //    someone re-ducks it. _setMuted(true) is idempotent, writes every Spotify session,
            //    and preserves the saved restore level, so this is cheap insurance.
            if (wantEffects && _effecting && _muted) _setMuted(true);
        }

        private void StopEffected()
        {
            if (_effecting) { _stopEffectedOutput(); _effecting = false; }
            if (_muted) { _setMuted(false); _muted = false; }   // invariant: unmute
        }

        // Stop everything + unmute. Call on source change to non-Spotify, disable, dispose, shutdown.
        public void Shutdown()
        {
            StopEffected();
            if (_capturing) { _stopCapture(); _capturing = false; }
        }
    }
}
