using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Playnite.SDK.Controls;
using UniPlaySong.Audio;

namespace UniPlaySong.Controls
{
    // PS5-style Now-Playing popup: large art, source, timeline, full transport + volume.
    // With a strip it is UPS_MusicPlayerControlVisual: the strip (a MusicVisualControl) stands on the seek bar, the bar
    // gets a knob, and the card is the album art, blurred, under a dark tint of its colour.
    public partial class MediaControllerOverlay : PluginUserControl
    {
        internal static readonly Color CardColor = Color.FromRgb(0x0E, 0x11, 0x16);   // the card's own near-black
        private const byte CardAlpha = 0xB8;   // the variant's card over its frosted art: the art shows through, text stays clear

        private readonly ActiveMediaViewModel _vm;
        private readonly FrameworkElement _strip;
        private readonly string _paletteStore;
        private readonly Func<string> _gameName;
        private string _tintFor;

        // paletteStore: where picture colours are kept (VisualizerCoverPalette), for the tint. gameName: the game that owns
        // the playing song (null when none: default music, a custom folder), for the pill.
        public MediaControllerOverlay(ActiveMediaViewModel vm, FrameworkElement strip = null, string paletteStore = null, Func<string> gameName = null)
        {
            ((IComponentConnector)this).InitializeComponent();
            DataContext = vm;
            _vm = vm;
            if (strip == null) return;
            _strip = strip;
            _paletteStore = paletteStore;
            _gameName = gameName;
            SourceLabel.Visibility = Visibility.Collapsed;
            // the knob style's height: the XAML's own Height="4" on the bar outranks a style, which left a 4 px bar that
            // clipped the knob and was hard to hit
            SeekBar.ClearValue(HeightProperty);
            SeekBar.Style = (Style)FindResource("UpsKnobSeekBar");
            // Time labels of a fixed width: the bar between them (and the strip above it) keeps its width as the times change
            // length - a width change restarts the strip's renderer
            PositionLabel.MinWidth = DurationLabel.MinWidth = 34;
            DurationLabel.TextAlignment = TextAlignment.Right;
            // Fixed width: sized to its title and art it jumped between songs, and every jump restarted the strip's renderer
            CardRoot.Width = 420;
            // The frosted cover: blurred once per cover and reused every frame (BitmapCache), clipped to the card's rounded
            // corners
            Frost.Visibility = Visibility.Visible;
            Frost.CacheMode = new BitmapCache(0.5);
            Frost.SizeChanged += (s, e) => Frost.Clip = new RectangleGeometry(new Rect(e.NewSize), 16, 16);
            StripHost.Content = strip;
            Card.Background = new SolidColorBrush(WithAlpha(CardColor));
            ShowStrip();
            Tint();
            ShowGame();
            IsVisibleChanged += (s, e) => ShowStrip();
            vm.PropertyChanged += OnMediaChanged;
        }

        // The strip's row only while there is UniPlaySong playback to hear (NAudio): on SDL2 the bar stays plain, with no
        // empty row above it. Checked whenever the popup appears.
        private void ShowStrip() =>
            StripHost.Visibility = _strip != null && VisualizationDataProvider.Current != null ? Visibility.Visible : Visibility.Collapsed;

