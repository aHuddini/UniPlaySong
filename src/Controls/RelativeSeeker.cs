using System;
using UniPlaySong.Services;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Controls
{
    // Moves the current song by relative amounts, for gestures that send many small steps (turning the disc, holding
    // the stick over). Steps add up; the audio jumps at most once per interval, and Flush lands whatever is left.
    internal sealed class RelativeSeeker
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(120);

        private readonly Func<IMusicPlaybackService> _playback;
        private readonly IActiveMediaService _activeMedia;
        private double _pendingSeconds;
        private DateTime _lastSeek;

        public RelativeSeeker(Func<IMusicPlaybackService> playback, IActiveMediaService activeMedia)
        {
            _playback = playback;
            _activeMedia = activeMedia;
        }

        public bool CanSeek => _playback?.Invoke()?.CanSeek == true;

        public void Add(double seconds)
        {
            _pendingSeconds += seconds;
            if (DateTime.UtcNow - _lastSeek >= Interval) Flush();
        }

        public void Flush()
        {
            if (_pendingSeconds == 0) return;
            var playback = _playback?.Invoke();
            var now = playback?.CurrentTime;
            if (playback?.CanSeek == true && now.HasValue)
            {
                playback.Seek(now.Value + TimeSpan.FromSeconds(_pendingSeconds));
                _activeMedia?.Poll(); // timelines show the new position now, not at the next 1 s tick
            }
            _pendingSeconds = 0;
            _lastSeek = DateTime.UtcNow;
        }
    }
}
