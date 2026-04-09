using System.Diagnostics;
using System.IO;
using Playnite;
using UniPlaySong.Audio;
using UniPlaySong.Models;
using UniPlaySong.Services;
using UniPlaySong.Settings;

namespace UniPlaySong;

public class UniPlaySongPlugin : Plugin
{
    public const string Id = "Huddini.UniPlaySong";
    public static IPlayniteApi? PlayniteApi { get; private set; }

    private static readonly ILogger _logger = LogManager.GetLogger();

    private SettingsHandler? _settingsHandler;
    private NAudioMusicPlayer? _player;
    private MusicFader? _fader;
    private GameMusicFileService? _fileService;
    private RadioService? _radioService;
    private MusicPlaybackService? _playbackService;
    private PlaybackCoordinator? _coordinator;
    private MediaControlService? _controller;

    public UniPlaySongPlugin() : base()
    {
        XamlId = "UniPlaySong";
    }

    public override async Task InitializeAsync(InitializeArgs args)
    {
        await Task.CompletedTask;
        _logger.Info("InitializeAsync: starting");
        PlayniteApi = args.Api;
        Loc.Api = args.Api;

        _logger.Info($"InitializeAsync: UserDataDir={args.Api.UserDataDir}");
        _settingsHandler = new SettingsHandler(args.Api.UserDataDir);
        _logger.Info($"InitializeAsync: settings loaded (EnableMusic={_settingsHandler.Settings.EnableMusic}, Volume={_settingsHandler.Settings.MusicVolume}, Radio={_settingsHandler.Settings.RadioModeEnabled})");

        _fileService = new GameMusicFileService(args.Api.UserDataDir);
        _logger.Info($"InitializeAsync: GameMusicFileService created (basePath={_fileService.BaseMusicPath})");

        _player = new NAudioMusicPlayer();
        _logger.Info("InitializeAsync: NAudioMusicPlayer created");

        _fader = new MusicFader(_player);
        _radioService = new RadioService(_fileService);
        _playbackService = new MusicPlaybackService(
            _player, _fader, _fileService, _radioService, _settingsHandler.Settings);
        _coordinator = new PlaybackCoordinator(_playbackService, _settingsHandler.Settings);
        _controller = new MediaControlService(_playbackService);

        _logger.Info("InitializeAsync: all services wired — plugin ready");
    }

    public override async Task OnApplicationStartupAsync(OnApplicationStartupArgs args)
    {
        await Task.CompletedTask;
        _logger.Info("OnApplicationStartupAsync: UI loaded, accepting game selections");
    }

    public override async Task OnGameSelectionChangedAsync(OnGameSelectionChangedArgs args)
    {
        await Task.CompletedTask;
        var game = args.NewSelection?.FirstOrDefault();
        _logger.Info($"OnGameSelectionChanged: {game?.Name ?? "(null)"} [id={game?.Id ?? "n/a"}]");
        _coordinator?.HandleGameSelected(game);
    }

    public override async Task OnGameStartingAsync(OnGameStartingEventArgs args)
    {
        await Task.CompletedTask;
        _logger.Info($"OnGameStarting: {args.Game?.Name ?? "(null)"}");
        _playbackService?.AddPauseSource(PauseSource.GameStarting);
    }

    public override async Task OnGameStoppedAsync(OnGameStoppedEventArgs args)
    {
        await Task.CompletedTask;
        _logger.Info($"OnGameStopped: {args.StoppedArgs?.SessionLength}");
        _playbackService?.RemovePauseSource(PauseSource.GameStarting);
    }

    public override async Task OnApplicationShutdownAsync(OnApplicationShutdownArgs args)
    {
        await Task.CompletedTask;
        _logger.Info("OnApplicationShutdown: stopping playback, saving settings");
        _playbackService?.FadeOutAndStop();
        _settingsHandler?.SaveIfNeeded();
    }

    // App menu
    public override ICollection<MenuItemDescriptor>? GetAppMenuItemDescriptors(
        GetAppMenuItemDescriptorsArgs args)
    {
        return
        [
            new MenuItemDescriptor("ups.playpause", "UniPlaySong: Play/Pause"),
            new MenuItemDescriptor("ups.skip", "UniPlaySong: Skip to Next Song"),
            new MenuItemDescriptor("ups.openroot", "UniPlaySong: Open Music Root Folder")
        ];
    }

    public override ICollection<MenuItemImpl>? GetAppMenuItems(GetAppMenuItemsArgs args)
    {
        if (args.ItemId == "ups.playpause")
        {
            var label = _controller?.IsPlaying == true ? "Pause Music" : "Resume Music";
            return [new MenuItemImpl(label, () => _controller?.TogglePlayPause())];
        }
        if (args.ItemId == "ups.skip")
        {
            return [new MenuItemImpl("Skip to Next Song", () => _controller?.SkipToNext())];
        }
        if (args.ItemId == "ups.openroot")
        {
            return [new MenuItemImpl("Open Music Root Folder", () =>
            {
                if (_fileService != null)
                {
                    Directory.CreateDirectory(_fileService.BaseMusicPath);
                    Process.Start(new ProcessStartInfo(_fileService.BaseMusicPath) { UseShellExecute = true });
                }
            })];
        }
        return null;
    }

    // Game right-click menu
    public override ICollection<MenuItemDescriptor> GetGameMenuItemDescriptors(
        GetGameMenuItemDescriptorsArgs args)
    {
        return
        [
            new MenuItemDescriptor("ups.gamefolder", "UniPlaySong: Open Music Folder"),
            new MenuItemDescriptor("ups.gameinfo", "UniPlaySong: Music Info")
        ];
    }

    public override ICollection<MenuItemImpl>? GetGameMenuItems(GetGameMenuItemsArgs args)
    {
        if (args.ItemId == "ups.gamefolder")
        {
            var items = new List<MenuItemImpl>();
            foreach (var game in args.Games)
            {
                var g = game;
                items.Add(new MenuItemImpl($"Open folder: {g.Name}", () =>
                {
                    if (_fileService == null) return;
                    var dir = _fileService.GetGameMusicDirectory(g);
                    Directory.CreateDirectory(dir);
                    Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                }));
            }
            return items;
        }
        if (args.ItemId == "ups.gameinfo")
        {
            var items = new List<MenuItemImpl>();
            foreach (var game in args.Games)
            {
                var g = game;
                var songs = _fileService?.GetAvailableSongs(g) ?? [];
                var label = songs.Length > 0
                    ? $"{g.Name}: {songs.Length} song(s)"
                    : $"{g.Name}: No music (click Open Music Folder to add)";
                items.Add(new MenuItemImpl(label, () => { }));
            }
            return items;
        }
        return null;
    }

    public override async Task<PluginSettingsHandler?> GetSettingsHandlerAsync(
        GetSettingsHandlerArgs args)
    {
        await Task.CompletedTask;
        return _settingsHandler;
    }

    public override async ValueTask DisposeAsync()
    {
        _controller?.Dispose();
        _fader?.Dispose();
        _fileService?.Dispose();

        if (_player != null)
            await _player.DisposeAsync();

        await base.DisposeAsync();
    }
}
