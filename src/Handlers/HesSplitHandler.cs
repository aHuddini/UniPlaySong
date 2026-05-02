using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Audio;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Handlers
{
    // Single-action handler that splits a multi-track HES file (PC Engine /
    // TurboGrafx-16) into individual mini-HES files, one per track listed in
    // the file's M3U sidecar. Each mini-HES has its first_track header byte
    // patched to point at one specific song; UPS then sees N independent
    // .hes files in the music folder, each playing a single track when
    // loaded by GmeReader.
    //
    // No dialog: this is a fire-and-forget menu action. User confirmation
    // shows track count + preservation behavior; execution is sequential
    // and reports a summary message at the end. The original .hes is copied
    // to PreservedOriginals/<GameId>/ before any new files are written.
    public class HesSplitHandler
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "HesSplit";

        private readonly IPlayniteAPI _playniteApi;
        private readonly GameMusicFileService _fileService;
        private readonly IMusicPlaybackService _playbackService;

        public HesSplitHandler(
            IPlayniteAPI playniteApi,
            GameMusicFileService fileService,
            IMusicPlaybackService playbackService)
        {
            _playniteApi = playniteApi;
            _fileService = fileService;
            _playbackService = playbackService;
        }

        // Returns the list of (.hes, .m3u, tracks) candidates in the game's music
        // folder that have an M3U sidecar with at least 2 entries. Empty list when
        // there's nothing splittable. Used by the menu code to gate visibility.
        public List<HesSplitCandidate> FindSplittable(Game game)
        {
            var result = new List<HesSplitCandidate>();
            try
            {
                var directory = _fileService?.GetGameMusicDirectory(game);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    return result;

                foreach (var hesPath in Directory.GetFiles(directory, "*.hes", SearchOption.TopDirectoryOnly))
                {
                    var tracks = HesM3uParser.LoadFor(hesPath);
                    if (tracks != null && tracks.Count >= 2)
                    {
                        result.Add(new HesSplitCandidate { HesPath = hesPath, Tracks = tracks });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error scanning for splittable HES files");
            }
            return result;
        }

        public void RunForGame(Game game)
        {
            try
            {
                if (game == null)
                {
                    _playniteApi.Dialogs.ShowMessage("No game selected.", "Split HES Tracks");
                    return;
                }

                var candidates = FindSplittable(game);
                if (candidates.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage(
                        "No splittable HES files found. " +
                        "An .hes file with a sibling .m3u sidecar (containing 2+ tracks) is required.",
                        "Split HES Tracks");
                    return;
                }

                // Confirmation: total tracks across all candidate HES files
                int totalTracks = candidates.Sum(c => c.Tracks.Count);
                var fileLines = string.Join("\n",
                    candidates.Select(c => $"  • {Path.GetFileName(c.HesPath)} ({c.Tracks.Count} tracks)"));

                var confirmResult = _playniteApi.Dialogs.ShowMessage(
                    $"Found {candidates.Count} HES file(s) to split into {totalTracks} mini-HES files:\n\n{fileLines}\n\n" +
                    $"Each new mini-HES will play a single track from the original.\n" +
                    $"Originals will be preserved to PreservedOriginals\\{game.Id}\\.\n\n" +
                    $"Proceed?",
                    "Split HES Tracks",
                    System.Windows.MessageBoxButton.YesNo);

                if (confirmResult != System.Windows.MessageBoxResult.Yes)
                    return;

                int totalCreated = 0;
                int totalFailed = 0;
                var perFileSummary = new List<string>();

                foreach (var candidate in candidates)
                {
                    var fileResult = SplitOne(game, candidate);
                    totalCreated += fileResult.CreatedCount;
                    totalFailed += fileResult.FailedCount;
                    perFileSummary.Add(
                        $"  • {Path.GetFileName(candidate.HesPath)}: " +
                        $"{fileResult.CreatedCount} created" +
                        (fileResult.FailedCount > 0 ? $", {fileResult.FailedCount} failed" : ""));
                }

                _fileService?.InvalidateCacheForGame(game);

                // Restart playback so the user immediately hears the new mini files
                // in shuffle. Safe no-op when nothing was playing for this game.
                try { _playbackService?.PlayGameMusic(game); } catch { }

                var msg = $"Split complete:\n\n{string.Join("\n", perFileSummary)}\n\n" +
                          $"Total: {totalCreated} mini-HES file(s) created" +
                          (totalFailed > 0 ? $", {totalFailed} failed" : "") + ".";

                _playniteApi.Dialogs.ShowMessage(msg, "Split HES Tracks");
                Logger.Info($"HES split for '{game.Name}': created={totalCreated}, failed={totalFailed}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in HES split handler");
                _playniteApi.Dialogs.ShowErrorMessage(
                    $"HES split failed: {ex.Message}",
                    "Split HES Tracks");
            }
        }

        private SplitResult SplitOne(Game game, HesSplitCandidate candidate)
        {
            var result = new SplitResult();
            try
            {
                var sourceBytes = File.ReadAllBytes(candidate.HesPath);
                if (!HesHeaderPatcher.IsValidHesHeader(sourceBytes))
                {
                    Logger.Warn($"Skipping invalid HES file: {candidate.HesPath}");
                    result.FailedCount = candidate.Tracks.Count;
                    return result;
                }

                PreserveOriginal(game, candidate.HesPath);

                var directory = Path.GetDirectoryName(candidate.HesPath);
                var baseName = Path.GetFileNameWithoutExtension(candidate.HesPath);

                foreach (var track in candidate.Tracks)
                {
                    try
                    {
                        var patched = HesHeaderPatcher.PatchForTrack(sourceBytes, track.TrackIndex);
                        var safeTitle = SanitizeFilename(track.Title);
                        var miniName = $"{baseName} - {safeTitle}.hes";
                        var miniPath = Path.Combine(directory, miniName);

                        // Don't overwrite if the same mini-file already exists from a
                        // previous split run — caller can manually delete to redo.
                        if (File.Exists(miniPath))
                        {
                            Logger.DebugIf(LogPrefix, $"Skipping existing: {miniName}");
                            result.SkippedCount++;
                            continue;
                        }

                        File.WriteAllBytes(miniPath, patched);
                        result.CreatedCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to write mini-HES for track ${track.TrackIndex:X2}");
                        result.FailedCount++;
                    }
                }

                // Delete the original master HES so it doesn't keep playing as the
                // 27th song alongside the 26 mini-files. The preservation copy is
                // already safe in PreservedOriginals.
                try
                {
                    File.Delete(candidate.HesPath);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not delete original HES: {ex.Message}");
                }

                // Also delete the sidecar M3U — its track references point at the
                // (now-deleted) original filename, and the mini-files don't need it.
                try
                {
                    var sidecarPath = Path.Combine(directory, baseName + HesM3uParser.SidecarExtension);
                    if (File.Exists(sidecarPath))
                        File.Delete(sidecarPath);
                }
                catch { /* non-fatal */ }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"SplitOne failed for {candidate.HesPath}");
                result.FailedCount = candidate.Tracks.Count;
            }
            return result;
        }

        // Mirror of AudioAmplifyService.PreserveOriginalAsync's structure but
        // synchronous and scoped to a single HES file. Best-effort: any error
        // is logged but doesn't block the split itself.
        private void PreserveOriginal(Game game, string sourcePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(sourcePath);
                if (string.IsNullOrEmpty(directory)) return;

                // PreservedOriginals lives at <baseMusicPath>/PreservedOriginals/<GameId>/
                // Mirror the structure used by other preservation paths.
                var parentDir = Directory.GetParent(directory)?.Parent?.FullName;
                if (string.IsNullOrEmpty(parentDir)) return;

                var preservedDir = Path.Combine(
                    parentDir,
                    Constants.PreservedOriginalsFolderName,
                    game.Id.ToString());
                Directory.CreateDirectory(preservedDir);

                var preservedPath = Path.Combine(preservedDir, Path.GetFileName(sourcePath));
                if (!File.Exists(preservedPath))
                {
                    File.Copy(sourcePath, preservedPath, false);
                    Logger.DebugIf(LogPrefix, $"Preserved original to: {preservedPath}");
                }

                // Also preserve the sidecar M3U if present, so the user can fully
                // restore the original split-source state if they want to redo.
                var sidecarPath = Path.Combine(directory,
                    Path.GetFileNameWithoutExtension(sourcePath) + HesM3uParser.SidecarExtension);
                if (File.Exists(sidecarPath))
                {
                    var preservedSidecarPath = Path.Combine(preservedDir, Path.GetFileName(sidecarPath));
                    if (!File.Exists(preservedSidecarPath))
                        File.Copy(sidecarPath, preservedSidecarPath, false);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not preserve original {sourcePath}: {ex.Message}");
            }
        }

        // Strips characters Windows filesystems reject and trims runaway whitespace.
        // Empty input falls back to "Track" so we never produce a dotfile.
        private static string SanitizeFilename(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Track";
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new System.Text.StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                cleaned.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            var result = cleaned.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "Track" : result;
        }

        public class HesSplitCandidate
        {
            public string HesPath { get; set; }
            public List<HesTrackEntry> Tracks { get; set; }
        }

        private class SplitResult
        {
            public int CreatedCount;
            public int FailedCount;
            public int SkippedCount;
        }
    }
}
