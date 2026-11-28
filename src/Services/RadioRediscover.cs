using System;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // "Favour games you haven't played in a while" (RadioRediscoverGames): which games count, and the line the cards
    // show for one. A favoured game's songs are listed RadioRediscoverWeight times in the radio pool (SongPoolProvider),
    // so they come up that much more often; every other game stays in the pool.
    internal static class RadioRediscover
    {
        // Never played (no last activity), or last played RadioRediscoverMonths or more ago
        public static bool IsFavoured(DateTime? lastActivity, DateTime now)
            => lastActivity == null || lastActivity.Value <= now.AddMonths(-Constants.RadioRediscoverMonths);

        // The hover card: "From Okami · not played in 2 years" / "From Okami · never played"
        public static string Line(string gameName, DateTime? lastActivity, DateTime now)
            => $"From {gameName} · {Age(lastActivity, now)}";

        // "never played" / "not played in 8 months" / "not played in 2 years"
        public static string Age(DateTime? lastActivity, DateTime now)
        {
            if (lastActivity == null) return "never played";
            var last = lastActivity.Value;
            int months = (now.Year - last.Year) * 12 + now.Month - last.Month - (now.Day < last.Day ? 1 : 0);
            if (months >= 12)
            {
                int years = months / 12;
                return $"not played in {years} year{(years == 1 ? "" : "s")}";
            }
            return $"not played in {months} month{(months == 1 ? "" : "s")}";
        }
    }
}
