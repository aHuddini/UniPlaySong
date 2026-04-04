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

    public UniPlaySongPlugin() : base()
    {
        XamlId = "UniPlaySong";
    }

    public override async Task InitializeAsync(InitializeArgs args)
    {
        await Task.CompletedTask;
        PlayniteApi = args.Api;
        Loc.Api = args.Api;

        _settingsHandler = new SettingsHandler(args.Api.UserDataDir);

        _fileService = new GameMusicFileService(args.Api.UserDataDir);
        _player = new NAudioMusicPlayer();
        _fader = new MusicFader(_player);
        _radioService = new RadioService(_fileService);
        _playbackService = new MusicPlaybackService(
            _player, _fader, _fileService, _radioService, _settingsHandler.Settings);
        _coordinator = new PlaybackCoordinator(_playbackService, _settingsHandler.Settings);

        _logger.Info("UniPlaySong P11 initialized");
    }

    public override async Task OnApplicationStartupAsync(OnApplicationStartupArgs args)
    {
        await Task.CompletedTask;
        _logger.Info("UniPlaySong P11 ready");
    }

    // P11 SDK uses OnGameSelectionChangedAsync with NewSelection (IReadOnlyList<Game>)
    public override async Task OnGameSelectionChangedAsync(OnGameSelectionChangedArgs args)
    {
        await Task.CompletedTask;
        var game = args.NewSelection?.FirstOrDefault();
        _coordinator?.HandleGameSelected(game);
    }

    public override async Task OnGameStartingAsync(OnGameStartingEventArgs args)
    {
        await Task.CompletedTask;
        _playbackService?.AddPauseSource(PauseSource.GameStarting);
    }

    public override async Task OnGameStoppedAsync(OnGameStoppedEventArgs args)
    {
        await Task.CompletedTask;
        _playbackService?.RemovePauseSource(PauseSource.GameStarting);
    }

    public override async Task OnApplicationShutdownAsync(OnApplicationShutdownArgs args)
    {
        await Task.CompletedTask;
        _playbackService?.FadeOutAndStop();
    }

    public override ICollection<MenuItemDescriptor>? GetAppMenuItemDescriptors(
        GetAppMenuItemDescriptorsArgs args)
    {
        return [new MenuItemDescriptor("ups.skip", "UniPlaySong: Skip to Next Song")];
    }

    // P11 MenuItemImpl ctor: (string name, Action invokeAction, bool? isChecked, UIIcon icon)
    public override ICollection<MenuItemImpl>? GetAppMenuItems(GetAppMenuItemsArgs args)
    {
        if (args.ItemId == "ups.skip")
        {
            return [new MenuItemImpl("Skip to Next Song", () =>
            {
                _playbackService?.SkipToNext();
            }, null, null!)];
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
        _fader?.Dispose();
        _fileService?.Dispose();

        if (_player != null)
            await _player.DisposeAsync();

        await base.DisposeAsync();
    }
}