        private void OnMediaChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ActiveMediaViewModel.AlbumArtPath)) Tint();
            // every snapshot (Progress ticks), not only a new title: the song's path can land after its title
            if (e.PropertyName == nameof(ActiveMediaViewModel.Title) || e.PropertyName == nameof(ActiveMediaViewModel.SourceName)
                || e.PropertyName == nameof(ActiveMediaViewModel.Progress)) ShowGame();
        }

        // The pill: Spotify's name for Spotify, else the game that owns the song; none for music no game owns. A new name
        // crossfades in while the pill eases to its width, so it keeps its shape across a game switch. A name that goes
        // missing for a moment (between songs nothing is playing) keeps the pill; it hides only if still missing after 1.5 s.
        private string _shownGame;
        private System.Windows.Threading.DispatcherTimer _hidePill;

        private void ShowGame()
        {
            string name = _vm?.SourceKind == Models.ActiveMediaSourceKind.Spotify ? _vm.SourceName : _gameName?.Invoke();
            if (string.IsNullOrEmpty(name))
            {
                if (GamePill.Visibility != Visibility.Visible || _hidePill?.IsEnabled == true) return;
                if (_hidePill == null)
                {
                    _hidePill = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    _hidePill.Tick += (s, e) => { _hidePill.Stop(); GamePill.Visibility = Visibility.Collapsed; GamePill.BeginAnimation(WidthProperty, null); GamePill.Width = double.NaN; _shownGame = null; };
                }
                _hidePill.Start();
                return;
            }
            _hidePill?.Stop();
            if (name == _shownGame) return;
            _shownGame = name;
            if (GamePill.Visibility != Visibility.Visible)
            {
                GamePillText.Text = name;
                GamePill.Visibility = Visibility.Visible;
                GamePill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));
                return;
            }
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.12));
            fadeOut.Completed += (s, e) =>
            {
                if (_shownGame != name) return;   // another switch came in meanwhile: that one's fade carries on
                double from = GamePill.ActualWidth;
                GamePillText.Text = name;
                GamePill.BeginAnimation(WidthProperty, null);   // a held width would win over the auto one being measured
                GamePill.Width = double.NaN;
                GamePill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                GamePill.BeginAnimation(WidthProperty, new DoubleAnimation(from, GamePill.DesiredSize.Width, TimeSpan.FromSeconds(0.2))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                GamePillText.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.15)));
            };
            GamePillText.BeginAnimation(OpacityProperty, fadeOut);
        }

        // On a new cover: the frosted cover behind the card, and the card fades (0.8 s) to a dark tint of its colours. Both
        // off the UI thread - the cover decoded small (it is blurred anyway) and read whole (no file lock), the colours from
        // the colour store, so a picture is only taken apart once. No art, or one that won't decode: the card's own near-black.
        private void Tint()
        {
            string art = _vm?.AlbumArtPath;
            if (art == _tintFor) return;
            _tintFor = art;
            if (string.IsNullOrEmpty(art)) { FrostFill.Fill = null; FadeTo(CardColor); return; }
            Task.Run(() =>
            {
                Color to = CardColor;
                BitmapImage cover = null;
                try { if (!string.IsNullOrEmpty(_paletteStore)) to = DarkTint(VisualizerCoverPalette.Get(art, _paletteStore, out _)); }
                catch (Exception) { }   // colours unreadable: the card's own
                try
                {
                    cover = new BitmapImage();
                    cover.BeginInit();
                    cover.UriSource = new Uri(art, UriKind.Absolute);
                    cover.DecodePixelWidth = 160;
                    cover.CacheOption = BitmapCacheOption.OnLoad;
                    cover.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    cover.EndInit();
                    cover.Freeze();
                }
                catch (Exception) { cover = null; }   // unreadable art: no frost
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_tintFor != art) return;
                    FrostFill.Fill = cover == null ? null : new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
                    FadeTo(to);
                }));
            });
        }

        private void FadeTo(Color c) =>
            Card.Background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(WithAlpha(c), TimeSpan.FromSeconds(0.8)));

        private static Color WithAlpha(Color c) => Color.FromArgb(CardAlpha, c.R, c.G, c.B);

        // The art's most vivid colour, darkened (brightest channel to 46 of 255) and mixed 60% into the card's own
        // near-black: a lean toward the art that keeps white text readable. A cover with no colour in it keeps the card's.
        internal static Color DarkTint(Color[] palette)
        {
            var vivid = palette.OrderByDescending(c => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B))).First();
            int max = Math.Max(vivid.R, Math.Max(vivid.G, vivid.B)), min = Math.Min(vivid.R, Math.Min(vivid.G, vivid.B));
            if (max - min < 13) return CardColor;   // under 0.05 saturation: grey
            byte Mix(byte card, byte art) => (byte)Math.Round(card + (art * 46.0 / max - card) * 0.6);
            return Color.FromRgb(Mix(CardColor.R, vivid.R), Mix(CardColor.G, vivid.G), Mix(CardColor.B, vivid.B));
        }
    }
}
