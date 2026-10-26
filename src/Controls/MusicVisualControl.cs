using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using UniPlaySong.Audio;
using UniPlaySong.Common;
using UniPlaySong.Services;
using static UniPlaySong.Controls.VisualizerBridge;

namespace UniPlaySong.Controls
{
    // Theme element "UPS_MusicVisualControl": a visualizer drawn by a separate 64-bit renderer
    // (Visualizer\UniPlaySongVisualizer.exe) and shown here with real transparency over whatever the theme draws behind
    // it. No setting: the renderer runs while the element is on screen and UPS plays through NAudio (Live Effects and the
    // like - the renderer hears UPS's own output, VisualizerAudioFeed), and draws one frame per WPF frame. The theme picks
    // the style by the Tag (ParseTag), the Jukebox Fountain by default. Never focusable or hit-testable, so it can't take
    // controller focus. Layout and native side: native/visualizer/.
    // The same control is the music player's spectrum strip (UPS_MusicPlayerControlVisual, see MediaControllerOverlay):
    // its own style (UPS - Seek Spectrum.vis) and the popup's art for colours. A strip on screen borrows the renderer
    // from a page visualizer, which holds its last frame until the strip goes.
    public class MusicVisualControl : PluginUserControl
    {
        private static readonly string Folder =
            Path.Combine(Path.GetDirectoryName(typeof(MusicVisualControl).Assembly.Location) ?? string.Empty, "Visualizer");
        private static readonly string RendererPath = Path.Combine(Folder, "UniPlaySongVisualizer.exe");

        // The bridge holds one device and one set of shared textures, so only one element shows at a time.
        private static MusicVisualControl _owner;
        // Page visualizers waiting for a strip to give the renderer back: the one it was borrowed from (holding its last
        // frame) and any that wanted it meanwhile. Restarted when the strip goes.
        private static readonly List<MusicVisualControl> _waiting = new List<MusicVisualControl>();
        private static Task _rendererExited = Task.FromResult(0);
        private static int _starts;

        private readonly ISettingsProvider _settings;
        private readonly FileLogger _log;
        // Sized in code to the frame's box in device pixels, centred: shown 1:1, never stretched to the element. No layout
        // rounding: it rounds to the DPI's grid, which under Fullscreen's Viewbox isn't the pixel grid - a 988 px frame
        // (592.8 units) came out 593 units, 988.33 px, and every row was resampled. _snap puts it on whole pixels instead.
        private readonly Image _image = new Image
        {
            Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            UseLayoutRounding = false
        };
        private readonly TranslateTransform _snap = new TranslateTransform();   // the image's sub-pixel nudge, see OnRendering
        private Vector _deviceScale = new Vector(1, 1);
        private string _shownAt;   // where the frame was last logged as shown (debug log)
        // The Jukebox Fountain's palette from a picture: three colours (VisualizerCoverPalette) sent to the renderer, which
        // builds the palette and crossfades to it. Which picture is the theme's choice, by the element's Tag: "Background"
        // (the default - the game's background, what fills the screen), "Cover" (the game's cover) or "Disc" (the
        // now-playing art: the song's own, else the cover). Each falls back to the others when its picture is missing.
        private string _gameBackground, _gameCover;   // the game shown's, when the file is there (GameContextChanged)
        private string _coverFor;   // the art the renderer last got colours for (null: none sent since it started)
        private string _tagStyle;   // the Tag's parts (ParseTag)
        private string _tagPalette;
        private bool _coverSent;
        private readonly DispatcherTimer _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private bool _subscribed;
        private Process _renderer;
        private VisualizerAudioFeed _feed;
        private VisualizerSongAnalysis _songAnalysis;   // the Jukebox Fountain only: its beats and sections need the song analysed
        private readonly string _otherMusicFolder;
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _view;
        private IntPtr _tick;
        private D3DImage _d3d;
        private int _w, _h;
        private long _lastFrame;
        private int _generation;
        private Size _startedAt;
        private string _startedPreset;
        private Window _window;
        private bool IsStrip { get; }   // the music player's spectrum strip, not the page visualizer

