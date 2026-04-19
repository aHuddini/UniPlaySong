using System;
using System.Windows.Threading;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Polls the current song's position and fires a crossfade into the next song when
    // the remaining time drops to the configured crossfade duration. Used by
    // MusicPlaybackService in place of ScheduleSongEndFade when EnableTrueCrossfade is on.
    //
    // Owns a single 500ms DispatcherTimer. Fires crossfade at most once per scheduled
    // session; the timer stops itself after firing or when explicitly cancelled.
    //
    // Next-song selection is delegated to MusicPlaybackService via a Func<string> at
    // schedule time — this captures the current auto-advance CONTEXT (radio pool /
    // default pool / RandomizeOnMusicEnd game folder).
    public class CrossfadeCoordinator
    {
        private const string LogPrefix = "Crossfade";
        private const int PollIntervalMs = 500;

        private readonly IMusicPlaybackService _playbackService;
        private readonly Func<UniPlaySongSettings> _settingsGetter;
        private readonly FileLogger _fileLogger;

        private DispatcherTimer _pollTimer;
        private Func<string> _nextSongPicker;
        private bool _hasFired;

        public CrossfadeCoordinator(
            IMusicPlaybackService playbackService,
            Func<UniPlaySongSettings> settingsGetter,
            FileLogger fileLogger)
        {
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
            _settingsGetter = settingsGetter ?? throw new ArgumentNullException(nameof(settingsGetter));
            _fileLogger = fileLogger;
        }

        // Starts polling for the current song. When position reaches
        // (TotalTime - crossfadeDuration), invokes the picker and fires the crossfade.
        // Cancels any previous polling session first.
        //
        // nextSongPicker: delegate that picks the next song when crossfade fires.
        // Returns null/empty to skip crossfade (fallback to sequential behavior).
        public void ScheduleCrossfade(Func<string> nextSongPicker)
        {
            Cancel();

            if (nextSongPicker == null)
            {
                _fileLogger?.Warn($"[{LogPrefix}] ScheduleCrossfade called with null picker — ignoring.");
                return;
            }

            _nextSongPicker = nextSongPicker;
            _hasFired = false;

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
            };
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();

            _fileLogger?.Debug($"[{LogPrefix}] Polling started (500ms interval)");
        }

        public void Cancel()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer.Tick -= OnPollTick;
                _pollTimer = null;
                _fileLogger?.Debug($"[{LogPrefix}] Polling cancelled");
            }
            _nextSongPicker = null;
            _hasFired = false;
        }

        private void OnPollTick(object sender, EventArgs e)
        {
            try
            {
                if (_hasFired) { Cancel(); return; }

                var settings = _settingsGetter();
                if (settings == null || !settings.EnableTrueCrossfade)
                {
                    _fileLogger?.Debug($"[{LogPrefix}] Setting off mid-poll — cancelling.");
                    Cancel();
                    return;
                }

                var totalTime = _playbackService.GetCurrentSongTotalTime();
                var currentTime = _playbackService.GetCurrentSongCurrentTime();

                if (totalTime == null || currentTime == null)
                {
                    // Song unloaded or backend swap in progress — just keep polling.
                    return;
                }

                double crossfadeDuration = settings.CrossfadeDurationSeconds;
                double remainingSeconds = (totalTime.Value - currentTime.Value).TotalSeconds;

                // Need at least 2s of post-crossfade buffer to be safe (per spec).
                if (remainingSeconds > crossfadeDuration + 2.0)
                {
                    // Not yet — keep polling.
                    return;
                }

                if (remainingSeconds < crossfadeDuration)
                {
                    // Song is ending too fast — crossfade duration won't fit. Skip this
                    // transition; fall back to sequential (OnMediaEnded will handle normally).
                    _fileLogger?.Debug($"[{LogPrefix}] Song too close to EOF ({remainingSeconds:F2}s remaining < {crossfadeDuration}s) — skipping crossfade.");
                    Cancel();
                    return;
                }

                // Time to fire.
                var picker = _nextSongPicker;
                if (picker == null) { Cancel(); return; }

                string nextPath;
                try
                {
                    nextPath = picker();
                }
                catch (Exception ex)
                {
                    _fileLogger?.Error($"[{LogPrefix}] Picker threw: {ex.Message}");
                    Cancel();
                    return;
                }

                if (string.IsNullOrEmpty(nextPath))
                {
                    _fileLogger?.Debug($"[{LogPrefix}] Picker returned empty — skipping crossfade.");
                    Cancel();
                    return;
                }

                _hasFired = true;
                _fileLogger?.Info($"[{LogPrefix}] Firing crossfade into {System.IO.Path.GetFileName(nextPath)} over {crossfadeDuration}s (remaining={remainingSeconds:F2}s)");
                _playbackService.StartCrossfadeIntoNext(nextPath, crossfadeDuration);

                // Stop polling — fire-once semantics. Subsequent promotion will trigger
                // MarkSongStart() on the new primary, which will call ScheduleCrossfade
                // again with a fresh picker.
                Cancel();
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"[{LogPrefix}] OnPollTick error: {ex.Message}");
                Cancel();
            }
        }
    }
}
