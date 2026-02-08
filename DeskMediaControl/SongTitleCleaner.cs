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

        /// <summary>
        /// Parses a filename to extract artist/game and title components.
        /// Looks for patterns like "Artist - Title" or "Game Soundtrack - Title".
        /// </summary>
        /// <param name="filePath">Full path or filename of the song</param>
        /// <param name="title">Extracted song title</param>
        /// <param name="artist">Extracted artist/game name (null if not found)</param>
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

        /// <summary>
        /// Cleans a song filename to produce a user-friendly display title.
        /// </summary>
        /// <param name="filePath">Full path or filename of the song</param>
        /// <returns>Cleaned song title</returns>
        public static string CleanTitle(string filePath)
        {
            ParseFilename(filePath, out string title, out string artist);
            return title;
        }

        /// <summary>
        /// Cleans a filename and extracts the artist/game portion if present.
        /// </summary>
        /// <param name="filePath">Full path or filename of the song</param>
        /// <returns>Extracted artist/game name, or null if not found</returns>
        public static string ExtractArtistFromFilename(string filePath)
        {
            ParseFilename(filePath, out _, out string artist);
            return artist;
        }

        /// <summary>
        /// Formats a duration as m:ss or h:mm:ss.
        /// </summary>
        /// <param name="duration">Duration to format</param>
        /// <returns>Formatted duration string, or empty string if zero/negative</returns>
        public static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalSeconds <= 0)
                return string.Empty;

            return duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : duration.ToString(@"m\:ss");
        }

        /// <summary>
        /// Formats song info for display (title with optional artist and duration).
        /// </summary>
        /// <param name="title">Song title</param>
        /// <param name="artist">Artist name (can be null/empty)</param>
        /// <param name="duration">Song duration (can be null)</param>
        /// <returns>Formatted display string</returns>
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

            // Add duration if available (with pipe separator)
            if (duration.HasValue)
            {
                string durationStr = FormatDuration(duration.Value);
                if (!string.IsNullOrEmpty(durationStr))
                {
                    result = $"{result} | {durationStr}";
                }
            }

            return result;
        }
    }
}
