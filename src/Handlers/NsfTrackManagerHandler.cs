using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
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

                string pickedPath;
                if (nsfs.Count == 1)
                {
                    pickedPath = nsfs[0];
                }
                else
                {
                    var options = nsfs
                        .Select(p => new GenericItemOption(Path.GetFileName(p), p))
                        .ToList();
                    var choice = _playniteApi.Dialogs.ChooseItemWithSearch(
                        options,
                        (s) => options
                            .Where(o => o.Name.IndexOf(s ?? "", StringComparison.OrdinalIgnoreCase) >= 0)
                            .ToList<GenericItemOption>(),
                        defaultSearch: "",
                        caption: "Select NSF file");
                    if (choice == null) return;
                    pickedPath = choice.Description;
                }

                Logger.DebugIf(LogPrefix, $"Opening dialog for NSF: {pickedPath}");

                _playbackService?.Stop();
                _playbackService?.AddPauseSource(PauseSource.NsfPreview);

                NsfTrackManagerViewModel vm = null;
                try
                {
                    vm = new NsfTrackManagerViewModel(pickedPath, game.Name ?? "Unknown");
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
                        $"NSF split complete for {game.Name}",
                        NotificationType.Info));
                    Logger.DebugIf(LogPrefix, "Commit path: song cache invalidated");

                    // Start playing the freshly-split game music so the user hears
                    // the result immediately instead of having to switch games.
                    // PlayGameMusic sweeps leaked NsfPreview itself before starting.
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
