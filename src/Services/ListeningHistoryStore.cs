using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Playnite.SDK;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    // Accumulates and persists what has actually been listened to.
    //
    // Modelled on SearchCacheService: versioned root, snake_case DTOs, a settings-driven enable
    // flag, and an atomic temp-file-then-move save. Deliberately not a new pattern - a side-file
    // that loads differently or saves non-atomically is the one that ends up corrupt.
    //
    // ponytail: the whole document is rewritten on every flush. Fine at library scale - a few
    // hundred tracks is a few tens of KB - and flushes happen per track change, not per second.
    // Revisit if the file ever passes a few thousand track entries.
    public class ListeningHistoryStore
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();

        private readonly string _filePath;
        private readonly object _lock = new object();

        // Separate from _lock on purpose. Flushes are queued to the thread pool, so two can be in
        // flight at once; without this they race over the same temp file and one File.Move throws.
        private readonly object _writeLock = new object();

        private readonly Common.FileLogger _fileLogger;

        // Below this a span is a loading artefact, not a listen. See Record().
        private const double NegligibleSpanSeconds = 0.05;

        private ListeningHistoryData _data;
        private bool _dirty;
        private bool _enabled;

        public ListeningHistoryStore(string extensionDataPath, bool enabled, Common.FileLogger fileLogger = null)
        {
            _filePath = Path.Combine(extensionDataPath ?? string.Empty, "listening-history.json");
            _enabled = enabled;
            _fileLogger = fileLogger;

            Load();
        }

        // Flipped live from the settings dialog. Turning it off stops recording immediately but
        // keeps what is already on disk - clearing is a separate, deliberate action.
        public bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        public string FilePath
        {
            get { return _filePath; }
        }

        // Folds one finished stretch of listening into the totals.
        //
        // Seconds accumulate even when the stretch is too short to count as a play: skipping
        // through fifty tracks is half a minute of listening, not fifty plays and not zero seconds.
        public void Record(ListeningSpan span)
        {
            if (!_enabled || span == null || string.IsNullOrEmpty(span.Path))
            {
                return;
            }

            // A span with no measurable audible time is not evidence of anything.
            //
            // Set low on purpose. The job is to drop true no-ops - a game switch that loads and
            // closes a track without ever sounding it produces a span of microseconds - not to
            // judge whether a listen was worthwhile. Half a second, the first value here, was
            // already deciding that a track heard briefly never happened, and it silently threw
            // away real listening time.
            if (span.Seconds < NegligibleSpanSeconds)
            {
                return;
            }

            lock (_lock)
            {
                var now = DateTime.UtcNow;

                TrackListeningRecord track;
                if (!_data.Tracks.TryGetValue(span.Path, out track))
                {
                    track = new TrackListeningRecord { Path = span.Path };
                    _data.Tracks[span.Path] = track;
                }

                // Refreshed every time, so a track renamed on disk stops showing its old name.
                track.Title = string.IsNullOrEmpty(span.Title) ? SafeFileName(span.Path) : span.Title;
                track.TotalSeconds += span.Seconds;
                track.LastPlayedUtc = now;

                _data.TotalSeconds += span.Seconds;

                if (span.CountsAsPlay)
                {
                    track.PlayCount++;
                    _data.TotalPlays++;
                }

                // Default and radio music has no meaningful game. Attributing it to whichever game
                // happened to be under the cursor would invent a figure rather than record one.
                if (!string.IsNullOrEmpty(span.GameId))
                {
                    GameListeningRecord game;
                    if (!_data.Games.TryGetValue(span.GameId, out game))
                    {
                        game = new GameListeningRecord { GameId = span.GameId };
                        _data.Games[span.GameId] = game;
                    }

                    if (!string.IsNullOrEmpty(span.GameName))
                    {
                        game.GameName = span.GameName;
                    }

                    game.TotalSeconds += span.Seconds;
                    game.LastPlayedUtc = now;

                    if (span.CountsAsPlay)
                    {
                        game.PlayCount++;
                    }
                }

                _dirty = true;
            }

            /* Written now, on a pool thread, rather than only at shutdown.
               Record is called on the UI thread once per track change - not per second - so the
               cost is one small JSON write per song, and it means an evening of listening survives
               Playnite being killed rather than closed. The IO is queued off the UI thread because
               the callers are the playback service's own start and stop paths. */
            System.Threading.ThreadPool.QueueUserWorkItem(_ => Flush());
        }

        // Writes only when something changed, so a quiet session costs no disk IO at all.
        public void Flush()
        {
            string json;

            lock (_lock)
            {
                if (!_dirty)
                {
                    return;
                }

                // Serialized inside the lock, written outside it. DynamicColorCache.Save does the
                // same, and it is the better-written of the two existing side-file writers.
                json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                _dirty = false;
            }

            WriteAtomic(json);
        }

        // A copy of the records, for anything that wants to ask a question of them.
        //
        // The only read path, and deliberately the only one: this class keeps records and knows
        // nothing about what a "top game" is. ListeningInsights answers that, as pure functions
        // over what comes back from here. Adding a metric never touches this file.
        //
        // Copied rather than handed out live, so a caller cannot hold the lock while it sorts, and
        // cannot mutate the totals by accident.
        public ListeningSnapshot Snapshot()
        {
            lock (_lock)
            {
                return new ListeningSnapshot
                {
                    TotalSeconds = _data.TotalSeconds,
                    TotalPlays = _data.TotalPlays,
                    Tracks = _data.Tracks.Values.ToList(),
                    Games = _data.Games.Values.ToList()
                };
            }
        }

        public long GetFileSizeBytes()
        {
            try
            {
                return File.Exists(_filePath) ? new FileInfo(_filePath).Length : 0L;
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        // Deletes everything, in memory and on disk. Irreversible by design - the confirmation
        // belongs to the caller.
        public void Clear()
        {
            lock (_lock)
            {
                _data = new ListeningHistoryData();
                _dirty = false;
            }

            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[Listening] Could not delete the history file");
                _fileLogger?.Error("[Listening] Could not delete the history file", ex);
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    _data = new ListeningHistoryData();
                    return;
                }

                var json = File.ReadAllText(_filePath);
                _data = JsonConvert.DeserializeObject<ListeningHistoryData>(json);

                // A truncated or hand-edited file starts fresh rather than throwing. This runs
                // during plugin startup; an exception here would take the whole plugin down.
                if (_data == null || _data.Tracks == null || _data.Games == null ||
                    _data.Version != ListeningHistoryData.CurrentVersion)
                {
                    _data = new ListeningHistoryData();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[Listening] Could not read the history file, starting fresh: " + ex.Message);
                _data = new ListeningHistoryData();
            }
        }

        private void WriteAtomic(string json)
        {
            lock (_writeLock)
            {
                WriteAtomicCore(json);
            }
        }

        private void WriteAtomicCore(string json)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempPath = _filePath + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }

                File.Move(tempPath, _filePath);
            }
            catch (Exception ex)
            {
                // Non-critical: the in-memory totals are still right for this session and the next
                // flush tries again.
                Logger.Error(ex, "[Listening] Could not save the history file");
                _fileLogger?.Error("[Listening] Could not save the history file", ex);
            }
        }

        private static string SafeFileName(string path)
        {
            try
            {
                return Path.GetFileNameWithoutExtension(path);
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
