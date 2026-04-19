using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Audio;
using UniPlaySong.Common;
using UniPlaySong.Models;
using UniPlaySong.Services;
using UniPlaySong.ViewModels;

namespace UniPlaySong.Handlers
{
    // Orchestrates the NSF Track Manager dialog for a single game.
    // Finds .nsf files in the game's music folder, opens the splitter UI,
    // pauses main UPS playback while the dialog is open, and invalidates
    // the song cache on commit so the new mini-NSFs are picked up.
    public class NsfTrackManagerHandler
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "NsfTrackManager";

        private readonly IPlayniteAPI _playniteApi;
        private readonly GameMusicFileService _fileService;
        private readonly IMusicPlaybackService _playbackService;

        public NsfTrackManagerHandler(
            IPlayniteAPI playniteApi,
            GameMusicFileService fileService,
            IMusicPlaybackService playbackService)
        {
            _playniteApi = playniteApi;
            _fileService = fileService;
            _playbackService = playbackService;
        }

        public void ShowForGame(Game game)
        {
            try
            {
                Logger.DebugIf(LogPrefix, $"ShowForGame called for: {game?.Name}");

                if (game == null)
                {
                    _playniteApi.Dialogs.ShowMessage("No game selected.", "NSF Track Manager");
                    return;
                }

                var gameFolder = _fileService?.GetGameMusicDirectory(game);
                var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                var nsfs = songs
                    .Where(s => s.EndsWith(".nsf", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (nsfs.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage(
                        $"No .nsf files found for '{game.Name}'.",
                        "NSF Track Manager");
                    return;
                }

                // Classify each NSF. Splittable masters have total_songs > 1 AND != 255.
                // The 255 value is a recovery sentinel applied to files where the original
                // total_songs got lost during earlier buggy splits — practically these are
                // minis, not splittable. All .nsf files (regardless of total_songs) are
                // candidates for Edit Loops, since loop overrides are file-agnostic.
                var splittableMasters = new List<string>();
                foreach (var path in nsfs)
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(path);
                        int totalSongs = NsfHeaderPatcher.ReadTotalSongs(bytes);
                        if (totalSongs > 1 && totalSongs != 255)
                            splittableMasters.Add(path);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Could not classify NSF '" + path + "': " + ex.Message);
                    }
                }

                // Edit Loops uses ALL .nsf files in the folder — no classification needed.
                var loopEditable = nsfs;

                // Auto-pick the first splittable master (if any). User rarely has multiple
                // masters in one folder; in that case the first-in-list is a sensible default.
                // A future in-dialog "Master file:" dropdown could expose switching.
                string pickedMaster = splittableMasters.FirstOrDefault();

                Logger.DebugIf(LogPrefix,
                    $"Opening dialog: master={pickedMaster ?? "<none>"}, loopEditable={loopEditable.Count}, splittableMasters={splittableMasters.Count}");

                _playbackService?.Stop();
                _playbackService?.AddPauseSource(PauseSource.NsfPreview);

                NsfTrackManagerViewModel vm = null;
                try
                {
                    vm = new NsfTrackManagerViewModel(pickedMaster, loopEditable, gameFolder, game.Name ?? "Unknown");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to load NSF");
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"Could not load NSF file:\n{ex.Message}",
                        "NSF Track Manager");
                    return;
                }

                var dialog = new Views.NsfTrackManagerDialog();
                dialog.Initialize(vm);

                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    $"NSF Track Manager - {game.Name}",
                    dialog,
                    width: 720,
                    height: 520);

                bool committed = false;
                vm.CloseRequested = (ok) =>
                {
                    committed = ok;
                    window.Close();
                };

                window.Closing += (s, e) =>
                {
                    Logger.DebugIf(LogPrefix, "Dialog closing, disposing VM");
                    vm.Dispose();
                };

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "nsf track manager close");

                window.ShowDialog();

                if (committed)
                {
                    _fileService?.InvalidateCacheForGame(game);
                    _playniteApi.Notifications.Add(new NotificationMessage(
                        "ups-nsf-split-" + game.Id,
                        $"NSF Manager: changes saved for {game.Name}",
                        NotificationType.Info));
                    Logger.DebugIf(LogPrefix, "Commit path: song cache invalidated");

                    // Start playing the updated game music so the user hears results
                    // (especially relevant after loop-save: GmeReader reads the fresh manifest).
                    _playbackService?.PlayGameMusic(game);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in NsfTrackManagerHandler.ShowForGame");
                _playniteApi.Dialogs.ShowErrorMessage(
                    $"NSF Track Manager failed:\n{ex.Message}",
                    "UniPlaySong");
            }
            finally
            {
                _playbackService?.RemovePauseSource(PauseSource.NsfPreview);
            }
        }
    }
}
