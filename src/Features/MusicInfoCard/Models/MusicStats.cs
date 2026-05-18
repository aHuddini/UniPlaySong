using System;
using System.Collections.Generic;

namespace UniPlaySong.Features.MusicInfoCard.Models
{
    // Aggregate statistics computed for a single Game's music folder.
    //
    // Immutable record; populated once by MusicStatsService and consumed by
    // the dialog views. Optional fields are nullable so a partially-failed
    // aggregation (e.g. all chiptunes, no TagLib bitrate available) still
    // renders cleanly — the views check HasValue and hide the row.
    public sealed class MusicStats
    {
        // Game whose folder was scanned. Convenience for the views — saves
        // re-passing the Game object through the dialog binding.
        public string GameName { get; set; }

        // ===== File-level counts =====

        // Number of audio files discovered in the folder. Includes M3U-host
        // chiptune files (a single .hes counts as 1) — for the per-track
        // playlist expansion, see PlaylistTrackCount below.
        public int FileCount { get; set; }

        // Files that failed to parse (corrupt MP3, bad header, locked file).
        // Surfaced in the dialog as "N files unreadable" so missing tracks
        // aren't silently dropped from totals.
        public int UnreadableCount { get; set; }

        // ===== Playlist awareness =====

        // Files that have an M3U sidecar declaring multi-track playback
        // (HES is the only format that uses this in UPS today, but the
        // count is format-agnostic). 0 when no playlists are present.
        public int PlaylistFileCount { get; set; }

        // Total track count across all M3U sidecars. For "1 song = 1 file"
        // games this equals FileCount; for HES-heavy games it can exceed
        // FileCount substantially. Dialog shows both numbers when they
        // differ ("12 files containing 47 tracks").
        public int PlaylistTrackCount { get; set; }

        // ===== Duration =====

        // Sum of every file's playable duration. For M3U playlist files,
        // the sum of all sidecar track durations (not just the first track's).
        // TimeSpan.Zero when nothing parsed.
        public TimeSpan TotalDuration { get; set; }

        // Longest single playable unit. For non-playlist files this is the
        // file itself. For playlist files (multi-track HES), each TRACK
        // is considered separately — the longest is whichever track inside
        // any playlist had the longest M3U-declared duration.
        public (string Title, TimeSpan Duration)? LongestTrack { get; set; }

        // Shortest playable unit, same scoping as LongestTrack.
        // Excludes 0-duration entries (M3U rows with missing mm:ss field).
        public (string Title, TimeSpan Duration)? ShortestTrack { get; set; }

        // ===== Size =====

        // Sum of every file's on-disk size in bytes. Includes M3U sidecars
        // and any non-audio files in the folder, since GameMusicFileService
        // already filters to supported extensions.
        public long TotalSizeBytes { get; set; }

        // ===== Quality =====

        // Average bitrate across all TagLib-readable files (chiptune files
        // don't report bitrate in a TagLib-compatible way and are excluded
        // from this average). Null when no files contributed a bitrate.
        public int? AverageBitrateKbps { get; set; }

        // ===== Format breakdown =====

        // Map of file extension (".mp3", ".hes", etc., lowercase with the
        // leading dot) → count of files with that extension. Useful for
        // a small table in the dialog. Sorted by descending count by the
        // view layer, not here.
        public Dictionary<string, int> FormatBreakdown { get; set; } = new Dictionary<string, int>();

        // Per-song / per-track entries rendered as a scrollable list in the
        // dialog. Sorted alphabetically by Title (case-insensitive) by the
        // service. For HES files with M3U sidecars, each M3U track is its
        // own entry — so a 12-track .hes contributes 12 rows here, not 1.
        // For everything else, one entry per file.
        public List<SongEntry> Songs { get; set; } = new List<SongEntry>();
    }

    // One row in the song list panel. Composite key (FilePath + TrackIndex)
    // distinguishes M3U tracks within the same HES file; for non-playlist
    // files TrackIndex is null. Duration may be TimeSpan.Zero when the
    // source (M3U sidecar) didn't declare it.
    public sealed class SongEntry
    {
        // Human-readable title — TagLib tag, M3U track name, or filename
        // fallback (Path.GetFileNameWithoutExtension). Never null.
        public string Title { get; set; }

        // File extension lowercase, leading-dot included (".mp3", ".hes").
        // Used by the view to render a small format chip per row.
        public string Extension { get; set; }

        // Playable duration. TimeSpan.Zero when unknown (rare — only when
        // an M3U track is missing its mm:ss field). View hides duration
        // text on zero-duration rows.
        public TimeSpan Duration { get; set; }

        // On-disk size in bytes. For HES playlist tracks this is the
        // size of the parent .hes file, repeated across all its tracks
        // (each track lives inside one file). View can dedupe by file
        // path if it wants to show "physical bytes" instead.
        public long FileSizeBytes { get; set; }

        // True when this entry is one track inside a multi-track HES
        // playlist file. The view shows a small "track N" chip on these
        // rows so the user understands why several rows share a base name.
        public bool IsPlaylistTrack { get; set; }
    }
}
