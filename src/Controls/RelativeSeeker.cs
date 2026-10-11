using System;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Controls
{
    // Moves the active song (UPS music or Spotify) by relative amounts, for gestures that send many small steps
    // (turning the disc, holding the stick over). Steps add up; the audio jumps at most once per interval, and Flush
    // lands whatever is left.
    internal sealed class RelativeSeeker
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(120);

        private readonly IActiveMediaService _activeMedia;
        private double _pendingSeconds;
        private DateTime _lastSeek;

        public RelativeSeeker(IActiveMediaService activeMedia)
        {
            _activeMedia = activeMedia;
        }

        public bool CanSeek => _activeMedia?.CanSeek == true;

        public void Add(double seconds)
        {
            _pendingSeconds += seconds;
            if (DateTime.UtcNow - _lastSeek >= Interval) Flush();
        }

        public void Flush()
        {
            if (_pendingSeconds == 0) return;
            _activeMedia?.SeekBy(_pendingSeconds);
            _pendingSeconds = 0;
            _lastSeek = DateTime.UtcNow;
        }
    }
}
