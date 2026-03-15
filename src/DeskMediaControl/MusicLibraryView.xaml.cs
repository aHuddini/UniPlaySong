using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using UniPlaySong.Audio;
using UniPlaySong.Models;

namespace UniPlaySong.DeskMediaControl
{
    public partial class MusicLibraryView : UserControl
    {
        private MusicLibraryViewModel _viewModel;
        private SpectrumVisualizerControl _visualizer;
        private Func<UniPlaySongSettings> _getSettings;
        private Grid[] _tabPanels;
        private DispatcherTimer _glowTimer;
        private double _smoothedIntensity;

        public MusicLibraryView()
        {
            InitializeComponent();
        }

        public void Initialize(MusicLibraryViewModel viewModel, Func<UniPlaySongSettings> getSettings)
        {
            _viewModel = viewModel;
            _getSettings = getSettings;
            DataContext = viewModel;

            _tabPanels = new[] { GamesPanel, TracksPanel, ArtistsPanel, GenresPanel, StatsPanel };

            // Audio-reactive glow timer (~30fps for smooth animation)
            _glowTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _glowTimer.Tick += OnGlowTimerTick;

            try { CreateVisualizer(); }
            catch { }
            try { CreateExpandedVisualizer(); }
            catch { }
        }

        private void CreateVisualizer()
        {
            _visualizer = new SpectrumVisualizerControl();
            _visualizer.SetSettingsProvider(_getSettings);
            _visualizer.HorizontalAlignment = HorizontalAlignment.Stretch;
            _visualizer.VerticalAlignment = VerticalAlignment.Center;

            VisualizerContainer.SizeChanged += (s, e) =>
            {
                try
                {
                    if (e.NewSize.Width > 0 && _visualizer.Width > 0)
                    {
                        double scale = e.NewSize.Width / _visualizer.Width;
                        _visualizer.LayoutTransform = new ScaleTransform(scale, 1.0);
                    }
                }
                catch { }
            };

            VisualizerContainer.Child = _visualizer;
        }

        private void CreateExpandedVisualizer()
        {
            var expandedViz = new SpectrumVisualizerControl();
            expandedViz.SetSettingsProvider(_getSettings);
            expandedViz.HorizontalAlignment = HorizontalAlignment.Stretch;
            expandedViz.VerticalAlignment = VerticalAlignment.Center;

            ExpandedVisualizerContainer.SizeChanged += (s, e) =>
            {
                try
                {
                    if (e.NewSize.Width > 0 && expandedViz.Width > 0)
                    {
                        double scale = e.NewSize.Width / expandedViz.Width;
                        expandedViz.LayoutTransform = new ScaleTransform(scale, 1.0);
                    }
                }
                catch { }
            };

            ExpandedVisualizerContainer.Child = expandedViz;
        }

        public void OnOpened()
        {
            try { _visualizer?.SetActive(true); } catch { }
            try { _viewModel?.OnDashboardOpened(); } catch { }
            try { _glowTimer?.Start(); } catch { }
        }

        public void OnClosed()
        {
            try { _visualizer?.SetActive(false); } catch { }
            try { _viewModel?.OnDashboardClosed(); } catch { }
            try { _glowTimer?.Stop(); } catch { }
        }

        private void OnTabClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out int index))
                {
                    _viewModel.SelectedTabIndex = index;
                    ShowTabPanel(index);
                }
            }
            catch { }
        }

        private void ShowTabPanel(int index)
        {
            if (_tabPanels == null) return;
            for (int i = 0; i < _tabPanels.Length; i++)
            {
                _tabPanels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OnGameCardClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is GameCardItem card)
                {
                    _viewModel.SelectedGame = card;
                }
            }
            catch { }
        }

        private void OnGamePlayClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Stop the click from bubbling up to the card click handler
                e.Handled = true;
                if (sender is Button btn && btn.Tag is GameCardItem card)
                {
                    _viewModel?.PlayGameByCard(card);
                }
            }
            catch { }
        }

        private void OnDetailTrackDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (DetailTrackListBox.SelectedItem is SongListItem item)
                    _viewModel?.PlaySong(item);
            }
            catch { }
        }

        private void OnAllTrackDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (AllTracksListBox.SelectedItem is SongListItem item)
                    _viewModel?.PlaySong(item);
            }
            catch { }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (_viewModel != null)
                    _viewModel.SearchText = SearchBox.Text;
            }
            catch { }
        }

        private void OnGamesGridSizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                // ~300px per card row. Adapt columns to available width.
                int columns = Math.Max(1, (int)(e.NewSize.Width / 300));
                var panel = FindUniformGrid(GamesGrid);
                if (panel != null)
                    panel.Columns = columns;
            }
            catch { }
        }

        private void OnGlowTimerTick(object sender, EventArgs e)
        {
            try
            {
                var vizProvider = VisualizationDataProvider.Current;
                double targetIntensity = 0;

                if (vizProvider != null)
                {
                    vizProvider.GetLevels(out float peak, out float rms);
                    targetIntensity = rms * 0.6 + peak * 0.4;
                }

                // Smooth the intensity to avoid flickering
                _smoothedIntensity += (targetIntensity - _smoothedIntensity) * 0.3;

                // Apply glow to currently-playing game cards
                if (GamesGrid == null || !GamesGrid.IsLoaded) return;

                var itemsGen = GamesGrid.ItemContainerGenerator;
                if (itemsGen == null) return;

                for (int i = 0; i < GamesGrid.Items.Count; i++)
                {
                    var container = itemsGen.ContainerFromIndex(i) as ContentPresenter;
                    if (container == null) continue;

                    var item = GamesGrid.Items[i] as GameCardItem;
                    if (item == null) continue;

                    // Find the Button inside the ContentPresenter
                    var button = FindChild<Button>(container);
                    if (button == null) continue;

                    if (item.IsCurrentlyPlaying && _smoothedIntensity > 0.01)
                    {
                        var shadow = button.Effect as DropShadowEffect;
                        if (shadow == null)
                        {
                            shadow = new DropShadowEffect
                            {
                                ShadowDepth = 0,
                                Direction = 0,
                                Color = (Color)ColorConverter.ConvertFromString("#6c5ce7"),
                                BlurRadius = 0
                            };
                            button.Effect = shadow;
                        }
                        shadow.BlurRadius = 4 + _smoothedIntensity * 20;
                        shadow.Opacity = 0.3 + _smoothedIntensity * 0.7;
                    }
                    else
                    {
                        if (button.Effect != null)
                            button.Effect = null;
                    }
                }
            }
            catch { }
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var descendant = FindChild<T>(child);
                if (descendant != null) return descendant;
            }
            return null;
        }

        private static System.Windows.Controls.Primitives.UniformGrid FindUniformGrid(ItemsControl itemsControl)
        {
            try
            {
                if (itemsControl == null || !itemsControl.IsLoaded) return null;
                var presenter = VisualTreeHelper.GetChild(itemsControl, 0) as FrameworkElement;
                if (presenter == null) return null;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(presenter); i++)
                {
                    var child = VisualTreeHelper.GetChild(presenter, i);
                    if (child is System.Windows.Controls.Primitives.UniformGrid ug)
                        return ug;
                }
            }
            catch { }
            return null;
        }
    }
}
