using System;

namespace UniPlaySong.Common
{
    // Durations measured in hours rather than minutes.
    //
    // Shared by the settings view model and the dashboard. The other duration helpers print m:ss
    // (SongTitleCleaner.FormatDuration, MusicInfoCard), which reads as nonsense past an hour ("143:07").
    public static class DurationFormatter
    {
        // "< 1m", "47m", "3h 12m", "2d 4h 30m".
        //
        // Never zero: a listening figure of "0m" after a session reads as broken recording rather
        // than as a short session, which is the one reading it must not invite.
        public static string Humanize(TimeSpan duration)
        {
            if (duration.TotalMinutes < 1)
            {
                return "< 1m";
            }

            int days = (int)duration.TotalDays;
            int hours = duration.Hours;
            int minutes = duration.Minutes;

            if (days > 0)
            {
                return $"{days}d {hours}h {minutes}m";
            }

            if (hours > 0)
            {
                return $"{hours}h {minutes}m";
            }

            return $"{minutes}m";
        }
    }
}
