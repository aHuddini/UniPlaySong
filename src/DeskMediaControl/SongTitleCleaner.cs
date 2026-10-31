using System;
using System.IO;
using System.Text.RegularExpressions;

namespace UniPlaySong.DeskMediaControl
{
    // Utility for cleaning song titles from filenames (removes suffixes, replaces separators, etc.)
    public static class SongTitleCleaner
    {
        // Compiled regex for collapsing multiple whitespace characters
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        // Common suffixes to remove from entire filename (case-insensitive)
        private static readonly string[] SuffixesToRemove = new[]
        {
            "-normalized",
            "_normalized",
            "-trimmed",
            "_trimmed",
            "-processed",
            "_processed",
            "-amplified",
            "_amplified",
            "-louder",
            "_louder",
            "-edited",
            "_edited",
            "(normalized)",
            "(trimmed)",
            "(processed)",
            " normalized",
            " trimmed"
        };

        // Suffixes to strip from artist/game portion (case-insensitive)
        private static readonly string[] ArtistSuffixesToStrip = new[]
        {
            " soundtrack",
            " ost",
            " original soundtrack",
            " score",
            " music"
        };

        // Splits "Artist - Title" / "Game Soundtrack - Title" filenames; artist is null when there is no separator.
        public static void ParseFilename(string filePath, out string title, out string artist)
        {
            title = string.Empty;
            artist = null;

            if (string.IsNullOrWhiteSpace(filePath))
                return;

            // Get just the filename without extension
            string filename = Path.GetFileNameWithoutExtension(filePath);

            if (string.IsNullOrWhiteSpace(filename))
                return;

            // Remove common processing suffixes first
            foreach (var suffix in SuffixesToRemove)
            {
                int index = filename.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                {
                    filename = filename.Substring(0, index);
                }
            }

            // Look for " - " separator(s)
            // For "Game Soundtrack - Artist - Song Title", use LAST separator for title
            int lastSeparatorIndex = filename.LastIndexOf(" - ", StringComparison.Ordinal);
            if (lastSeparatorIndex > 0 && lastSeparatorIndex < filename.Length - 3)
            {
                // Found separator - split into artist/game and title
                string artistPart = filename.Substring(0, lastSeparatorIndex).Trim();
                string titlePart = filename.Substring(lastSeparatorIndex + 3).Trim();

                // Strip soundtrack-related suffixes from artist/game portion
                // Check for patterns like "Game Soundtrack - Artist" and extract just artist
                foreach (var suffix in ArtistSuffixesToStrip)
                {
                    // Look for "Game Soundtrack - Artist" pattern
                    int suffixIndex = artistPart.IndexOf(suffix + " - ", StringComparison.OrdinalIgnoreCase);
                    if (suffixIndex > 0)
                    {
                        // Extract just the artist part after "Soundtrack - "
                        artistPart = artistPart.Substring(suffixIndex + suffix.Length + 3).Trim();
                        break;
                    }
                    // Also check if it just ends with the suffix (no artist after)
                    if (artistPart.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        artistPart = artistPart.Substring(0, artistPart.Length - suffix.Length).Trim();
                        break;
                    }
                }

                // Clean up underscores in both parts
                artistPart = artistPart.Replace('_', ' ');
                titlePart = titlePart.Replace('_', ' ');

                // Collapse multiple spaces
                artistPart = WhitespaceRegex.Replace(artistPart, " ").Trim();
                titlePart = WhitespaceRegex.Replace(titlePart, " ").Trim();

                if (!string.IsNullOrWhiteSpace(artistPart))
                {
                    artist = artistPart;
                }
                title = !string.IsNullOrWhiteSpace(titlePart) ? titlePart : artistPart;
            }
            else
            {
                // No separator - treat entire filename as title
                title = filename.Replace('-', ' ').Replace('_', ' ');
                title = WhitespaceRegex.Replace(title, " ").Trim();
            }
        }

        public static string CleanTitle(string filePath)
        {
            ParseFilename(filePath, out string title, out string artist);
            return title;
        }

        // Returns null when the filename has no artist/game portion.
        public static string ExtractArtistFromFilename(string filePath)
        {
            ParseFilename(filePath, out _, out string artist);
            return artist;
        }

        // m:ss or h:mm:ss; empty for a zero or negative duration.
        public static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalSeconds <= 0)
                return string.Empty;

            return duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : duration.ToString(@"m\:ss");
        }

        // Title with optional artist and duration; artist and duration may be null.
        public static string FormatDisplayText(string title, string artist, TimeSpan? duration)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            string result = title;

            // Add artist if available
            if (!string.IsNullOrWhiteSpace(artist))
            {
                result = $"{title} - {artist}";
            }

            return result;
        }
    }
}
