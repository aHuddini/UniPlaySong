using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UniPlaySong.Common
{
    /// <summary>
    /// String utility functions for cleaning and processing game/music names
    /// </summary>
    public static class StringHelper
    {
        /// <summary>
        /// Case-insensitive Contains check (for .NET Framework compatibility)
        /// </summary>
        public static bool ContainsIgnoreCase(this string source, string value)
        {
            if (source == null || value == null)
                return false;
            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        
        /// <summary>
        /// Normalizes a string for comparison purposes (removes special chars, extra spaces, lowercases)
        /// </summary>
        public static string NormalizeForComparison(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;
                
            // Remove special characters, keeping only alphanumeric and spaces
            var normalized = Regex.Replace(input, @"[^\w\s]", " ");
            // Collapse multiple spaces into one
            normalized = Regex.Replace(normalized, @"\s+", " ");
            // Trim and lowercase
            return normalized.Trim().ToLowerInvariant();
        }
        
        /// <summary>
        /// Strips common suffixes from game names (e.g., "(2020)", "- Definitive Edition")
        /// </summary>
        public static string StripGameNameSuffixes(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;
                
            var result = input;
            
            // Remove year in parentheses: "Game Name (2020)" or "Game Name [2020]"
            result = Regex.Replace(result, @"\s*[\(\[]\d{4}[\)\]]\s*$", "");
            
            // Remove platform indicators in parentheses: "Game Name (PC)", "(PS4)", etc.
            result = Regex.Replace(result, @"\s*\((PC|PS[0-9]?|Xbox[^)]*|Switch|Steam|GOG|Epic)\)\s*$", "", RegexOptions.IgnoreCase);
            
            // Remove common edition suffixes (case-insensitive)
            // Include both "- Edition" and " Edition" variants
            var suffixes = new[]
            {
                // Edition variants (with dash)
                " - Definitive Edition",
                " - Complete Edition",
                " - Game of the Year Edition",
                " - GOTY Edition",
                " - Enhanced Edition",
                " - Special Edition",
                " - Collector's Edition",
                " - Ultimate Edition",
                " - Deluxe Edition",
                " - Premium Edition",
                " - Gold Edition",
                " - Anniversary Edition",
                " - Director's Cut",
                " - Final Cut",
                // Edition variants (without dash) - e.g., "CONTROL ULTIMATE EDITION"
                " Definitive Edition",
                " Complete Edition",
                " Game of the Year Edition",
                " GOTY Edition",
                " Enhanced Edition",
                " Special Edition",
                " Collector's Edition",
                " Ultimate Edition",
                " Deluxe Edition",
                " Premium Edition",
                " Gold Edition",
                " Anniversary Edition",
                " Director's Cut",
                " Final Cut",
                // Remaster variants
                " - Remastered",
                " - HD Remaster",
                " - HD Edition",
                " Remastered",
                " HD Remaster",
                " Remake",
                " Remaster",
                // Other common suffixes
                " - Original",
                " - Classic",
                " - Trilogy",
                " - Collection",
                " - Bundle",
                // Platform-specific cuts/versions
                " - GOG Cut",
                " GOG Cut"
            };
            
            // Apply suffix removal (may need multiple passes)
            bool changed;
            do
            {
                changed = false;
                foreach (var suffix in suffixes)
                {
                    if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        result = result.Substring(0, result.Length - suffix.Length);
                        changed = true;
                        break;
                    }
                }
            } while (changed);
            
            // Remove trailing special characters
            result = Regex.Replace(result, @"[\s\-:]+$", "");
            
            return result.Trim();
        }
        
        /// <summary>
        /// Prepares a game name for searching (strips suffixes, normalizes, handles special cases)
        /// </summary>
        public static string PrepareForSearch(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return string.Empty;
            
            var result = gameName;
            
            // First strip common suffixes
            result = StripGameNameSuffixes(result);
            
            // Handle numbered sequels - keep the number but normalize format
            // "Game Name 2" stays as is, "Game Name II" stays as is
            
            // Remove trademark/copyright symbols
            result = result.Replace("™", "").Replace("®", "").Replace("©", "");
            
            // Normalize dashes and colons to spaces for better search matching
            result = Regex.Replace(result, @"[\-–—:]", " ");
            
            // Collapse multiple spaces
            result = Regex.Replace(result, @"\s+", " ");
            
            // Remove leading/trailing articles for better matching (optional)
            // Keep "The" at the start as it's often part of the official name

            return result.Trim();
        }

        /// <summary>
        /// Comprehensive search query normalization for both KHInsider and YouTube.
        /// Removes trademark symbols, normalizes Unicode, strips suffixes, and cleans formatting.
        /// This is the main entry point for all search query normalization.
        /// </summary>
        public static string NormalizeSearchQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            var result = query;

            // 1. Remove trademark/copyright/registered symbols (Issue #48)
            result = result
                .Replace("™", "")
                .Replace("®", "")
                .Replace("©", "")
                .Replace("(TM)", "")
                .Replace("(R)", "")
                .Replace("(C)", "");

            // 2. Normalize curly quotes and apostrophes to ASCII equivalents
            result = result
                .Replace("\u2019", "'")  // Right single quote '
                .Replace("\u2018", "'")  // Left single quote '
                .Replace("\u201C", "\"") // Left double quote "
                .Replace("\u201D", "\"") // Right double quote "
                .Replace("\u2026", "..."); // Ellipsis …

            // 3. Strip edition suffixes (Definitive, GOTY, Remastered, etc.)
            result = StripGameNameSuffixes(result);

            // 4. Normalize dashes/colons to spaces for better search matching
            result = Regex.Replace(result, @"[:–—\-]+", " ");

            // 5. Remove remaining non-alphanumeric except spaces (but keep numbers)
            result = Regex.Replace(result, @"[^\w\s]", " ");

            // 6. Collapse multiple spaces
            result = Regex.Replace(result, @"\s+", " ");

            return result.Trim();
        }

        /// <summary>
        /// Generates multiple search query variations for fallback searching.
        /// Returns queries in order of specificity (most specific first).
        /// </summary>
        public static List<string> GenerateSearchQueries(string gameName, string suffix = "OST")
        {
            var queries = new List<string>();
            if (string.IsNullOrWhiteSpace(gameName))
                return queries;

            // Tier 1: Full normalized name + suffix
            var normalized = NormalizeSearchQuery(gameName);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                queries.Add($"{normalized} {suffix}");
            }

            // Tier 2: Base name only (without numbers/editions) + suffix
            var baseName = ExtractBaseGameName(gameName);
            if (!string.IsNullOrWhiteSpace(baseName) &&
                !baseName.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                queries.Add($"{baseName} {suffix}");
            }

            // Tier 3: Simplified - first significant words only (for very problematic names)
            var words = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
            {
                // Take first 2-3 significant words
                var simplified = string.Join(" ", words.Take(Math.Min(3, words.Length)));
                if (!queries.Any(q => q.StartsWith(simplified, StringComparison.OrdinalIgnoreCase)))
                {
                    queries.Add($"{simplified} game {suffix}");
                }
            }

            return queries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Strips HTML tags and special characters from a string
        /// </summary>
        public static string StripHtml(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            // Remove HTML tags
            var stripped = Regex.Replace(input, "<.*?>", string.Empty);
            // Decode HTML entities (basic)
            stripped = stripped.Replace("&amp;", "&")
                             .Replace("&lt;", "<")
                             .Replace("&gt;", ">")
                             .Replace("&quot;", "\"")
                             .Replace("&#39;", "'")
                             .Replace("&nbsp;", " ");
            
            return stripped.Trim();
        }

        /// <summary>
        /// Cleans a string for use in file paths (removes invalid characters)
        /// </summary>
        public static string CleanForPath(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            var cleaned = input;
            foreach (var c in invalidChars)
            {
                cleaned = cleaned.Replace(c, '_');
            }
            return cleaned.Trim();
        }

        /// <summary>
        /// Extracts the base/core game name for broad searching.
        /// "The Elder Scrolls IV: Oblivion - Game of the Year Edition" -> "Elder Scrolls 4 Oblivion"
        /// "Mafia III: Definitive Edition" -> "Mafia 3"
        /// "Tomb Raider: Anniversary" -> "Tomb Raider Anniversary"
        /// </summary>
        public static string ExtractBaseGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return string.Empty;

            var result = gameName;

            // Strip edition suffixes first
            result = StripGameNameSuffixes(result);

            // Remove colons, dashes, and normalize
            result = Regex.Replace(result, @"[:–—\-]+", " ");
            result = Regex.Replace(result, @"\s+", " ").Trim();

            // Remove common prefixes that don't help with searching
            if (result.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(4);
            }

            // Convert Roman numerals to Arabic for better YouTube search matching
            // YouTube playlists often use "Mafia 3" instead of "Mafia III"
            result = ConvertRomanNumeralsToArabic(result);

            return result.Trim();
        }

        /// <summary>
        /// Converts Roman numerals in a string to Arabic numerals.
        /// "Mafia III" -> "Mafia 3", "Final Fantasy VII" -> "Final Fantasy 7"
        /// </summary>
        public static string ConvertRomanNumeralsToArabic(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            // Roman numeral mappings (order matters - check longer patterns first)
            var romanToArabic = new (string roman, string arabic)[]
            {
                ("XX", "20"), ("XIX", "19"), ("XVIII", "18"), ("XVII", "17"), ("XVI", "16"),
                ("XV", "15"), ("XIV", "14"), ("XIII", "13"), ("XII", "12"), ("XI", "11"),
                ("X", "10"), ("IX", "9"), ("VIII", "8"), ("VII", "7"), ("VI", "6"),
                ("V", "5"), ("IV", "4"), ("III", "3"), ("II", "2"), ("I", "1")
            };

            var result = input;

            foreach (var (roman, arabic) in romanToArabic)
            {
                // Match Roman numeral as a whole word (surrounded by word boundaries or spaces)
                // This prevents matching "I" in words like "Infinity" or "VI" in "Civilization"
                var pattern = $@"\b{roman}\b";
                result = Regex.Replace(result, pattern, arabic, RegexOptions.IgnoreCase);
            }

            return result;
        }

        /// <summary>
        /// Parses a time span from a string (e.g., "3:45" -> TimeSpan)
        /// </summary>
        public static TimeSpan? ParseTimeSpan(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            // Try common formats: "3:45", "1:23:45"
            var formats = new[] { @"m\:s", @"h\:m\:s", @"mm\:ss", @"hh\:mm\:ss" };
            foreach (var format in formats)
            {
                if (TimeSpan.TryParseExact(input, format, null, out var result))
                {
                    return result;
                }
            }

            // Try standard parsing
            if (TimeSpan.TryParse(input, out var standardResult))
            {
                return standardResult;
            }

            return null;
        }
    }
}

