using System;

namespace UniPlaySong.Features.MusicInfoCard.Player
{
    // The Play/Pause ring is the song's progress, 12 o'clock = start, clockwise. The mouse puts the knob where it points.
    internal static class RingSeek
    {
        // (x, y): the pointer from the ring's centre, screen axes (y down). previous: the last fraction of this drag, so
        // crossing 12 o'clock stops at the end (or the start) instead of jumping the whole song.
        public static double FractionAt(double x, double y, double? previous)
        {
            double degrees = Math.Atan2(y, x) * 180 / Math.PI + 90; // 0 at 12 o'clock
            double fraction = ((degrees % 360) + 360) % 360 / 360;
            if (previous != null)
            {
                if (previous > 0.75 && fraction < 0.25) return 1.0;
                if (previous < 0.25 && fraction > 0.75) return 0.0;
            }
            return fraction;
        }
    }
}
