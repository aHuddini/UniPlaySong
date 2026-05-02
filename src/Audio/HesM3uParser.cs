using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace UniPlaySong.Audio
{
    // Parses the Game Music Emu extended-M3U sidecar format used to label
    // tracks inside a multi-track HES (PC Engine / TurboGrafx-16) file.
    //
    // The HES header itself only declares `first_track` — there is no
    // total-songs field. Tracks are scattered across the 0-255 index space.
    // Conventionally an .m3u sidecar with the same basename is shipped
    // alongside the .hes (Zophar's Domain, VGMRips, etc.) to enumerate
    // the real track indices and titles.
    //
    // M3U row format (one per track):
    //   <basename>.hes::HES,$<HEX_TRACK>,<title>,<mm:ss>,<loop_start>,<loop_count>
    //
    // Example (Bomberman TG-16):
    //   HC90036.hes::HES,$00,BGM #01,0:18,,1
    //   HC90036.hes::HES,$0E,BGM #10,0:16,,1
    //   HC90036.hes::HES,$19,Jingle #09,0:02,,1
    //
    // Lines starting with "#" are comments and ignored. Blank lines ignored.
    // Track index is parsed from the "$XX" hex notation (1-2 hex digits).
    public static class HesM3uParser
    {
        public const string SidecarExtension = ".m3u";

        // Returns the parsed track list for an .hes file by looking for a
        // sibling .m3u sidecar with the same basename. Returns an empty list
        // when no sidecar exists, the file is unreadable, or no rows parse.
        // Never throws — safe to call from the playback hot path.
        public static List<HesTrackEntry> LoadFor(string hesPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hesPath))
                    return new List<HesTrackEntry>();

                var directory = Path.GetDirectoryName(hesPath);
                if (string.IsNullOrEmpty(directory))
                    return new List<HesTrackEntry>();

                var sidecarPath = Path.Combine(
                    directory,
                    Path.GetFileNameWithoutExtension(hesPath) + SidecarExtension);

                if (!File.Exists(sidecarPath))
                    return new List<HesTrackEntry>();

                var lines = File.ReadAllLines(sidecarPath);
                return ParseLines(lines);
            }
            catch
            {
                return new List<HesTrackEntry>();
            }
        }

        // Pure parser — exposed for testing and reuse with already-loaded text.
        public static List<HesTrackEntry> ParseLines(IEnumerable<string> lines)
        {
            var result = new List<HesTrackEntry>();
            if (lines == null) return result;

            foreach (var rawLine in lines)
            {
                if (rawLine == null) continue;
                var line = rawLine.Trim();

                // Skip comments and blank lines (the leading metadata block uses # @TITLE etc).
                if (line.Length == 0 || line.StartsWith("#")) continue;

                // Find the "::HES," delimiter that separates the filename from the row data.
                // Anything that isn't a HES sidecar entry is silently skipped.
                const string delimiter = "::HES,";
                int delimIndex = line.IndexOf(delimiter, StringComparison.OrdinalIgnoreCase);
                if (delimIndex < 0) continue;

                var afterDelim = line.Substring(delimIndex + delimiter.Length);

                // Row body: $<hex>,<title>,<mm:ss>,<loop_start>,<loop_count>
                // Empty fields are valid (e.g. trailing ",," for loop_start).
                var fields = afterDelim.Split(new[] { ',' }, 5);
                if (fields.Length < 1) continue;

                int? trackIndex = ParseHexToken(fields[0]);
                if (!trackIndex.HasValue) continue;

                var title = fields.Length > 1 ? fields[1].Trim() : string.Empty;
                int? durationMs = fields.Length > 2 ? ParseDurationMs(fields[2]) : null;
                int loopCount = fields.Length > 4 ? ParseLoopCount(fields[4]) : 1;

                result.Add(new HesTrackEntry
                {
                    TrackIndex = trackIndex.Value,
                    Title = title,
                    DurationMs = durationMs,
                    LoopCount = loopCount,
                });
            }

            return result;
        }

        // "$0E" → 14, "$00" → 0, "$FF" → 255. Returns null on any parse error.
        // Tolerates surrounding whitespace and an optional 0x prefix as a fallback.
        private static int? ParseHexToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var token = raw.Trim();

            if (token.StartsWith("$")) token = token.Substring(1);
            else if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) token = token.Substring(2);

            if (token.Length == 0 || token.Length > 2) return null;

            if (int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
                && value >= 0 && value <= 255)
            {
                return value;
            }
            return null;
        }

        // "0:18" → 18000 ms, "2:33" → 153000 ms, "1:23:45" → 5025000 ms.
        // Returns null on parse failure (caller falls back to GME's metadata or default).
        private static int? ParseDurationMs(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var parts = raw.Trim().Split(':');
            try
            {
                int total = 0;
                foreach (var part in parts)
                {
                    if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return null;
                    total = total * 60 + n;
                }
                return total * 1000;
            }
            catch
            {
                return null;
            }
        }

        // Loop count: missing/blank → 1 (play once).
        private static int ParseLoopCount(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 1;
            if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0)
                return n;
            return 1;
        }
    }

    // One row from an HES extended-M3U sidecar.
    public class HesTrackEntry
    {
        // 0-255 track index passed to gme_start_track. Hex in the M3U ($0E),
        // stored as decimal here.
        public int TrackIndex { get; set; }

        // Human-readable title from the M3U (e.g. "BGM #10", "Jingle #03").
        public string Title { get; set; }

        // Duration parsed from the M3U mm:ss field, or null if missing.
        // GME's metadata is also unreliable for HES; the M3U is authoritative.
        public int? DurationMs { get; set; }

        // Loop count: typically 10 for music tracks, 1 for jingles. Not used
        // by the MVP playback path (UPS treats every track as play-once),
        // but preserved so a future HES Manager dialog can show it.
        public int LoopCount { get; set; }
    }
}
