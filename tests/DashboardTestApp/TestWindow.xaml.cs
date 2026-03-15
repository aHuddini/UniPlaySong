using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using UniPlaySong.Common;
using UniPlaySong.DeskMediaControl;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace DashboardTestApp
{
    public partial class TestWindow : Window
    {
        private MusicLibraryView _dashboardView;
        private MusicLibraryViewModel _viewModel;
        private MockMusicPlaybackService _mainService;
        private TestDashboardPlaybackService _dashboardService;
        private string _testMusicPath;

        public TestWindow()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
            Closing += (s, e) => _dashboardService?.Dispose();
        }

        private void Log(string msg)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var line = $"[{timestamp}] {msg}\n";
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogText.Text += line;
                // Auto-scroll: find parent ScrollViewer and scroll to end
                if (LogText.Parent is System.Windows.Controls.ScrollViewer sv)
                    sv.ScrollToEnd();
            }));
            System.Diagnostics.Debug.WriteLine(msg);
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Log("Initializing test harness...");

                // Use actual Playnite game music path
                _testMusicPath = FindTestMusicPath();
                if (string.IsNullOrEmpty(_testMusicPath))
                {
                    var playniteGamesPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Playnite", "ExtraMetadata", "UniPlaySong", "Games");
                    if (Directory.Exists(playniteGamesPath))
                        _testMusicPath = playniteGamesPath;
                }

                if (_testMusicPath != null)
                {
                    var dirCount = Directory.GetDirectories(_testMusicPath).Length;
                    Log($"Music path: {_testMusicPath} ({dirCount} game dirs)");
                }
                else
                {
                    Log("WARNING: No test music path found");
                }

                _mainService = new MockMusicPlaybackService();
                _dashboardService = new TestDashboardPlaybackService();
                Log("Services created");

                // Subscribe to dashboard events for logging
                _dashboardService.OnPlaybackStateChanged += () =>
                    Log($"[Dashboard] State changed: IsPlaying={_dashboardService.IsPlaying}, IsPaused={_dashboardService.IsPaused}, IsActive={_dashboardService.IsActive}");
                _dashboardService.OnSongChanged += path =>
                    Log($"[Dashboard] Song changed: {Path.GetFileName(path ?? "null")}");
                _dashboardService.OnSongEnded += () =>
                    Log("[Dashboard] Song ended");

                // Subscribe to main service events for logging
                _mainService.OnPlaybackStateChanged += () =>
                    Log($"[Main] State changed: PauseSources=[{string.Join(", ", _mainService.ActivePauseSources)}]");

                var settings = new global::UniPlaySong.UniPlaySongSettings();
                Log($"Settings created: MusicVolume={settings.MusicVolume}");

                _viewModel = new MusicLibraryViewModel(
                    () => _mainService,
                    () => settings,
                    () => null,
                    null,
                    () => null,
                    _testMusicPath ?? "",
                    _dashboardService,
                    msg => Log($"[VM] {msg}")
                );
                Log("ViewModel created");

                // Log ViewModel state
                _viewModel.PropertyChanged += (s2, e2) =>
                {
                    if (e2.PropertyName == "IsPlaying" || e2.PropertyName == "SongTitle" ||
                        e2.PropertyName == "SelectedGame" || e2.PropertyName == "IsNowPlayingExpanded" ||
                        e2.PropertyName == "SelectedTabIndex")
                    {
                        Log($"[VM Property] {e2.PropertyName} changed — IsPlaying={_viewModel.IsPlaying}, SongTitle={_viewModel.SongTitle}, SelectedGame={_viewModel.SelectedGame?.Name ?? "null"}, Tab={_viewModel.SelectedTabIndex}");
                    }
                };

                _dashboardView = new MusicLibraryView();
                Log("View created");

                _dashboardView.Initialize(_viewModel, () => settings);
                Log("View initialized");

                _dashboardView.OnOpened();
                Log("OnOpened called");

                DashboardHost.Content = _dashboardView;
                StatusText.Text = $"Loaded | {_testMusicPath}";
                Log("Dashboard hosted — ready");
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex}");
                StatusText.Text = $"ERROR: {ex.Message}";
            }
        }

        private string FindTestMusicPath()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Playnite", "ExtensionsData");

            if (Directory.Exists(appDataPath))
            {
                foreach (var extDir in Directory.GetDirectories(appDataPath))
                {
                    var gamesDir = Path.Combine(extDir, "GameMusic");
                    if (Directory.Exists(gamesDir) && Directory.GetDirectories(gamesDir).Length > 0)
                        return gamesDir;
                }
            }

            return null;
        }

        private void OnSimulateMainPlaying(object sender, RoutedEventArgs e)
        {
            _mainService.IsPlaying = true;
            Log("[Test] Simulated main player playing");
            StatusText.Text = "Main: Playing";
        }

        private void OnSimulateMainStop(object sender, RoutedEventArgs e)
        {
            _mainService.IsPlaying = false;
            _mainService.ActivePauseSources.Clear();
            Log("[Test] Simulated main player stopped, cleared pause sources");
            StatusText.Text = "Main: Stopped";
        }

        private void OnBackToGrid(object sender, RoutedEventArgs e)
        {
            _viewModel.SelectedGame = null;
            Log("[Test] Back to grid — SelectedGame = null");
        }

        private void OnClearLog(object sender, RoutedEventArgs e)
        {
            LogText.Text = "";
        }
    }
}
