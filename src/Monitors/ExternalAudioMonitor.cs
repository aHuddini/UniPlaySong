using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Monitors
{
    // Pauses UPS while another app plays audio and resumes after it goes quiet ("Pause on external audio").
    // Created once in OnApplicationStarted; SetEnabled follows the setting.
    public class ExternalAudioMonitor : IDisposable
    {
        private static readonly int _selfPid = System.Diagnostics.Process.GetCurrentProcess().Id;

        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly IMusicPlaybackService _playbackService;
        private readonly FileLogger _fileLogger;
        private readonly Func<bool> _isDesktop;

        // Read live: a settings save replaces the whole settings object.
        private UniPlaySongSettings _settings => _getSettings();

        // External audio detection (polls Windows audio sessions to pause when other apps play audio).
        // Uses System.Timers.Timer (ThreadPool) to keep COM interop off the UI thread.
        private System.Timers.Timer _externalAudioPollTimer;
        private int _externalAudioPollRunning; // Re-entrancy guard (0=idle, 1=running)
        private volatile bool _externalAudioDetected;
        private int _externalAudioDebounceCount;
        private int _externalAudioSilenceCount;
        private volatile bool _externalAudioPausedInstantly; // Tracks whether current pause used instant mode (ensures resume matches)
        private HashSet<string> _externalAudioExcludedPids = new HashSet<string>(); // Cached excluded process names (lowercase)
        // Same set + "spotify" — used while UPS conducts Spotify so its own audio can't self-trigger the pause.
        private HashSet<string> _externalAudioExcludedPidsWithSpotify = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "spotify" };
        private string _externalAudioExcludedAppsRaw = ""; // Tracks setting value to know when to rebuild cache
        private bool IsExternalAudioInstantMode => _settings?.ExternalAudioInstantPause == true || (_settings?.ExternalAudioDebounceSeconds ?? 3) == 0;
        private double ExternalAudioPollIntervalMs => IsExternalAudioInstantMode ? 500.0 : 1000.0;
        private int ExternalAudioDebounceThreshold => (_settings?.ExternalAudioDebounceSeconds ?? 3) == 0
            ? 1 : Math.Max(1, (int)Math.Ceiling((_settings?.ExternalAudioDebounceSeconds ?? 3) * 1000.0 / ExternalAudioPollIntervalMs));

        public ExternalAudioMonitor(Func<UniPlaySongSettings> getSettings, IMusicPlaybackService playbackService,
            FileLogger fileLogger, Func<bool> isDesktop)
        {
            _getSettings = getSettings;
            _playbackService = playbackService;
            _fileLogger = fileLogger;
            _isDesktop = isDesktop;

            _externalAudioPollTimer = new System.Timers.Timer(1500);
            _externalAudioPollTimer.Elapsed += OnExternalAudioPollTick;
            _externalAudioPollTimer.AutoReset = true;
            if (_settings?.PauseOnExternalAudio == true)
                _externalAudioPollTimer.Start();
        }

        public void SetEnabled(bool enabled)
        {
            if (enabled)
                _externalAudioPollTimer?.Start();
            else
                _externalAudioPollTimer?.Stop();
        }

        public void Dispose()
        {
            _externalAudioPollTimer?.Stop();
            _externalAudioPollTimer?.Dispose();
            _externalAudioPollTimer = null;
        }

        // Polls Windows audio sessions to detect external audio playback.
        // Pauses UPS after sustained external audio (~3s), resumes after sustained silence (~3s).
        private void OnExternalAudioPollTick(object sender, EventArgs e)
        {
            // Re-entrancy guard — skip if previous tick is still running (COM can be slow)
            if (Interlocked.CompareExchange(ref _externalAudioPollRunning, 1, 0) != 0)
                return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Adjust poll rate dynamically (0.5s in instant mode, 1.0s otherwise)
                var targetMs = ExternalAudioPollIntervalMs;
                if (Math.Abs(_externalAudioPollTimer.Interval - targetMs) > 1.0)
                    _externalAudioPollTimer.Interval = targetMs;

                if (_settings?.PauseOnExternalAudio != true)
                {
                    if (_externalAudioDetected)
                    {
                        _externalAudioDetected = false;
                        _externalAudioDebounceCount = 0;
                        _externalAudioSilenceCount = 0;
                        var wasInstant = _externalAudioPausedInstantly;
                        _externalAudioPausedInstantly = false;
                        DispatchPauseAction(() =>
                        {
                            if (wasInstant)
                                _playbackService?.RemovePauseSourceImmediate(Models.PauseSource.ExternalAudio);
                            else
                                _playbackService?.RemovePauseSource(Models.PauseSource.ExternalAudio);
                        });
                    }
                    return;
                }

                // Rebuild excluded apps cache if setting changed
                var excludedRaw = _settings?.ExternalAudioExcludedApps ?? "";
                if (excludedRaw != _externalAudioExcludedAppsRaw)
                {
                    _externalAudioExcludedAppsRaw = excludedRaw;
                    _externalAudioExcludedPids = new HashSet<string>(
                        excludedRaw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim().ToLowerInvariant())
                            .Where(s => s.Length > 0),
                        StringComparer.OrdinalIgnoreCase);
                    _externalAudioExcludedPidsWithSpotify = new HashSet<string>(
                        _externalAudioExcludedPids, StringComparer.OrdinalIgnoreCase) { "spotify" };
                }

                // When UPS is conducting Spotify as the audible music, exclude Spotify's own session from DETECTION (instead of
                // skipping all detections, which also ignored real external audio like a browser and never paused Spotify
                // radio). Spotify's session can never trip, so no pause-oscillation; other apps detect normally and the
                // standard pause path pauses Spotify via the lifecycle machinery.
                bool spotifyIsAudible = _settings?.SpotifyActive == true
                    && (_playbackService?.IsPlayingDefaultMusic == true || _settings?.SpotifyRadioMode == true);
                var exclusions = spotifyIsAudible && _externalAudioExcludedPidsWithSpotify != null
                    ? _externalAudioExcludedPidsWithSpotify
                    : _externalAudioExcludedPids;

                float peakThreshold = IsExternalAudioInstantMode ? 0.005f : 0.01f;
                bool audioFound = Common.AudioSessionDetector.IsExternalAudioPlaying(
                    _selfPid, peakThreshold, exclusions, out var externalAudioSource);

                if (audioFound)
                {
                    _externalAudioSilenceCount = 0;
                    _externalAudioDebounceCount++;

                    if (!_externalAudioDetected && _externalAudioDebounceCount >= ExternalAudioDebounceThreshold)
                    {
                        // Spotify-as-active-music is handled at DETECTION (its session is in the
                        // exclusion set while spotifyIsAudible), so a detection here is real
                        // external audio and pauses normally — including pausing Spotify radio.

                        // Don't treat a launched game's own audio as "external." When a game session is active, the audio we're hearing
                        // is almost certainly the game itself. Adding ExternalAudio on top creates a stuck state via
                        // KeepPausedAfterExternalAudio when the game audio oscillates (common with windowed games that produce
                        // intermittent silence).
                        if (_playbackService?.IsGameSessionActive == true)
                        {
                            _externalAudioDetected = true;
                            _externalAudioPausedInstantly = false;
                            _fileLogger?.Debug($"External audio detected (source: {externalAudioSource ?? "unknown"}), but a game is currently launching/running — skipping ExternalAudio pause source (game audio is expected, not a real external source)");
                            return;
                        }

                        _externalAudioDetected = true;
                        _externalAudioPausedInstantly = _settings?.ExternalAudioInstantPause == true;
                        var useInstant = _externalAudioPausedInstantly;
                        _fileLogger?.Debug($"External audio detected (source: {externalAudioSource ?? "unknown"}), pausing{(useInstant ? " (instant)" : "")}");
                        DispatchPauseAction(() =>
                        {
                            if (useInstant)
                                _playbackService?.AddPauseSourceImmediate(Models.PauseSource.ExternalAudio);
                            else
                                _playbackService?.AddPauseSource(Models.PauseSource.ExternalAudio);
                        });
                    }
                }
                else
                {
                    _externalAudioDebounceCount = 0;
                    _externalAudioSilenceCount++;

                    if (_externalAudioDetected && _externalAudioSilenceCount >= ExternalAudioDebounceThreshold)
                    {
                        _externalAudioDetected = false;
                        var wasInstant = _externalAudioPausedInstantly;
                        _externalAudioPausedInstantly = false;

                        // Desktop-only: honor "keep paused after external audio" — when on, UPS stays paused until the user manually
                        // resumes (media keys, top panel toggle). In Fullscreen, we always auto-resume regardless of this setting since
                        // there's no persistent on-screen unpause control in Fullscreen. Note: manual resume paths
                        // (TopPanelMediaControl, MusicLibrary) explicitly call RemovePauseSource(ExternalAudio) as part of their play
                        // action, so resume still works — just not automatically.
                        bool stayPaused = _isDesktop() && _settings?.KeepPausedAfterExternalAudio == true;
                        if (stayPaused)
                        {
                            _fileLogger?.Debug("External audio stopped, staying paused (KeepPausedAfterExternalAudio)");
                            return;
                        }

                        _fileLogger?.Debug($"External audio stopped, resuming{(wasInstant ? " (instant)" : "")}");
                        DispatchPauseAction(() =>
                        {
                            if (wasInstant)
                                _playbackService?.RemovePauseSourceImmediate(Models.PauseSource.ExternalAudio);
                            else
                                _playbackService?.RemovePauseSource(Models.PauseSource.ExternalAudio);
                        });
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException comEx)
            {
                // COM threading issue — WASAPI may require STA on some systems
                _fileLogger?.Debug($"External audio poll COM error: {comEx.Message} (HRESULT: 0x{comEx.HResult:X8})");
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"External audio poll error: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                sw.Stop();
                // Only flag genuinely abnormal ticks. This poll runs 1–2×/sec forever, and routine
                // work here regularly takes 50–200ms. 250ms flags real stalls without the per-tick noise.
                if (sw.ElapsedMilliseconds > 250)
                    _fileLogger?.Debug(() => $"[Perf] ExternalAudioPoll: {sw.ElapsedMilliseconds}ms (slow tick)");
                Interlocked.Exchange(ref _externalAudioPollRunning, 0);
            }
        }

        // Dispatches a pause source action to the UI thread (non-blocking)
        private void DispatchPauseAction(Action action)
        {
            Application.Current?.Dispatcher?.BeginInvoke(action);
        }
    }
}