        // otherMusicFolder: where song analyses of music outside the game folders go (the Jukebox Fountain).
        // strip: the music player's spectrum strip (UPS_MusicPlayerControlVisual).
        public MusicVisualControl(ISettingsProvider settings, FileLogger log, string otherMusicFolder, bool strip = false)
        {
            _settings = settings;
            _log = log;
            _otherMusicFolder = otherMusicFolder;
            IsStrip = strip;
            Focusable = false;
            IsHitTestVisible = false;
            // the theme's element carries the Tag (the palette's picture and the style, see ParseTag); Playnite doesn't
            // pass it on, so take it from there
            if (!IsStrip)
                SetBinding(TagProperty, new System.Windows.Data.Binding(nameof(Tag))
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Control), 1)
                });
            else _image.VerticalAlignment = VerticalAlignment.Bottom;   // the bars stand on the seek bar
            _image.RenderTransform = _snap;
            var grid = new Grid();
            grid.Children.Add(_image);
            Content = grid;

            // The renderer's frame size is fixed when it starts. When the element settles at another size (a page's text
            // loading, a resolution change), restart it so the frame matches again - the presets lay out around the
            // centre to line up with the page, and a scaled-to-fit frame would drift off it.
            _resizeTimer.Tick += (s, e) => { _resizeTimer.Stop(); if (_owner == this && IsVisible) { Stop(); Start(); } };
            SizeChanged += (s, e) =>
            {
                if (_owner != this) return;
                if (Math.Abs(e.NewSize.Width - _startedAt.Width) < 8 && Math.Abs(e.NewSize.Height - _startedAt.Height) < 8) return;
                _resizeTimer.Stop();
                _resizeTimer.Start();
            };
            // A collapsed element stays loaded, so visibility - not Loaded - decides whether the renderer runs.
            IsVisibleChanged += (s, e) => Refresh();
            Loaded += (s, e) =>
            {
                Subscribe();
                Refresh();
            };
            Unloaded += (s, e) =>
            {
                Unsubscribe();
                Stop();
            };
        }

        // True while the visualizer is on screen and drawing. Themes can bind it (Content.IsRunning on the element) to show
        // a fallback - a WPF effect, say - only when the visualizer is off or missing, never on top of it.
        private static readonly DependencyPropertyKey IsRunningKey = DependencyProperty.RegisterReadOnly(
            nameof(IsRunning), typeof(bool), typeof(MusicVisualControl), new PropertyMetadata(false));
        public static readonly DependencyProperty IsRunningProperty = IsRunningKey.DependencyProperty;
        public bool IsRunning => (bool)GetValue(IsRunningProperty);

        // The Jukebox Fountain's source: 0 reacting live, 1 analysing the song, 2 following the analysed song.
        // Themes can bind it (Content.SongState on the element).
        private static readonly DependencyPropertyKey SongStateKey = DependencyProperty.RegisterReadOnly(
            nameof(SongState), typeof(int), typeof(MusicVisualControl), new PropertyMetadata(0));
        public static readonly DependencyProperty SongStateProperty = SongStateKey.DependencyProperty;
        public int SongState => (int)GetValue(SongStateProperty);

        // Only NAudio playback can be heard (VisualizationDataProvider); on SDL2 there is none, so nothing starts
        private static bool Enabled => VisualizationDataProvider.Current != null;

        // The frame in device pixels: at most 2560x1440, even. A page visualizer too small to measure falls back to 1280x720;
        // a strip is its own size, down to 4 px, and (0, 0) below that - too thin to draw, so it doesn't start.
        internal static (int w, int h) FrameSize(double width, double height, Vector deviceScale, bool strip)
        {
            int ew = (int)(width * deviceScale.X), eh = (int)(height * deviceScale.Y);
            if (strip) { if (ew < 4 || eh < 4) return (0, 0); }
            else if (ew < 64 || eh < 64) { ew = 1280; eh = 720; }
            return (Math.Min(ew, 2560) & ~1, Math.Min(eh, 1440) & ~1);
        }

        // What Start does when the renderer may be someone else's: take it (nobody has it), borrow it (a strip, from a
        // page - nobody opens the player over its own page visualizer), wait for it (a page, while a strip has it), or
        // leave it (one at a time, as before).
        internal enum Claim { Take, Borrow, Wait, Leave }

        internal static Claim ClaimFrom(bool strip, bool hasOwner, bool ownerIsStrip) =>
            !hasOwner ? Claim.Take : strip && !ownerIsStrip ? Claim.Borrow : !strip && ownerIsStrip ? Claim.Wait : Claim.Leave;

        // A strip that goes (not one restarting at a new size) hands the renderer back to the waiting pages - even if it
        // never got it (closed while the page's renderer was still exiting) - unless another strip has it now.
        internal static bool HandsBack(bool strip, bool ownedByAnother, bool staying) => strip && !ownedByAnother && !staying;

        private static void HandBack()
        {
            var pages = _waiting.ToArray();
            _waiting.Clear();
            foreach (var page in pages) page.Refresh();
        }

        // The chosen style's file: a MilkDrop preset made by native/visualizer/presets/make_presets.py, or the Jukebox
        // Fountain's style file, made by native/visualizer/bench/tools/export_t8.mjs. The strip has its own.
        // The theme's style, else the Jukebox Fountain.
        private string PresetPath => Path.Combine(Folder, "presets", IsStrip ? "UPS - Seek Spectrum.vis"
            : ThemeStyleFile(_tagStyle, file => File.Exists(Path.Combine(Folder, "presets", file))) ?? StyleFile(MusicVisualizerStyle.JukeboxFountain));

        // The theme's Tag: the palette's picture alone ("Cover"), or keyed parts - "Style=GlowBars; Palette=Cover". Keys
        // ignore case.
        internal static (string style, string palette) ParseTag(string tag)
        {
            string style = null, palette = null;
            foreach (var part in (tag ?? string.Empty).Split(';'))
            {
                int eq = part.IndexOf('=');
                string key = eq < 0 ? "Palette" : part.Substring(0, eq).Trim();
                string value = (eq < 0 ? part : part.Substring(eq + 1)).Trim();
                if (value.Length == 0) continue;
                if (key.Equals("Style", StringComparison.OrdinalIgnoreCase)) style = value;
                else if (key.Equals("Palette", StringComparison.OrdinalIgnoreCase)) palette = value;
            }
            return (style, palette);
        }

        // The theme's style as a preset file, or null (none, or one UniPlaySong doesn't have: the default then). A
        // built-in name (JukeboxFountain, GlowBars, Fountain; case and spaces ignored), else a shipped style by name -
        // "Midnight Neon" is presets\UPS - Midnight Neon.vis, a theme developer's VisualizerPrototyper style added in an
        // update. A name, never a path: a theme picks among UniPlaySong's own styles, not a file of its own.
        internal static string ThemeStyleFile(string name, Func<string, bool> shipped)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Enum.TryParse(name.Replace(" ", string.Empty), true, out MusicVisualizerStyle s) && Enum.IsDefined(typeof(MusicVisualizerStyle), s))
                return StyleFile(s);
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            string file = "UPS - " + name + ".vis";
            return shipped(file) ? file : null;
        }

        // Read once per Tag change, not per frame (PaletteArt runs every frame); a new style restarts the renderer
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property != TagProperty || IsStrip) return;
            (_tagStyle, _tagPalette) = ParseTag(e.NewValue as string);
            Refresh();
        }

        private static string StyleFile(MusicVisualizerStyle style)
        {
            switch (style)
            {
                case MusicVisualizerStyle.GlowBars: return "UPS - Glow Bars.milk";
                case MusicVisualizerStyle.Fountain: return "UPS - Fountain.milk";
                default: return "UPS - Jukebox Fountain.vis";
            }
        }

        // Start or stop to match "on screen and something to hear". Deferred to after layout, so the renderer can match the
        // element's real size.
        private void Refresh() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible || !Enabled) { Stop(); return; }
            // A preset is loaded once at start, so a new style means a new renderer.
            if (_owner == this && _startedPreset != PresetPath) Stop();
            Start();
        }), DispatcherPriority.Loaded);

        // A settings save can move playback onto or off NAudio (Live Effects), so check again after one
        private void Subscribe()
        {
            if (_settings == null || _subscribed) return;
            _settings.SettingsChanged += OnSettingsReplaced;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_settings == null) return;
            _settings.SettingsChanged -= OnSettingsReplaced;
            _subscribed = false;
        }

        private void OnSettingsReplaced(object sender, SettingsChangedEventArgs e) => Refresh();

        private void Start()
        {
            if (!Enabled)
            {
                _log?.Debug("[Visualizer] Not started: no NAudio playback to hear");
                return;
            }
            var uiTime = Stopwatch.StartNew();   // how long starting holds the UI thread (the debug log)
            // The frame's box: the element in device pixels, centred and shown 1:1 - pixel for pixel is what keeps the
            // visualizer sharp. Past the cap the rest of the element stays empty rather than upscaled (an upscaled frame -
            // 1728 px stretched over 2880 - looked blurry all over, and filling the width also made every column wider than
            // the page was laid out for).
            _deviceScale = ToDevice(this, new Point(1, 1)) - ToDevice(this, new Point(0, 0));
            var (w, h) = FrameSize(ActualWidth, ActualHeight, _deviceScale, IsStrip);
            if (w == 0)
            {
                _log?.Debug($"[Visualizer] Not started: too small to draw ({ActualWidth:0}x{ActualHeight:0})");
                return;
            }
            switch (ClaimFrom(IsStrip, _owner != null, _owner?.IsStrip == true))
            {
                case Claim.Leave:
                    return;
                case Claim.Wait:
                    if (!_waiting.Contains(this)) _waiting.Add(this);
                    return;
                case Claim.Borrow:
                    var page = _owner;
                    if (!_waiting.Contains(page)) _waiting.Add(page);
                    page.Suspend();
                    break;
            }
            // A new renderer must not start before the old one has gone: its shared memory would still be in use.
            if (!_rendererExited.IsCompleted)
            {
                _rendererExited.ContinueWith(_ => Refresh());
                return;
            }
            string preset = PresetPath;
            if (!File.Exists(RendererPath) || !File.Exists(preset))
            {
                _log?.Warn($"[Visualizer] Not installed: {RendererPath} or its preset is missing");
                if (IsStrip) HandBack();
                return;
            }

            _owner = this;
            _startedPreset = preset;
            int generation = ++_generation;
            int pid = Process.GetCurrentProcess().Id;
            string channel = $@"Local\UniPlaySong_Visualizer_{pid}_{++_starts}";
            try
            {
                Load(Folder);
                _feed = VisualizerAudioFeed.Open(channel + "_Audio");
                // every .vis style is the Jukebox Fountain engine, which follows the song's analysis
                if (!IsStrip && preset.EndsWith(".vis", StringComparison.OrdinalIgnoreCase))
                    _songAnalysis = new VisualizerSongAnalysis(_feed, _log, _otherMusicFolder);
            }
            catch (Exception ex)
            {
                _log?.Warn($"[Visualizer] Could not start: {ex.Message}");
                _owner = null;
                if (IsStrip) HandBack();
                return;
            }

            _startedAt = new Size(ActualWidth, ActualHeight);
            _image.Width = w / _deviceScale.X;
            _image.Height = h / _deviceScale.Y;

            try
            {
                // The parent pid lets the renderer exit with Playnite even if Playnite crashes.
                _renderer = Process.Start(new ProcessStartInfo(RendererPath, $"\"{preset}\" {w} {h} {channel} {pid}")
                    { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Folder });
            }
            catch (Exception ex)
            {
                _log?.Warn($"[Visualizer] Renderer did not start: {ex.Message}");
                Fail();
                return;
            }
            _log?.Debug($"[Visualizer] Started {w}x{h}, channel {channel} ({uiTime.ElapsedMilliseconds} ms on the UI thread)");

            // The renderer's shared memory takes a moment to appear; don't hold the UI thread for it.
            Task.Run(() =>
            {
                MemoryMappedFile mmf = null;
                for (int i = 0; i < 100 && mmf == null; i++)
                {
                    try { mmf = MemoryMappedFile.OpenExisting(channel + "_Frames"); }
                    catch (FileNotFoundException) { System.Threading.Thread.Sleep(50); }
                }
                var view = mmf?.CreateViewAccessor();
                for (int i = 0; view != null && i < 100 && view.ReadUInt32(OffMagic) != FramesMagic; i++)
                    System.Threading.Thread.Sleep(20);
                Dispatcher.BeginInvoke(new Action(() => Attach(generation, channel, mmf, view)));
            });
        }

        private void Attach(int generation, string channel, MemoryMappedFile mmf, MemoryMappedViewAccessor view)
        {
            if (generation != _generation || view == null || view.ReadUInt32(OffMagic) != FramesMagic)
            {
                view?.Dispose(); mmf?.Dispose();
                if (generation == _generation)
                {
                    _log?.Warn("[Visualizer] Renderer never published its frames");
                    Fail();
                }
                return;
            }
            _mmf = mmf;
            _view = view;
            _w = view.ReadInt32(OffWidth);
            _h = view.ReadInt32(OffHeight);
            _tick = CreateEventW(IntPtr.Zero, false, false, channel + "_Tick");

            var hwnd = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
            int hr = hwnd == IntPtr.Zero ? -1 : vt_init(hwnd);
            for (int i = 0; hr == 0 && i < 3; i++) hr = vt_open(i, view.ReadUInt64(OffHandles + 8 * i), _w, _h);
            var back = hr == 0 ? vt_backbuffer(_w, _h) : IntPtr.Zero;
            if (back == IntPtr.Zero)
            {
                _log?.Warn($"[Visualizer] Could not open the renderer's textures (0x{hr:x8})");
                Fail();
                return;
            }

            _d3d = new D3DImage();
            _d3d.Lock();
            _d3d.SetBackBuffer(D3DResourceType.IDirect3DSurface9, back, true);
            _d3d.Unlock();
            _image.Source = _d3d;
            _shownAt = null;
            _coverSent = false;   // a new renderer: send it the cover again
            _window = Window.GetWindow(this);
            _lastFrame = 0;
            CompositionTarget.Rendering += OnRendering;
            SetValue(IsRunningKey, true);
        }

        // One renderer frame per WPF frame: tell it what's being heard, ask for the next frame, show the newest ready.
        private void OnRendering(object sender, EventArgs e)
        {
            _feed?.PublishPlayed();
            _songAnalysis?.Tick();
            // Ask for a frame only while there is something to show: not after 1.5 s of silence (paused, stopped, between
            // songs - the visuals have settled by then) and not while Playnite is in the background (a game, another app).
            // The last frame stays up and the renderer waits, drawing nothing.
            bool idle = _feed == null || unchecked(Environment.TickCount - _feed.LastSoundTick) > 1500 || _window?.IsActive == false;
            if (!idle) SetEvent(_tick);
            PublishCover();
            int songState = _view.ReadInt32(OffSongState);
            if (songState != SongState)
            {
                _log?.Debug($"[Visualizer] Song state {(songState == 2 ? "following" : songState == 1 ? "analysing" : "live")} at +{_songAnalysis?.SinceSongMs} ms");
                SetValue(SongStateKey, songState);
            }
            // Keep the frame on whole device pixels. Layout rounding works in this element's units, and under Fullscreen's
            // Viewbox a whole unit is a fraction of a pixel (the stage's top, 110 units down, lands at 183.3 px at 200%) -
            // WPF would then resample every frame and the visualizer goes soft. The layout offset leaves out _snap itself.
            if (VisualTreeHelper.GetParent(_image) is Visual parent)
            {
                var at = ToDevice(parent, (Point)VisualTreeHelper.GetOffset(_image));
                _snap.X = (Math.Round(at.X) - at.X) / _deviceScale.X;
                _snap.Y = (Math.Round(at.Y) - at.Y) / _deviceScale.Y;
                // Where the frame actually lands (after _snap), whenever that changes: whole-pixel corners and the frame's
                // own size mean it's shown 1:1.
                var tl = ToDevice(_image, new Point(0, 0));
                var br = ToDevice(_image, new Point(_image.ActualWidth, _image.ActualHeight));
                var shownAt = $"at {tl.X:0.###},{tl.Y:0.###}, {br.X - tl.X:0.###}x{br.Y - tl.Y:0.###} device px for a {_w}x{_h} frame";
                if (_image.ActualWidth > 0 && shownAt != _shownAt) { _shownAt = shownAt; _log?.Debug($"[Visualizer] Shown {shownAt}"); }
            }
            long frame = _view.ReadInt64(OffFrame);
            if (frame == _lastFrame || frame <= 0 || !_d3d.IsFrontBufferAvailable) return;
            if (_lastFrame == 0) _log?.Debug($"[Visualizer] Frames by {(_view.ReadInt32(OffTransport) == 2 ? "zero-copy (GPU only)" : "copy (through the CPU)")}");
            _lastFrame = frame;
            _d3d.Lock();
            vt_copy(_view.ReadInt32(OffLatest));
            _d3d.AddDirtyRect(new Int32Rect(0, 0, _w, _h));
            _d3d.Unlock();
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            _gameBackground = GameFile(newContext?.BackgroundImage);
            _gameCover = GameFile(newContext?.CoverImage);
        }

        private static string GameFile(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                string path = Playnite.SDK.API.Instance?.Database?.GetFullFilePath(id);
                return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
            }
            catch (Exception) { return null; }   // a URL rather than a file, or a path it won't take
        }

        // The picture the palette comes from, by the Tag's choice, falling back to the others. The strip: the player's own
        // art (the now-playing art the popup shows). Spotify: its album art whatever the Tag - the selected game has
        // nothing to do with what is playing.
        private string PaletteArt()
        {
            string disc = _settings?.Current?.NowPlayingAlbumArtPath;
            if (string.IsNullOrEmpty(disc)) disc = null;
            if (IsStrip || (_settings?.Current?.SpotifyActive == true && disc != null)) return disc;
            string source = _tagPalette;
            if ("Disc".Equals(source, StringComparison.OrdinalIgnoreCase)) return disc ?? _gameBackground;
            if ("Cover".Equals(source, StringComparison.OrdinalIgnoreCase)) return _gameCover ?? _gameBackground ?? disc;
            return _gameBackground ?? _gameCover ?? disc;
        }

        // When the picture changes, its three colours go to the renderer - taken off the UI thread, sent only if this
        // renderer is still the one running and the art hasn't changed again. No art, or none usable: the style's own palette.
        private void PublishCover()
        {
            string art = PaletteArt();
            if (_coverSent && art == _coverFor) return;
            _coverSent = true;
            _coverFor = art;
            var feed = _feed;
            if (feed == null) return;
            if (string.IsNullOrEmpty(art) || !File.Exists(art)) { feed.PublishCover(false, default(Color), default(Color), default(Color)); return; }
            Task.Run(() =>
            {
                Color a = default(Color), b = default(Color), c = default(Color);
                bool on = false, analysed = false;
                try
                {
                    if (IsStrip)
                    {
                        // the strip's two colours (VisualizerStripColors), blended up each bar (UPS - Seek Spectrum.vis);
                        // it reads the first two as sent (uCoverA, uCoverB)
                        var (foot, top) = VisualizerStripColors.FromFile(art);
                        a = foot; b = top; c = top;
                    }
                    else
                    {
                        var cols = VisualizerCoverPalette.Get(art, Path.Combine(_otherMusicFolder, "palettes.json"), out analysed);
                        a = cols[0]; b = cols[1]; c = cols[2];
                    }
                    on = true;
                }
                catch (Exception ex) { _log?.Debug($"[Visualizer] Cover colours skipped: {ex.Message}"); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_feed != feed || _coverFor != art) return;
                    feed.PublishCover(on, a, b, c);
                    _log?.Debug(on ? $"[Visualizer] Cover palette #{a.R:X2}{a.G:X2}{a.B:X2} / #{b.R:X2}{b.G:X2}{b.B:X2} / #{c.R:X2}{c.G:X2}{c.B:X2} from {(art == _gameBackground ? "background" : art == _gameCover ? "cover" : "disc art")} {Path.GetFileName(art)} ({(analysed ? "analysed" : "stored")})" : "[Visualizer] Cover palette: none (the style's own)");
                }));
            });
        }

        // A point in v's units, in device pixels: DPI and every transform above v. TransformToDevice alone is only the DPI,
        // and Playnite's Fullscreen window shows its whole view through a Viewbox (laid out 1080 units tall, stretched to
        // the screen) - sizing by DPI alone drew a 2560 px frame into 2133 px on a 2880x1800 screen at 200%.
        private static Point ToDevice(Visual v, Point p)
        {
            var source = PresentationSource.FromVisual(v);
            if (source?.RootVisual == null) return p;
            return source.CompositionTarget.TransformToDevice.Transform(v.TransformToAncestor(source.RootVisual).Transform(p));
        }

        private void Stop()
        {
            if (_owner == this) StopRenderer();
            if (HandsBack(IsStrip, _owner != null && _owner != this, IsVisible && Enabled)) HandBack();
        }

        // Starting failed: stop, and a strip gives the renderer back (it won't draw, so the page should).
        private void Fail()
        {
            Stop();
            if (IsStrip) HandBack();
        }

        private void StopRenderer()
        {
            _generation++;
            SetValue(IsRunningKey, false);
            SetValue(SongStateKey, 0);
            CompositionTarget.Rendering -= OnRendering;
            if (_d3d != null)
            {
                _d3d.Lock();
                _d3d.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
                _d3d.Unlock();
                _d3d = null;
                _image.Source = null;
            }
            vt_shutdown();
            if (_view != null) { _view.Write(OffStop, 1); SetEvent(_tick); }
            // Our handle to the tick event; the renderer holds its own, so closing ours never strands it.
            if (_tick != IntPtr.Zero) { CloseHandle(_tick); _tick = IntPtr.Zero; }
            var renderer = _renderer;
            _rendererExited = Task.Run(() =>
            {
                try { if (renderer != null && !renderer.WaitForExit(3000)) renderer.Kill(); } catch { }
                renderer?.Dispose();
            });
            _renderer = null;
            _view?.Dispose(); _view = null;
            _mmf?.Dispose(); _mmf = null;
            _songAnalysis?.Dispose(); _songAnalysis = null;
            _feed?.Dispose(); _feed = null;
            _owner = null;
        }

        // Lends the renderer to a strip: stop, but keep the last frame up (a still picture) until starting again.
        private void Suspend()
        {
            BitmapSource still = null;
            if (_d3d != null && _w > 0 && _h > 0)
            {
                try
                {
                    var rtb = new RenderTargetBitmap(_w, _h, 96 * _deviceScale.X, 96 * _deviceScale.Y, PixelFormats.Pbgra32);
                    rtb.Render(_image);
                    rtb.Freeze();
                    still = rtb;
                }
                catch (Exception ex) { _log?.Debug($"[Visualizer] Could not keep the last frame: {ex.Message}"); }
            }
            Stop();
            _image.Source = still;
            _log?.Debug("[Visualizer] Lent to the music player's strip");
        }
    }

    // The x86 bridge DLL (Visualizer\UniPlaySongVisualizerBridge.dll) that opens the renderer's shared D3D11 textures
    // through D3D9Ex for D3DImage, and the frame header layout (native/visualizer/visualizer_shared.h, VisFrames).
    internal static class VisualizerBridge
    {
        private const string Dll = "UniPlaySongVisualizerBridge.dll";
        private static bool _loaded;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr CreateEventW(IntPtr a, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] internal static extern bool SetEvent(IntPtr h);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int vt_init(IntPtr hwnd);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int vt_open(int index, ulong handle, int w, int h);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr vt_backbuffer(int w, int h);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int vt_copy(int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void vt_shutdown();

        internal const int OffMagic = 0, OffWidth = 4, OffHeight = 8, OffHandles = 16, OffLatest = 40, OffSongState = 44, OffFrame = 48, OffStop = 80, OffTransport = 84;
        internal const uint FramesMagic = 0x31585456u; // "VTX1"

        // DllImport by name binds to an already-loaded module; the bridge lives in the Visualizer subfolder, which the
        // loader doesn't search, so load it by full path first.
        internal static void Load(string folder)
        {
            if (_loaded) return;
            if (LoadLibraryW(Path.Combine(folder, Dll)) == IntPtr.Zero)
                throw new DllNotFoundException($"{Dll} (error {Marshal.GetLastWin32Error()})");
            _loaded = true;
        }
    }
}
