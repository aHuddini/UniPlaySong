using System;
using System.Collections.Concurrent;
using System.Threading;
using UniPlaySong.Common;

namespace UniPlaySong.Services.Spotify
{
    // The desired transport intent. Control commands coalesce to the latest one so a burst
    // of pause/resume posts never thrashes Spotify.
    public enum SpotifyControlIntent { None, Play, Pause }

    // Owns a single long-lived background thread that executes blocking SMTC actions OFF the
    // UI thread. Callers post work from any thread and return immediately; nothing here ever
    // blocks the caller. The worker has no WinRT dependency — the client supplies the SMTC
    // lambdas — so it is fully unit-testable and reusable. Fail-safe: a throwing action is
    // caught and logged; the worker never dies.
    public class SpotifySmtcWorker : IDisposable
    {
        private readonly FileLogger _fileLogger;
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private Thread _thread;
        private volatile bool _stopping;

        // Coalesced control intent: the latest Play/Pause wins. Guarded by _controlLock.
        private readonly object _controlLock = new object();
        private SpotifyControlIntent _pendingIntent = SpotifyControlIntent.None;
        private Action<SpotifyControlIntent> _pendingExecute;
        private bool _controlEnqueued;

        public SpotifySmtcWorker(FileLogger fileLogger)
        {
            _fileLogger = fileLogger;
        }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "UPS-SpotifySmtcWorker"
            };
            _thread.Start();
        }

        private void RunLoop()
        {
            try
            {
                foreach (var action in _queue.GetConsumingEnumerable())
                {
                    if (_stopping) break;
                    try { action?.Invoke(); }
                    catch (Exception ex) { _fileLogger?.Debug($"[SpotifyWorker] action failed: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[SpotifyWorker] loop terminated: {ex.Message}");
            }
        }

        // Enqueue a non-coalesced action (e.g. a metadata read whose own callback marshals results).
        public void PostRequest(Action work)
        {
            if (work == null || _stopping) return;
            try { _queue.Add(work); }
            catch (Exception ex) { _fileLogger?.Debug($"[SpotifyWorker] PostRequest failed: {ex.Message}"); }
        }

        // Enqueue a control command. Coalesces to the latest intent: if several controls queue
        // up before the worker drains them, only the last intent's execute runs. `execute` is
        // invoked on the worker thread with the winning intent.
        public void PostControl(SpotifyControlIntent intent, Action<SpotifyControlIntent> execute)
        {
            if (intent == SpotifyControlIntent.None || execute == null || _stopping) return;
            bool needEnqueue = false;
            lock (_controlLock)
            {
                _pendingIntent = intent;      // latest wins
                _pendingExecute = execute;
                if (!_controlEnqueued) { _controlEnqueued = true; needEnqueue = true; }
            }
            if (needEnqueue)
            {
                PostRequest(() =>
                {
                    SpotifyControlIntent runIntent;
                    Action<SpotifyControlIntent> runExecute;
                    lock (_controlLock)
                    {
                        runIntent = _pendingIntent;
                        runExecute = _pendingExecute;
                        _controlEnqueued = false;
                        _pendingIntent = SpotifyControlIntent.None;
                        _pendingExecute = null;
                    }
                    if (runIntent != SpotifyControlIntent.None) runExecute?.Invoke(runIntent);
                });
            }
        }

        public void Stop()
        {
            if (_stopping) return;
            _stopping = true;
            try { _queue.CompleteAdding(); } catch { }
            try { _thread?.Join(TimeSpan.FromSeconds(2)); } catch { }
            _thread = null;
        }

        public void Dispose()
        {
            Stop();
            try { _queue.Dispose(); } catch { }
        }
    }
}
