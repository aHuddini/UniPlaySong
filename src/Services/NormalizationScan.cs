using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UniPlaySong.Services
{
    // Which songs UPS hasn't normalized. A normalized file carries the normalization suffix in its name ("song-normalized.mp3"),
    // which is also how "Skip already normalized" decides, so this reads names only, no audio. Pure: the settings page's
    // "Find Songs That Aren't Normalized" feeds it the library and shows the report.
    internal static class NormalizationScan
    {
        public static bool IsNormalized(string path, string suffix) =>
            !string.IsNullOrEmpty(suffix) &&
            Path.GetFileNameWithoutExtension(path ?? string.Empty).IndexOf(suffix, StringComparison.OrdinalIgnoreCase) >= 0;

        // Per game, its songs that aren't normalized; games with none left out, sorted by game name
        public static List<(string Game, List<string> Songs)> Pending(IEnumerable<(string Game, IEnumerable<string> Songs)> library, string suffix) =>
            (library ?? Enumerable.Empty<(string, IEnumerable<string>)>())
                .Select(g => (g.Game, Songs: (g.Songs ?? Enumerable.Empty<string>()).Where(s => !IsNormalized(s, suffix)).ToList()))
                .Where(g => g.Songs.Count > 0)
                .OrderBy(g => g.Game, StringComparer.OrdinalIgnoreCase)
                .ToList();

        // "Game" then its song names, indented; past maxLines, how many more
        public static string Report(List<(string Game, List<string> Songs)> pending, int maxLines)
        {
            var text = new StringBuilder();
            int lines = 0, songsShown = 0, gamesShown = 0;
            foreach (var (game, songs) in pending)
            {
                if (lines >= maxLines) break;
                text.AppendLine(game);
                lines++;
                gamesShown++;
                foreach (var song in songs)
                {
                    if (lines >= maxLines) break;
                    text.AppendLine("    " + Path.GetFileName(song));
                    lines++;
                    songsShown++;
                }
            }
            int songsLeft = pending.Sum(g => g.Songs.Count) - songsShown;
            if (songsLeft > 0)
                text.AppendLine($"…and {songsLeft} more song{(songsLeft == 1 ? "" : "s")}" +
                    (pending.Count > gamesShown ? $" in {pending.Count - gamesShown} more game{(pending.Count - gamesShown == 1 ? "" : "s")}" : ""));
            return text.ToString().TrimEnd();
        }
    }
}
