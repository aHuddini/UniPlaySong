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

                // Classify each NSF as master (total_songs > 1) or mini (total_songs == 1).
                var masters = new List<string>();
                var minis = new List<string>();
                foreach (var path in nsfs)
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(path);
                        int totalSongs = NsfHeaderPatcher.ReadTotalSongs(bytes);
                        if (totalSongs > 1) masters.Add(path);
                        else if (totalSongs == 1) minis.Add(path);
                        // totalSongs == 0 means invalid header; skip.
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Could not classify NSF '" + path + "': " + ex.Message);
                    }
                }

                if (masters.Count == 0 && minis.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage(
                        $"Found .nsf files for '{game.Name}', but none could be read as valid NSF.",
                        "NSF Track Manager");
                    return;
                }

                // Pick master NSF: direct if one, picker if multiple, null if none.
                string pickedMaster = null;
                if (masters.Count == 1)
                {
                    pickedMaster = masters[0];
                }
                else if (masters.Count > 1)
                {
                    var options = masters
                        .Select(p => new GenericItemOption(Path.GetFileName(p), p))
                        .ToList();
                    var choice = _playniteApi.Dialogs.ChooseItemWithSearch(
                        options,
                        (s) => options
                            .Where(o => o.Name.IndexOf(s ?? "", StringComparison.OrdinalIgnoreCase) >= 0)
                            .ToList<GenericItemOption>(),
                        defaultSearch: "",
                        caption: "Select master NSF to split");
                    if (choice == null && minis.Count == 0) return; // user cancelled and no minis to edit
                    if (choice != null) pickedMaster = choice.Description;
                    // If cancelled but minis exist, proceed with Edit-Loops-only dialog.
                }

                Logger.DebugIf(LogPrefix,
                    $"Opening dialog: master={pickedMaster ?? "<none>"}, minis={minis.Count}");

                _playbackService?.Stop();
                _playbackService?.AddPauseSource(PauseSource.NsfPreview);

                NsfTrackManagerViewModel vm = null;
                try
                {
                    vm = new NsfTrackManagerViewModel(pickedMaster, minis, gameFolder, game.Name ?? "Unknown");
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
