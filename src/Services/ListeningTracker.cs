using System;
using System.Diagnostics;
using System.IO;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    // Measures how long the current track was actually audible.
    //
    // Why a stopwatch and not the obvious two alternatives:
    //
    //   DateTime.Now - _songStartTime is wrong. _songStartTime is never re-marked on resume, so a
    //   track paused for an hour reports an hour of listening. It is right for what it was built
    //   for - the preview timer - and wrong for this.
    //
    //   IMusicPlayer.CurrentTime is worse. Reading it on a chiptune track while a background
    //   gme_seek is in flight blocks the calling thread for hundreds of milliseconds to seconds
    //   (see the comment in NAudioMusicPlayer.Pause), and this is the UI thread.
    //
    // So the elapsed time is accumulated from the pause and resume transitions the playback
    // service already raises. Nothing is ever asked of the player.
    public class ListeningTracker
    {
        // The scrobbling convention, and it holds up: half a minute is long enough to mean you
        // chose to listen rather than passed through on the way somewhere else.
        public const double MinimumSecondsForPlay = 30.0;

        private readonly ListeningHistoryStore _store;
        private readonly Stopwatch _audible = new Stopwatch();

        private string _path;
        private string _title;
        private string _gameId;
        private string _gameName;

        public ListeningTracker(ListeningHistoryStore store)
        {
            _store = store;
        }

        public bool IsTracking
        {
            get { return !string.IsNullOrEmpty(_path); }
        }

        // A track started. Whatever was in flight is banked first, so a track change needs no
        // separate call - which matters, because MarkSongStart is the only point every one of the
        // service's fourteen start paths passes through.
        public void Begin(string path, string gameId, string gameName)
        {
            Flush(endedNaturally: false);

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            _path = path;
            _title = SafeTitle(path);
            _gameId = gameId;
            _gameName = gameName;

            _audible.Reset();
            _audible.Start();
        }

        // Audio stopped being audible: a pause source arrived, a fade-out began, focus was lost.
        // Idempotent, because pause sources stack and only the first one is a real transition.
        public void Suspend()
        {
            if (IsTracking && _audible.IsRunning)
            {
                _audible.Stop();
            }
        }

        public void Resume()
        {
            if (IsTracking && !_audible.IsRunning)
            {
                _audible.Start();
            }
        }

        // The track reached its end on its own. This is the only signal that distinguishes a short
        // track heard in full from a long track abandoned early, which is why a twelve-second
        // chiptune loop that completed counts as a play and a twelve-second skip does not.
        public void Complete()
        {
            Flush(endedNaturally: true);
        }

        // Playback stopped without the track finishing - stop, shutdown, a switch to another game.
        public void Abandon()
        {
            Flush(endedNaturally: false);
        }

        private void Flush(bool endedNaturally)
        {
            if (!IsTracking)
            {
                return;
            }

            _audible.Stop();
            var seconds = _audible.Elapsed.TotalSeconds;

            var span = new ListeningSpan
            {
                Path = _path,
                Title = _title,
                GameId = _gameId,
                GameName = _gameName,
                Seconds = seconds,
                CountsAsPlay = endedNaturally || seconds >= MinimumSecondsForPlay
            };

            _path = null;
            _title = null;
            _gameId = null;
            _gameName = null;
            _audible.Reset();

            _store?.Record(span);
        }

        private static string SafeTitle(string path)
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
