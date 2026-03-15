using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.DeskMediaControl
{
    // ViewModel for the full-page Music Library dashboard.
    // Follows the same Func<> closure injection pattern as TopPanelMediaControlViewModel.
    public class MusicLibraryViewModel : ObservableObject
    {
        private readonly Func<IMusicPlaybackService> _getPlaybackService;
        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly Func<Game> _getCurrentGame;
        private readonly GameMusicFileService _fileService;
        private readonly Func<IPlayniteAPI> _getApi;
        private readonly Action<string> _log;
        private readonly IDashboardPlaybackService _dashboardService;

        private SongMetadataService _metadataService;
        private string _gamesPath;
        private DispatcherTimer _progressTimer;

        #region Now Playing State

        private string _songTitle = "No song playing";
        public string SongTitle
        {
            get => _songTitle;
            set { _songTitle = value; OnPropertyChanged(); }
        }

        private string _songArtist;
        public string SongArtist
        {
            get => _songArtist;
            set { _songArtist = value; OnPropertyChanged(); }
        }

        private string _songDuration;
        public string SongDuration
        {
            get => _songDuration;
            set { _songDuration = value; OnPropertyChanged(); }
        }

        private string _nowPlayingGameName;
        public string NowPlayingGameName
        {
            get => _nowPlayingGameName;
            set { _nowPlayingGameName = value; OnPropertyChanged(); }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon)); }
        }

        public string PlayPauseIcon => IsPlaying ? MediaControlIcons.Pause : MediaControlIcons.Play;

        private bool _canSkip;
        public bool CanSkip
        {
            get => _canSkip;
            set { _canSkip = value; OnPropertyChanged(); }
        }

        private ImageSource _coverArt;
        public ImageSource CoverArt
        {
            get => _coverArt;
            set { _coverArt = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCoverArt)); }
        }

        public bool HasCoverArt => _coverArt != null;

        private double _progressPercent;
        public double ProgressPercent
        {
            get => _progressPercent;
            set { _progressPercent = value; OnPropertyChanged(); }
        }

        private string _elapsedTime = "0:00";
        public string ElapsedTime
        {
            get => _elapsedTime;
            set { _elapsedTime = value; OnPropertyChanged(); }
        }

        private string _remainingTime = "0:00";
        public string RemainingTime
        {
            get => _remainingTime;
            set { _remainingTime = value; OnPropertyChanged(); }
        }

        private TimeSpan _currentDuration = TimeSpan.Zero;

        #endregion

        // Now Playing expanded view
        private bool _isNowPlayingExpanded;
        public bool IsNowPlayingExpanded
        {
            get => _isNowPlayingExpanded;
            set { _isNowPlayingExpanded = value; OnPropertyChanged(); }
        }

        public ICommand ToggleNowPlayingCommand { get; }

        #region Tab State

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { _selectedTabIndex = value; OnPropertyChanged(); OnTabChanged(); }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSearchText)); ApplySearchFilter(); }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(_searchText);

        #endregion

        #region Games Tab

        private ObservableCollection<GameCardItem> _games = new ObservableCollection<GameCardItem>();
        public ObservableCollection<GameCardItem> Games
        {
            get => _games;
            set { _games = value; OnPropertyChanged(); }
        }

        private ObservableCollection<GameCardItem> _filteredGames = new ObservableCollection<GameCardItem>();
        public ObservableCollection<GameCardItem> FilteredGames
        {
            get => _filteredGames;
            set { _filteredGames = value; OnPropertyChanged(); }
        }

        private GameCardItem _selectedGame;
        public GameCardItem SelectedGame
        {
            get => _selectedGame;
            set
            {
                _selectedGame = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsGameDetailVisible));
                OnPropertyChanged(nameof(IsGamesGridVisible));
                if (value != null) LoadGameDetail();
            }
        }

        public bool IsGameDetailVisible => _selectedGame != null;
        public bool IsGamesGridVisible => _selectedGame == null;

        #endregion

        #region Game Detail

        private ObservableCollection<SongListItem> _gameDetailSongs = new ObservableCollection<SongListItem>();
        public ObservableCollection<SongListItem> GameDetailSongs
        {
            get => _gameDetailSongs;
            set { _gameDetailSongs = value; OnPropertyChanged(); }
        }

        #endregion

        #region All Tracks Tab

        private ObservableCollection<SongListItem> _allTracks = new ObservableCollection<SongListItem>();
        public ObservableCollection<SongListItem> AllTracks
        {
            get => _allTracks;
            set { _allTracks = value; OnPropertyChanged(); }
        }

        #endregion

        #region Library Stats

        private int _gamesWithMusic;
        public int GamesWithMusic
        {
            get => _gamesWithMusic;
            set { _gamesWithMusic = value; OnPropertyChanged(); }
        }

        private int _totalSongs;
        public int TotalSongs
        {
            get => _totalSongs;
            set { _totalSongs = value; OnPropertyChanged(); }
        }

        private string _storageUsed = "—";
        public string StorageUsed
        {
            get => _storageUsed;
            set { _storageUsed = value; OnPropertyChanged(); }
        }

        private int _gamesWithoutMusic;
        public int GamesWithoutMusic
        {
            get => _gamesWithoutMusic;
            set { _gamesWithoutMusic = value; OnPropertyChanged(); }
        }

        private bool _statsLoaded;

        #endregion

        #region Commands

        public ICommand PlayPauseCommand { get; }
        public ICommand SkipCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand RefreshStatsCommand { get; }
        public ICommand BackToGamesCommand { get; }
        public ICommand PlayAllCommand { get; }
        public ICommand RadioModeCommand { get; }

        #endregion

        private bool _gamesLoaded;
        private bool _allTracksLoaded;

        public MusicLibraryViewModel(
            Func<IMusicPlaybackService> getPlaybackService,
            Func<UniPlaySongSettings> getSettings,
            Func<Game> getCurrentGame,
            GameMusicFileService fileService,
            Func<IPlayniteAPI> getApi,
            string gamesPath,
            IDashboardPlaybackService dashboardService,
            Action<string> log = null)
        {
            _getPlaybackService = getPlaybackService ?? throw new ArgumentNullException(nameof(getPlaybackService));
            _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
            _getCurrentGame = getCurrentGame ?? throw new ArgumentNullException(nameof(getCurrentGame));
            _fileService = fileService;
            _getApi = getApi;
            _gamesPath = gamesPath;
            _dashboardService = dashboardService;
            _log = log;

            PlayPauseCommand = new Common.RelayCommand(OnPlayPause);
            SkipCommand = new Common.RelayCommand(OnSkip, () => CanSkip);
            PreviousCommand = new Common.RelayCommand(OnPrevious);
            RefreshStatsCommand = new Common.RelayCommand(() => LoadLibraryStats());
            BackToGamesCommand = new Common.RelayCommand(OnBackToGames);
            PlayAllCommand = new Common.RelayCommand(OnPlayAll);
            RadioModeCommand = new Common.RelayCommand(OnRadioMode);
            ToggleNowPlayingCommand = new Common.RelayCommand(() => IsNowPlayingExpanded = !IsNowPlayingExpanded);

            _progressTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _progressTimer.Tick += OnProgressTimerTick;

            if (_dashboardService != null)
            {
                _dashboardService.OnPlaybackStateChanged += OnDashboardPlaybackStateChanged;
                _dashboardService.OnSongChanged += OnDashboardSongChanged;
                _dashboardService.OnSongEnded += () => OnDashboardPlaybackStateChanged();
            }

            try
            {
                var playbackService = _getPlaybackService?.Invoke();
                if (playbackService != null)
                {
                    SubscribeToEvents(playbackService);
                    InitializeMetadataService();
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"MusicLibrary: Error during init: {ex.Message}");
            }
        }

        #region Initialization and Events

        private void InitializeMetadataService()
        {
            var playbackService = _getPlaybackService?.Invoke();
            if (playbackService == null) return;

            _metadataService = new SongMetadataService(playbackService, null, _getSettings);
            _metadataService.OnSongInfoChanged += OnSongInfoChanged;

            if (!string.IsNullOrEmpty(playbackService.CurrentSongPath))
            {
                var songInfo = _metadataService.ReadMetadata(playbackService.CurrentSongPath);
                OnSongInfoChanged(songInfo);
            }
        }

        private void SubscribeToEvents(IMusicPlaybackService playbackService)
        {
            if (playbackService == null) return;

            playbackService.OnMusicStarted += _ =>
            {
                UpdatePlaybackState();
                MarkCurrentlyPlayingGame();
            };
            playbackService.OnMusicStopped += _ =>
            {
                UpdatePlaybackState();
                ResetProgress();
                MarkCurrentlyPlayingGame();
            };
            playbackService.OnPlaybackStateChanged += UpdatePlaybackState;
            playbackService.OnSongChanged += _ =>
            {
                MarkCurrentlyPlayingGame();
                MarkCurrentSongInDetail();
            };
            playbackService.OnSongCountChanged += () => UpdateSkipState();
        }

        public void ResubscribeToEvents(IMusicPlaybackService newPlaybackService)
        {
            SubscribeToEvents(newPlaybackService);
            _metadataService?.ResubscribeToService(newPlaybackService);
            UpdatePlaybackState();
        }

        #endregion

        #region Lifecycle

        public void OnDashboardOpened()
        {
            // Pause main player when dashboard opens
            var playbackService = _getPlaybackService?.Invoke();
            if (playbackService?.IsPlaying == true)
                playbackService.AddPauseSource(PauseSource.Dashboard);

            UpdatePlaybackState();
            UpdateCoverArt();
            LoadGameList();
            MarkCurrentlyPlayingGame();
            _progressTimer.Start();
        }

        public void OnDashboardClosed()
        {
            _progressTimer.Stop();
            // Stop dashboard playback so main player can resume
            if (_dashboardService?.IsActive == true)
                _dashboardService.Stop();
            // Also remove Dashboard pause source in case we paused main on open but never played
            _getPlaybackService?.Invoke()?.RemovePauseSource(PauseSource.Dashboard);
        }

        #endregion

        #region Now Playing Logic

        private void OnProgressTimerTick(object sender, EventArgs e)
        {
            try
            {
                TimeSpan? currentTime = null;
                TimeSpan duration = _currentDuration;
                bool isPlaying = false;

                if (_dashboardService?.IsActive == true)
                {
                    currentTime = _dashboardService.CurrentTime;
                    var totalTime = _dashboardService.TotalTime;
                    if (totalTime.HasValue) duration = totalTime.Value;
                    isPlaying = _dashboardService.IsPlaying;
                }
                else
                {
                    var playbackService = _getPlaybackService?.Invoke();
                    if (playbackService == null) return;
                    currentTime = playbackService.CurrentTime;
                    isPlaying = playbackService.IsPlaying && !playbackService.IsPaused;
                }

                if (!isPlaying || !currentTime.HasValue || duration.TotalSeconds <= 0)
                    return;

                double progress = Math.Min(1.0, Math.Max(0, currentTime.Value.TotalSeconds / duration.TotalSeconds));
                ProgressPercent = progress * 100;
                ElapsedTime = FormatTime(currentTime.Value);
                var remaining = duration - currentTime.Value;
                RemainingTime = remaining.TotalSeconds > 0 ? "-" + FormatTime(remaining) : "0:00";
            }
            catch { }
        }

        private void ResetProgress()
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    ProgressPercent = 0;
                    ElapsedTime = "0:00";
                    RemainingTime = "0:00";
                }));
            }
            catch { }
        }

        private static string FormatTime(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }

        private void OnSongInfoChanged(SongInfo songInfo)
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (songInfo == null || songInfo.IsEmpty)
                        {
                            SongTitle = "No song playing";
                            SongArtist = null;
                            SongDuration = null;
                            _currentDuration = TimeSpan.Zero;
                            ResetProgress();
                            return;
                        }

                        SongTitle = songInfo.Title;
                        SongArtist = songInfo.HasArtist ? songInfo.Artist : null;
                        SongDuration = songInfo.HasDuration ? songInfo.DurationText : null;
                        _currentDuration = songInfo.HasDuration ? songInfo.Duration : TimeSpan.Zero;

                        var playbackService = _getPlaybackService?.Invoke();
                        var game = playbackService?.CurrentGame ?? _getCurrentGame?.Invoke();
                        NowPlayingGameName = game?.Name;
                        UpdateCoverArt();
                        MarkCurrentSongInDetail();
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"MusicLibrary: Error in OnSongInfoChanged: {ex.Message}");
                    }
                }));
            }
            catch { }
        }

        private void UpdateCoverArt()
        {
            try
            {
                // Use playback service's current game (not Playnite selection)
                var playbackService = _getPlaybackService?.Invoke();
                var game = playbackService?.CurrentGame ?? _getCurrentGame?.Invoke();
                if (game == null) { CoverArt = null; return; }

                var api = _getApi?.Invoke();
                if (api == null) { CoverArt = null; return; }

                var imageId = game.CoverImage ?? game.BackgroundImage;
                if (string.IsNullOrEmpty(imageId)) { CoverArt = null; return; }

                var imagePath = api.Database.GetFullFilePath(imageId);
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) { CoverArt = null; return; }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 300;
                bitmap.EndInit();
                bitmap.Freeze();
                CoverArt = bitmap;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"MusicLibrary: Error loading cover art: {ex.Message}");
                CoverArt = null;
            }
        }

        private void UpdatePlaybackState()
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var playbackService = _getPlaybackService?.Invoke();
                        IsPlaying = playbackService?.IsPlaying == true && playbackService?.IsPaused != true;
                        UpdateSkipState();
                    }
                    catch { }
                }));
            }
            catch { }
        }

        private void UpdateSkipState()
        {
            var playbackService = _getPlaybackService?.Invoke();
            CanSkip = playbackService?.CurrentGameSongCount >= 2
                || playbackService?.IsPlayingPoolBasedDefault == true;
        }

        #endregion

        #region Transport Commands

        private void OnPlayPause()
        {
            try
            {
                // Dashboard active — toggle its pause state
                if (_dashboardService != null && _dashboardService.IsActive)
                {
                    if (_dashboardService.IsPaused)
                        _dashboardService.Resume();
                    else
                        _dashboardService.Pause();
                    return;
                }

                // Fallback: toggle main playback service
                var playbackService = _getPlaybackService?.Invoke();
                if (playbackService == null) return;

                if (playbackService.IsPaused)
                {
                    playbackService.RemovePauseSource(PauseSource.Idle);
                    playbackService.RemovePauseSource(PauseSource.ExternalAudio);
                    playbackService.RemovePauseSource(PauseSource.SystemLock);
                    playbackService.RemovePauseSource(PauseSource.Manual);
                }
                else if (playbackService.IsPlaying || playbackService.IsLoaded)
                {
                    playbackService.AddPauseSource(PauseSource.Manual);
                }
                else
                {
                    var currentGame = _getCurrentGame?.Invoke();
                    if (currentGame != null)
                        playbackService.PlayGameMusic(currentGame, _getSettings?.Invoke());
                }

                UpdatePlaybackState();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"MusicLibrary: Error toggling playback: {ex.Message}");
            }
        }

        private void OnSkip()
        {
            try
            {
                if (_dashboardService != null && _dashboardService.IsActive)
                {
                    _dashboardService.SkipNext();
                    return;
                }

                var playbackService = _getPlaybackService?.Invoke();
                playbackService?.SkipToNextSong();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"MusicLibrary: Error skipping song: {ex.Message}");
            }
        }

        private void OnPrevious()
        {
            try
            {
                if (_dashboardService != null && _dashboardService.IsActive)
                {
                    _dashboardService.SkipPrevious();
                    return;
                }

                var playbackService = _getPlaybackService?.Invoke();
                playbackService?.RestartCurrentSong();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"MusicLibrary: Error going previous: {ex.Message}");
            }
        }

        public void PlaySong(SongListItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.FilePath)) return;
            try
            {
                _dashboardService?.Play(item.FilePath);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"MusicLibrary: Error playing song: {ex.Message}");
            }
        }

        private void OnPlayAll()
        {
            if (_selectedGame == null) return;
            try
            {
                var files = Directory.GetFiles(_selectedGame.MusicDirectoryPath)
                    .Where(f => Constants.SupportedAudioExtensionsLowercase.Contains(
                        Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count > 0)
                    _dashboardService?.PlayList(files);
            }
            catch (Exception ex) { _log?.Invoke($"MusicLibrary: Error playing all: {ex.Message}"); }
        }

        public void PlayGameByCard(GameCardItem card)
        {
            if (card == null) return;
            try
            {
                var files = Directory.GetFiles(card.MusicDirectoryPath)
                    .Where(f => Constants.SupportedAudioExtensionsLowercase.Contains(
                        Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count > 0)
                    _dashboardService?.PlayList(files);
            }
            catch (Exception ex) { _log?.Invoke($"MusicLibrary: Error playing game: {ex.Message}"); }
        }

        private void OnRadioMode()
        {
            try
            {
                if (string.IsNullOrEmpty(_gamesPath) || !Directory.Exists(_gamesPath)) return;

                var supportedExts = Constants.SupportedAudioExtensionsLowercase;
                var songs = new List<string>();
                foreach (var dir in Directory.GetDirectories(_gamesPath))
                {
                    songs.AddRange(Directory.GetFiles(dir)
                        .Where(f => supportedExts.Contains(Path.GetExtension(f).ToLowerInvariant())));
                }

                if (songs.Count == 0) return;
                _dashboardService?.StartRadio(songs);
                _log?.Invoke($"MusicLibrary: Radio mode started with {songs.Count} songs");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"MusicLibrary: Error starting radio mode: {ex.Message}");
            }
        }

        private void OnBackToGames()
        {
            SelectedGame = null;
        }

        #endregion

        #region Dashboard Service Events

        private void OnDashboardPlaybackStateChanged()
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        IsPlaying = _dashboardService?.IsPlaying == true;
                        CanSkip = _dashboardService?.IsActive == true;
                        MarkCurrentlyPlayingGame();
                    }
                    catch { }
                }));
            }
            catch { }
        }

        private void OnDashboardSongChanged(string filePath)
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (string.IsNullOrEmpty(filePath))
                        {
                            SongTitle = "No song playing";
                            SongArtist = null;
                            return;
                        }

                        // Read metadata if available
                        var info = _metadataService?.ReadMetadata(filePath);
                        if (info != null && !info.IsEmpty)
                        {
                            SongTitle = info.Title;
                            SongArtist = info.HasArtist ? info.Artist : null;
                            _currentDuration = info.HasDuration ? info.Duration : TimeSpan.Zero;
                        }
                        else
                        {
                            SongTitleCleaner.ParseFilename(filePath, out string title, out string artist);
                            SongTitle = title ?? Path.GetFileNameWithoutExtension(filePath);
                            SongArtist = artist;
                            _currentDuration = TimeSpan.Zero;
                        }

                        // Find game from file path and load cover art
                        var dirPath = Path.GetDirectoryName(filePath);
                        var matchingGame = Games?.FirstOrDefault(g =>
                            string.Equals(g.MusicDirectoryPath, dirPath, StringComparison.OrdinalIgnoreCase));
                        NowPlayingGameName = matchingGame?.Name;

                        // Load actual game cover art (not the small icon)
                        try
                        {
                            if (matchingGame != null)
                            {
                                var api = _getApi?.Invoke();
                                var allGames = api?.Database?.Games;
                                Game game = null;
                                if (Guid.TryParse(matchingGame.GameId, out var gid))
                                    game = allGames?.FirstOrDefault(g => g.Id == gid);

                                if (game != null && api != null)
                                {
                                    var imageId = game.CoverImage ?? game.BackgroundImage ?? game.Icon;
                                    if (!string.IsNullOrEmpty(imageId))
                                    {
                                        var imagePath = api.Database.GetFullFilePath(imageId);
                                        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                                        {
                                            var bitmap = new BitmapImage();
                                            bitmap.BeginInit();
                                            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                            bitmap.DecodePixelWidth = 300;
                                            bitmap.EndInit();
                                            bitmap.Freeze();
                                            CoverArt = bitmap;
                                        }
                                    }
                                }
                            }
                        }
                        catch { }

                        MarkCurrentlyPlayingGame();
                        MarkCurrentSongInDetail();
                    }
                    catch { }
                }));
            }
            catch { }
        }

        #endregion

        #region Game List Loading

        public void LoadGameList()
        {
            if (_gamesLoaded) return;
            _gamesLoaded = true;

            Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(_gamesPath) || !Directory.Exists(_gamesPath)) return;

                    var api = _getApi?.Invoke();
                    var allGames = api?.Database?.Games;
                    var dirs = Directory.GetDirectories(_gamesPath);
                    var supportedExts = Constants.SupportedAudioExtensionsLowercase;
                    var items = new List<GameCardItem>();

                    // Phase 1: Fast scan — build cards without cover art
                    foreach (var dir in dirs)
                    {
                        var files = Directory.GetFiles(dir)
                            .Where(f => supportedExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .ToArray();

                        if (files.Length == 0) continue;

                        var dirName = Path.GetFileName(dir);
                        Game game = null;
                        if (Guid.TryParse(dirName, out var gameId))
                            game = allGames?.FirstOrDefault(g => g.Id == gameId);

                        items.Add(new GameCardItem
                        {
                            GameId = dirName,
                            Name = game?.Name ?? dirName,
                            SongCount = files.Length,
                            TotalDuration = TimeSpan.Zero,
                            CoverArt = null,
                            MusicDirectoryPath = dir
                        });
                    }

                    items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                    // Show cards immediately (no cover art yet)
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        Games = new ObservableCollection<GameCardItem>(items);
                        FilteredGames = new ObservableCollection<GameCardItem>(items);
                        MarkCurrentlyPlayingGame();
                    }));

                    _log?.Invoke($"MusicLibrary: Loaded {items.Count} games with music (loading cover art...)");

                    // Phase 2: Load cover art + durations in background
                    foreach (var card in items)
                    {
                        try
                        {
                            Game game = null;
                            if (Guid.TryParse(card.GameId, out var gid))
                                game = allGames?.FirstOrDefault(g => g.Id == gid);

                            // Load icon for game card
                            if (game != null)
                            {
                                try
                                {
                                    var imageId = game.Icon ?? game.CoverImage ?? game.BackgroundImage;
                                    if (!string.IsNullOrEmpty(imageId))
                                    {
                                        var imagePath = api?.Database?.GetFullFilePath(imageId);
                                        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                                        {
                                            var bitmap = new BitmapImage();
                                            bitmap.BeginInit();
                                            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                            bitmap.DecodePixelWidth = 96;
                                            bitmap.EndInit();
                                            bitmap.Freeze();

                                            var cardRef = card;
                                            var bitmapRef = bitmap;
                                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                            {
                                                cardRef.CoverArt = bitmapRef;
                                            }));
                                        }
                                    }
                                }
                                catch { }
                            }

                            // Calculate total duration from audio files
                            try
                            {
                                var totalDuration = TimeSpan.Zero;
                                var songFiles = Directory.GetFiles(card.MusicDirectoryPath)
                                    .Where(f => supportedExts.Contains(Path.GetExtension(f).ToLowerInvariant()));

                                foreach (var songFile in songFiles)
                                {
                                    try
                                    {
                                        var info = _metadataService?.ReadMetadata(songFile);
                                        if (info != null && info.HasDuration)
                                            totalDuration += info.Duration;
                                    }
                                    catch { }
                                }

                                if (totalDuration > TimeSpan.Zero)
                                {
                                    var cardRef = card;
                                    var dur = totalDuration;
                                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                    {
                                        cardRef.TotalDuration = dur;
                                    }));
                                }
                            }
                            catch { }
                        }
                        catch { }
                    }

                    _log?.Invoke("MusicLibrary: Cover art + duration loading complete");
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"MusicLibrary: Error loading game list: {ex.Message}");
                }
            });
        }

        private void MarkCurrentlyPlayingGame()
        {
            try
            {
                // Check dashboard service first, then main service
                string currentPath = null;
                if (_dashboardService?.IsActive == true)
                    currentPath = _dashboardService.CurrentSongPath;
                else
                    currentPath = _getPlaybackService?.Invoke()?.CurrentSongPath;

                if (Games == null) return;

                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        foreach (var game in Games)
                        {
                            game.IsCurrentlyPlaying = !string.IsNullOrEmpty(currentPath)
                                && currentPath.StartsWith(game.MusicDirectoryPath, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch { }
                }));
            }
            catch { }
        }

        #endregion

        #region Game Detail

        private void LoadGameDetail()
        {
            if (_selectedGame == null) return;

            Task.Run(() =>
            {
                try
                {
                    var files = Directory.GetFiles(_selectedGame.MusicDirectoryPath)
                        .Where(f => Constants.SupportedAudioExtensionsLowercase.Contains(
                            Path.GetExtension(f).ToLowerInvariant()))
                        .OrderBy(f => f)
                        .ToArray();

                    var currentPath = _getPlaybackService?.Invoke()?.CurrentSongPath;
                    var items = new ObservableCollection<SongListItem>();
                    int trackNum = 1;

                    foreach (var path in files)
                    {
                        SongTitleCleaner.ParseFilename(path, out string title, out string artist);
                        var duration = TimeSpan.Zero;
                        try
                        {
                            var info = _metadataService?.ReadMetadata(path);
                            if (info != null && info.HasDuration) duration = info.Duration;
                        }
                        catch { }

                        items.Add(new SongListItem
                        {
                            TrackNumber = trackNum++,
                            FilePath = path,
                            Title = title,
                            Artist = artist,
                            Duration = duration,
                            GameName = _selectedGame.Name,
                            IsCurrentlyPlaying = string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase)
                        });
                    }

                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        GameDetailSongs = items;
                    }));
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"MusicLibrary: Error loading game detail: {ex.Message}");
                }
            });
        }

        private void MarkCurrentSongInDetail()
        {
            try
            {
                var currentPath = _getPlaybackService?.Invoke()?.CurrentSongPath;
                if (GameDetailSongs == null) return;
                foreach (var song in GameDetailSongs)
                    song.IsCurrentlyPlaying = string.Equals(song.FilePath, currentPath, StringComparison.OrdinalIgnoreCase);

                if (AllTracks != null)
                    foreach (var song in AllTracks)
                        song.IsCurrentlyPlaying = string.Equals(song.FilePath, currentPath, StringComparison.OrdinalIgnoreCase);
            }
            catch { }
        }

        #endregion

        #region All Tracks Tab

        private void LoadAllTracks()
        {
            if (_allTracksLoaded) return;
            _allTracksLoaded = true;

            Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(_gamesPath) || !Directory.Exists(_gamesPath)) return;

                    var api = _getApi?.Invoke();
                    var allGames = api?.Database?.Games;
                    var dirs = Directory.GetDirectories(_gamesPath);
                    var supportedExts = Constants.SupportedAudioExtensionsLowercase;
                    var items = new List<SongListItem>();
                    int trackNum = 1;

                    foreach (var dir in dirs)
                    {
                        var dirName = Path.GetFileName(dir);
                        Game game = null;
                        if (Guid.TryParse(dirName, out var gameId))
                            game = allGames?.FirstOrDefault(g => g.Id == gameId);

                        var gameName = game?.Name ?? dirName;
                        var files = Directory.GetFiles(dir)
                            .Where(f => supportedExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .OrderBy(f => f);

                        foreach (var path in files)
                        {
                            SongTitleCleaner.ParseFilename(path, out string title, out string artist);
                            items.Add(new SongListItem
                            {
                                TrackNumber = trackNum++,
                                FilePath = path,
                                Title = title,
                                Artist = artist,
                                GameName = gameName,
                                Duration = TimeSpan.Zero
                            });
                        }
                    }

                    var currentPath = _getPlaybackService?.Invoke()?.CurrentSongPath;
                    foreach (var item in items)
                        item.IsCurrentlyPlaying = string.Equals(item.FilePath, currentPath, StringComparison.OrdinalIgnoreCase);

                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        AllTracks = new ObservableCollection<SongListItem>(items);
                    }));

                    _log?.Invoke($"MusicLibrary: Loaded {items.Count} total tracks");
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"MusicLibrary: Error loading all tracks: {ex.Message}");
                }
            });
        }

        #endregion

        #region Library Stats

        private void LoadLibraryStats()
        {
            Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(_gamesPath) || !Directory.Exists(_gamesPath))
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            GamesWithMusic = 0;
                            TotalSongs = 0;
                            StorageUsed = "0 MB";
                            GamesWithoutMusic = 0;
                            _statsLoaded = true;
                        }));
                        return;
                    }

                    var dirs = Directory.GetDirectories(_gamesPath);
                    int gamesWithMusic = 0;
                    int totalSongs = 0;
                    long totalBytes = 0;
                    var supportedExts = Constants.SupportedAudioExtensionsLowercase;

                    foreach (var dir in dirs)
                    {
                        var files = Directory.GetFiles(dir)
                            .Where(f => supportedExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .ToArray();
                        if (files.Length > 0)
                        {
                            gamesWithMusic++;
                            totalSongs += files.Length;
                            foreach (var f in files)
                            {
                                try { totalBytes += new FileInfo(f).Length; } catch { }
                            }
                        }
                    }

                    int totalGames = 0;
                    try
                    {
                        var api = _getApi?.Invoke();
                        totalGames = api?.Database?.Games?.Count ?? 0;
                    }
                    catch { }

                    string storage;
                    if (totalBytes >= 1_073_741_824)
                        storage = $"{totalBytes / 1_073_741_824.0:F1} GB";
                    else
                        storage = $"{totalBytes / 1_048_576.0:F0} MB";

                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        GamesWithMusic = gamesWithMusic;
                        TotalSongs = totalSongs;
                        StorageUsed = storage;
                        GamesWithoutMusic = Math.Max(0, totalGames - gamesWithMusic);
                        _statsLoaded = true;
                    }));

                    _log?.Invoke($"MusicLibrary: Library stats loaded — {gamesWithMusic} games, {totalSongs} songs, {storage}");
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"MusicLibrary: Error loading library stats: {ex.Message}");
                    _statsLoaded = true;
                }
            });
        }

        #endregion

        #region Search and Tab Switching

        private void ApplySearchFilter()
        {
            var query = _searchText?.Trim() ?? "";
            if (SelectedTabIndex == 0)
            {
                if (string.IsNullOrEmpty(query))
                    FilteredGames = new ObservableCollection<GameCardItem>(Games);
                else
                    FilteredGames = new ObservableCollection<GameCardItem>(
                        Games.Where(g => g.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            }
        }

        private void OnTabChanged()
        {
            if (SelectedTabIndex == 1 && !_allTracksLoaded) LoadAllTracks();
            if (SelectedTabIndex == 4 && !_statsLoaded) LoadLibraryStats();
        }

        #endregion
    }
}
