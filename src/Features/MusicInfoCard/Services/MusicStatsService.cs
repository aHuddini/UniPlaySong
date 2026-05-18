using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Audio;
using UniPlaySong.Common;
using UniPlaySong.Features.MusicInfoCard.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Features.MusicInfoCard.Services
{
    // Default IMusicStatsProvider implementation. Reads file metadata via
    // TagLib for standard audio formats and via GmeReader / HesM3uParser
    // for chiptune formats. Aggregates everything into a MusicStats DTO
    // on a background thread.
    //
    // Module-local. No outside-of-feature consumers of this class — the
    // handler depends on the IMusicStatsProvider interface.
    public class MusicStatsService : IMusicStatsProvider
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Extensions that the GME backend handles. These have to be probed
        // via GmeReader (not TagLib) for duration. Matches the chiptune
        // entries in Constants.SupportedAudioExtensionsLowercase but is
        // duplicated here so the module stays self-contained — changing
        // the global list won't silently break stats.
        private static readonly HashSet<string> ChiptuneExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".vgm", ".vgz", ".spc", ".nsf", ".nsfe", ".gbs", ".gym", ".hes", ".kss", ".sap", ".ay"
        };

        private readonly GameMusicFileService _fileService;

        public MusicStatsService(GameMusicFileService fileService)
        {
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        }

        public Task<MusicStats> ComputeAsync(Game game, CancellationToken cancellationToken)
        {
            return Task.Run(() => ComputeInternal(game, cancellationToken), cancellationToken);
        }

        private MusicStats ComputeInternal(Game game, CancellationToken ct)
        {
            var stats = new MusicStats { GameName = game?.Name ?? "Unknown Game" };
            if (game == null) return stats;

            var files = _fileService.GetAvailableSongs(game);
            if (files == null || files.Count == 0) return stats;

            stats.FileCount = files.Count;

            long bitrateSum = 0;
            int bitrateCount = 0;
            (string Title, TimeSpan Duration)? longest = null;
            (string Title, TimeSpan Duration)? shortest = null;

            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    // File size and format breakdown are universal.
                    var fileInfo = new FileInfo(filePath);
                    stats.TotalSizeBytes += fileInfo.Length;

                    var ext = (Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant();
                    if (stats.FormatBreakdown.ContainsKey(ext))
                        stats.FormatBreakdown[ext]++;
                    else
                        stats.FormatBreakdown[ext] = 1;

                    // Branch by format. The three branches below each set
                    // their contribution to TotalDuration and to the
                    // longest/shortest comparison. None modifies anything
                    // outside this scope so failures don't pollute totals.
                    if (ChiptuneExtensions.Contains(ext))
                    {
                        ProcessChiptuneFile(filePath, ext, stats, ref longest, ref shortest);
                    }
                    else
                    {
                        ProcessStandardAudioFile(filePath, stats, ref bitrateSum, ref bitrateCount, ref longest, ref shortest);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // honor cancellation
                }
                catch (Exception ex)
                {
                    stats.UnreadableCount++;
                    Logger.Debug($"MusicStatsService: failed to read {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            if (bitrateCount > 0)
                stats.AverageBitrateKbps = (int)(bitrateSum / bitrateCount);

            stats.LongestTrack = longest;
            stats.ShortestTrack = shortest;

            // Alphabetical sort by title (case-insensitive). Done at the end
            // rather than during the per-file pass so the comparison cost is
            // a single O(n log n) on the final list instead of an O(n log n)
            // insertion per file.
            stats.Songs = stats.Songs
                .OrderBy(s => s.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return stats;
        }

        private void ProcessStandardAudioFile(
            string filePath,
            MusicStats stats,
            ref long bitrateSum,
            ref int bitrateCount,
            ref (string Title, TimeSpan Duration)? longest,
            ref (string Title, TimeSpan Duration)? shortest)
        {
            var fileSize = new FileInfo(filePath).Length;
            var ext = (Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant();

            using (var tagFile = TagLib.File.Create(filePath))
            {
                var duration = tagFile.Properties?.Duration ?? TimeSpan.Zero;
                int bitrate = tagFile.Properties?.AudioBitrate ?? 0;

                stats.TotalDuration += duration;

                if (bitrate > 0)
                {
                    bitrateSum += bitrate;
                    bitrateCount++;
                }

                // Use the TagLib title if present, otherwise the filename
                // without extension. Game-folder files frequently lack
                // proper tags, so this fallback is the common case.
                var title = !string.IsNullOrWhiteSpace(tagFile.Tag?.Title)
                    ? tagFile.Tag.Title
                    : Path.GetFileNameWithoutExtension(filePath);

                CompareTrack(title, duration, ref longest, ref shortest);

                stats.Songs.Add(new SongEntry
                {
                    Title = title,
                    Extension = ext,
                    Duration = duration,
                    FileSizeBytes = fileSize,
                    IsPlaylistTrack = false
                });
            }
        }

        private void ProcessChiptuneFile(
            string filePath,
            string ext,
            MusicStats stats,
            ref (string Title, TimeSpan Duration)? longest,
            ref (string Title, TimeSpan Duration)? shortest)
        {
            var fileSize = new FileInfo(filePath).Length;

            // HES is the only chiptune format that ships M3U sidecars in
            // the project today, but the parser key is the ".hes" extension
            // — other GME formats fall through to the GmeReader probe below.
            if (ext.Equals(".hes", StringComparison.OrdinalIgnoreCase))
            {
                var tracks = HesM3uParser.LoadFor(filePath);
                if (tracks != null && tracks.Count > 0)
                {
                    // Playlist-aware: each M3U track counts separately for
                    // playlist counts, duration aggregation, and longest/
                    // shortest. The HES file itself still counts as one
                    // FileCount entry (already incremented in caller).
                    stats.PlaylistFileCount++;
                    stats.PlaylistTrackCount += tracks.Count;

                    var baseName = Path.GetFileNameWithoutExtension(filePath);
                    foreach (var track in tracks)
                    {
                        // M3U DurationMs is nullable — missing mm:ss field.
                        // We sum a default of 0 for missing durations so
                        // they don't poison the longest/shortest comparison
                        // (CompareTrack skips zero durations for shortest).
                        var durMs = track.DurationMs ?? 0;
                        var duration = TimeSpan.FromMilliseconds(durMs);
                        stats.TotalDuration += duration;

                        var trackTitle = !string.IsNullOrWhiteSpace(track.Title)
                            ? $"{baseName} — {track.Title}"
                            : $"{baseName} — Track {track.TrackIndex:X2}";

                        if (duration > TimeSpan.Zero)
                            CompareTrack(trackTitle, duration, ref longest, ref shortest);

                        // Each M3U track is one song-list row. FileSize is
                        // the parent .hes size repeated for every track;
                        // the view can divide if it wants a per-track est.
                        stats.Songs.Add(new SongEntry
                        {
                            Title = trackTitle,
                            Extension = ext,
                            Duration = duration,
                            FileSizeBytes = fileSize,
                            IsPlaylistTrack = true
                        });
                    }
                    return;
                }
            }

            // No sidecar (or non-HES chiptune): probe duration via GmeReader.
            // Single-track sniff — we only need TotalTime. GmeReader is a
            // WaveStream so we Dispose to release the GME handle promptly.
            using (var reader = new GmeReader(filePath))
            {
                var duration = reader.TotalTime;
                stats.TotalDuration += duration;

                var title = Path.GetFileNameWithoutExtension(filePath);
                CompareTrack(title, duration, ref longest, ref shortest);

                stats.Songs.Add(new SongEntry
                {
                    Title = title,
                    Extension = ext,
                    Duration = duration,
                    FileSizeBytes = fileSize,
                    IsPlaylistTrack = false
                });
            }
        }

        // Updates longest / shortest if this track beats the current record.
        // Zero-duration tracks are excluded from the shortest comparison
        // (they're typically parse failures, not genuinely-tiny songs).
        private static void CompareTrack(
            string title,
            TimeSpan duration,
            ref (string Title, TimeSpan Duration)? longest,
            ref (string Title, TimeSpan Duration)? shortest)
        {
            if (!longest.HasValue || duration > longest.Value.Duration)
                longest = (title, duration);

            if (duration > TimeSpan.Zero &&
                (!shortest.HasValue || duration < shortest.Value.Duration))
                shortest = (title, duration);
        }
    }
}
