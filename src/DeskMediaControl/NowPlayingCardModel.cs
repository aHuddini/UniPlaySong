using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.DeskMediaControl
{
    // Everything the Now Playing card can read or do, handed in by the plugin: the card reaches existing services only
    // through these delegates. Changed is raised (on the UI thread) when the song, play state or list changed.
    internal sealed class NowPlayingCardHost
    {
        public Func<(string Title, string Artist, string ArtPath)> Song;
        public Func<ActiveMediaSnapshot> Snapshot;
        public Func<bool> CalmDownOn;
        public Func<(NowPlayingCardContext Context, IReadOnlyList<NowPlayingCardTrack> Rows)> Tracks;
        public Func<bool> CanOpenFolder;
        public Action PlayPause, Next, Previous, ToggleCalmDown, OpenFolder;
        public Action<NowPlayingCardTrack> PlayTrack;
        public Func<string, TimeSpan> SongLength;   // read from the file; called off the UI thread
        public Func<bool> CanSeek;
        public Action<double> SeekTo;               // 0..100 of the song

        public event Action Changed;
        public void RaiseChanged() => Changed?.Invoke();
    }

    internal sealed class NowPlayingCardModel : INotifyPropertyChanged
    {
        private readonly NowPlayingCardHost _host;

        private string _artPath;

        // A song picked on the card: marked at once and held until the player reports it (or a few seconds pass),
        // because the player only switches after fading the old song out, and refreshes in between would move the
        // marker back.
        private string _pickedPath;
        private DateTime _pickedAt;
        private static readonly TimeSpan PickHold = TimeSpan.FromSeconds(4);

        public void MarkPicked(NowPlayingCardTrack track)
        {
            if (track?.Path == null) return;
            _pickedPath = track.Path;
            _pickedAt = DateTime.UtcNow;
            foreach (var row in Tracks) row.IsCurrent = row == track;
            UpdateLive();
        }

        private void ApplyPick(IReadOnlyList<NowPlayingCardTrack> rows)
        {
            if (_pickedPath == null) return;
            bool arrived = false;
            foreach (var row in rows)
                if (row.IsCurrent && string.Equals(row.Path, _pickedPath, StringComparison.OrdinalIgnoreCase)) arrived = true;
            if (arrived || DateTime.UtcNow - _pickedAt > PickHold)
            {
                _pickedPath = null;
                return;
            }
            foreach (var row in rows) row.IsCurrent = string.Equals(row.Path, _pickedPath, StringComparison.OrdinalIgnoreCase);
        }

        public NowPlayingCardModel(NowPlayingCardHost host) => _host = host;

        public string Title { get; private set; }
        public string Artist { get; private set; }
        public ImageSource Banner { get; private set; }
        public Color TintDark { get; private set; }
        public Color TintVivid { get; private set; }
        public string Position { get; private set; }
        public string Duration { get; private set; }
        public double Progress { get; private set; }    // 0..1
        public bool IsPlaying { get; private set; }
        public bool CalmDownOn { get; private set; }
        public string ListHeader { get; private set; }
        public bool HasList { get; private set; }
        public IReadOnlyList<NowPlayingCardTrack> Tracks { get; private set; } = new NowPlayingCardTrack[0];
        public bool CanOpenFolder { get; private set; }
        public bool CanSeek { get; private set; }

        // The card is on screen: only then may the playing row's bars bounce
        private bool _shown;
        public void SetShown(bool shown)
        {
            _shown = shown;
            UpdateLive();
        }

        private void UpdateLive()
        {
            foreach (var row in Tracks) row.IsLive = _shown && IsPlaying && row.IsCurrent;
        }

        // The list folded away (the chevron on the footer's separator); remembered for the session
        private static bool _collapsed;
        public bool Collapsed => _collapsed;
        public static bool StartsCollapsed => _collapsed;
        public bool ShowList => HasList && !_collapsed;

        public void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            Set(nameof(Collapsed)); Set(nameof(ShowList));
        }

        // Song, list and buttons: on open and on every host change.
        public void Refresh()
        {
            var song = _host.Song?.Invoke() ?? default;
            Set(nameof(Title), Title = song.Title ?? string.Empty);
            Set(nameof(Artist), Artist = song.Artist ?? string.Empty);
            // One picture drives the banner: the art faintly, and the tint from its two colours. The host hands the
            // game's own art for game music, so the banner changes with the game, not with each track.
            if (!string.Equals(song.ArtPath, _artPath, StringComparison.OrdinalIgnoreCase))
            {
                _artPath = song.ArtPath;
                Banner = LoadBanner(song.ArtPath);
                Set(nameof(Banner)); // the card fades the new art in
                LoadTint(song.ArtPath);
            }

            var (context, rows) = _host.Tracks?.Invoke() ?? (NowPlayingCardContext.None, new NowPlayingCardTrack[0]);
            ApplyPick(rows);
            if (SameRows(Tracks, rows))
            {
                // Same songs: move the playing marker in place instead of rebuilding every row
                for (int i = 0; i < rows.Count; i++) ((NowPlayingCardTrack)Tracks[i]).IsCurrent = rows[i].IsCurrent;
            }
            else
            {
                Tracks = rows;
                _sizeText = null; // another list: its size is read again
                Set(nameof(Tracks));
            }
            LoadLengths(Tracks);
            UpdateLive();
            HasList = context != NowPlayingCardContext.None;
            _header = Header(context, rows.Count);
            if (context != NowPlayingCardContext.GameSongs) _sizeText = null;
            ShowHeader();
            Set(nameof(HasList)); Set(nameof(ShowList));
            if (context == NowPlayingCardContext.GameSongs) LoadSize(rows);

            CalmDownOn = _host.CalmDownOn?.Invoke() == true;
            CanOpenFolder = _host.CanOpenFolder?.Invoke() == true;
            Set(nameof(CalmDownOn)); Set(nameof(CanOpenFolder));
            Tick();
        }

        // Times, progress and play state: twice a second while the card is open.
        public void Tick()
        {
            var s = _host.Snapshot?.Invoke();
            Position = s?.HasActiveMedia == true ? s.PositionText : string.Empty;
            Duration = s?.HasActiveMedia == true ? s.DurationText : string.Empty;
            Progress = s?.HasActiveMedia == true ? Math.Max(0, Math.Min(1, s.Progress / 100)) : 0;
            IsPlaying = s?.IsPlaying == true;
            CanSeek = s?.HasActiveMedia == true && _host.CanSeek?.Invoke() == true;
            UpdateLive();
            Set(nameof(Position)); Set(nameof(Duration)); Set(nameof(Progress)); Set(nameof(IsPlaying)); Set(nameof(CanSeek));
        }

        // The art's two colours, read off the UI thread (decoding a large background takes a moment); applied only if
        // the art is still the same when they arrive. The card fades to them.
        private void LoadTint(string path)
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            System.Threading.Tasks.Task.Run(() =>
            {
                Color dark = Colors.Black, vivid = Colors.Black;
                try
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        (dark, vivid) = Audio.VisualizerStripColors.FromFile(path);
                }
                catch (Exception) { }
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!string.Equals(path, _artPath, StringComparison.OrdinalIgnoreCase)) return;
                    if (dark == TintDark && vivid == TintVivid) return;
                    TintDark = dark;
                    TintVivid = vivid;
                    Set(nameof(TintVivid));
                }));
            });
        }

        // The art, pre-blurred once and cached (no live WPF effect). Null shows the tint alone.
        private static ImageSource LoadBanner(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return Features.MusicInfoCard.MusicInfoCardHandler.LoadBlurredBackdrop(path).Backdrop;
            }
            catch (Exception) { return null; }
        }

        // "6 songs · 48.2 MB": the game's songs on disk, summed off the UI thread
        private string _header = string.Empty;
        private string _sizeText;

        private void ShowHeader()
        {
            ListHeader = (_sizeText == null ? _header : _header + " · " + _sizeText).ToUpperInvariant();
            Set(nameof(ListHeader));
        }

        private void LoadSize(IReadOnlyList<NowPlayingCardTrack> rows)
        {
            var paths = new List<string>();
            foreach (var row in rows) if (row.Path != null) paths.Add(row.Path);
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            System.Threading.Tasks.Task.Run(() =>
            {
                long bytes = 0;
                foreach (var path in paths)
                {
                    try { bytes += new FileInfo(path).Length; }
                    catch (Exception) { }
                }
                var text = FormatSize(bytes);
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!ReferenceEquals(rows, Tracks) && !SameRows(Tracks, rows)) return; // the list moved on
                    _sizeText = text;
                    ShowHeader();
                }));
            });
        }

        private static string FormatSize(long bytes) =>
            bytes <= 0 ? null
            : bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB"
            : $"{bytes / (double)(1L << 20):0.0} MB";

        // Song lengths, cached per file for the session: known ones show at once, the rest are read off the UI thread
        // and fill in on rows still showing.
        private static readonly Dictionary<string, string> LengthCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object LengthLock = new object();

        private void LoadLengths(IReadOnlyList<NowPlayingCardTrack> rows)
        {
            var missing = new List<string>();
            lock (LengthLock)
            {
                foreach (var row in rows)
                {
                    if (row.Path == null || row.Length.Length > 0) continue;
                    if (LengthCache.TryGetValue(row.Path, out var known)) row.Length = known;
                    else missing.Add(row.Path);
                }
            }
            var read = _host.SongLength;
            if (missing.Count == 0 || read == null) return;

            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var path in missing)
                {
                    string text;
                    try { text = Format(read(path)); }
                    catch (Exception) { text = string.Empty; }
                    lock (LengthLock) LengthCache[path] = text;
                }
                dispatcher.BeginInvoke(new Action(() =>
                {
                    lock (LengthLock)
                        foreach (var row in Tracks)
                            if (row.Path != null && LengthCache.TryGetValue(row.Path, out var text)) row.Length = text;
                }));
            });
        }

        private static string Format(TimeSpan length) =>
            length <= TimeSpan.Zero ? string.Empty
            : length.TotalHours >= 1 ? length.ToString(@"h\:mm\:ss") : length.ToString(@"m\:ss");

        private static bool SameRows(IReadOnlyList<NowPlayingCardTrack> current, IReadOnlyList<NowPlayingCardTrack> next)
        {
            if (current == null || next == null || current.Count != next.Count) return false;
            for (int i = 0; i < next.Count; i++)
                if (!current[i].SameRow(next[i])) return false;
            return true;
        }

        private static string Header(NowPlayingCardContext context, int count)
        {
            switch (context)
            {
                case NowPlayingCardContext.GameSongs: return count == 1 ? "1 song" : $"{count} songs";
                case NowPlayingCardContext.RadioUpNext: return "Up next · Radio";
                case NowPlayingCardContext.SpotifyRecent: return "Recently played";
                default: return string.Empty;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Set([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private void Set(string name, object _) => Set(name);
    }
}
