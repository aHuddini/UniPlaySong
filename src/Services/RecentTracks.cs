using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // One track heard. Album is the game's name for game music. Art is the cover as it played, this session only: the
    // file keeps text.
    public class RecentTrack
    {
        public string Title { get; }
        public string Artist { get; }
        public string Album { get; }
        public DateTime PlayedAt { get; }
        public ImageSource Art { get; }
        internal string Key { get; }   // the same track: Spotify's title/artist/album, a game song's file path

        public RecentTrack(string title, string artist, string album, DateTime playedAt, string key, ImageSource art = null)
        {
            Title = title ?? string.Empty;
            Artist = artist ?? string.Empty;
            Album = album ?? string.Empty;
            PlayedAt = playedAt;
            Key = key ?? string.Empty;
            Art = art;
        }
    }

    // The tracks heard recently, newest first - Spotify's and game music's, each its own list and file in UPS's data
    // folder (a line per track, oldest first, tab-separated: time, title, artist, album, key), so they outlive a restart.
    // Themes bind them through UniPlaySongSettings (SpotifyRecentTracks, GameRecentTracks) or place the UPS elements.
    // One list each for the session, not one per settings object: a settings save swaps the object, and the lists must
    // outlive that. Touched only on the UI thread.
    public class RecentTracks
    {
        public const int Max = 50;

        public static RecentTracks Spotify { get; } = new RecentTracks("SpotifyRecentTracks.txt");
        public static RecentTracks Games { get; } = new RecentTracks("GameRecentTracks.txt");

        public string FileName { get; }
        public ObservableCollection<RecentTrack> Items { get; } = new ObservableCollection<RecentTrack>();

        private string _file;
        private FileLogger _log;

        public RecentTracks(string fileName) { FileName = fileName; }

        public void Load(string dataFolder, FileLogger log = null)
        {
            _log = log;
            _file = Path.Combine(dataFolder, FileName);
            Items.Clear();
            try
            {
                if (!File.Exists(_file)) return;
                var lines = File.ReadAllLines(_file).Where(l => l.Length > 0).ToList();
                for (int i = lines.Count - 1; i >= 0 && Items.Count < Max; i--)
                {
                    var f = lines[i].Split('\t');
                    if (f.Length < 4 || !DateTime.TryParse(f[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)) continue;
                    Items.Add(new RecentTrack(f[1], f[2], f[3], at, f.Length > 4 ? f[4] : f[1] + "\n" + f[2] + "\n" + f[3]));
                }
            }
            catch (Exception ex)
            {
                _log?.Debug($"[RecentTracks] {FileName} not read: {ex.Message}");
            }
        }

        // A new track goes on top. The same track again on top (key) updates that row instead: Spotify paused and came
        // back, or a game song reported again with its tags read (first by file name, then by its title)
        public void Add(string title, string artist, string album, string key, ImageSource art)
        {
            if (string.IsNullOrEmpty(title)) return;
            if (Items.Count > 0 && Items[0].Key == key)
            {
                var top = Items[0];
                Items[0] = new RecentTrack(title, artist, album, top.PlayedAt, key, art ?? top.Art);
            }
            else
            {
                Items.Insert(0, new RecentTrack(title, artist, album, DateTime.Now, key, art));
                while (Items.Count > Max) Items.RemoveAt(Items.Count - 1);
            }
            if (_file == null) return;
            try
            {
                File.WriteAllLines(_file, Items.Reverse().Select(t =>
                    string.Join("\t", t.PlayedAt.ToString("o"), Clean(t.Title), Clean(t.Artist), Clean(t.Album), Clean(t.Key))));
            }
            catch (Exception ex)
            {
                _log?.Debug($"[RecentTracks] {FileName} not saved: {ex.Message}");
            }
        }

        private static string Clean(string s) => (s ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        // Small and frozen, held in memory: no file to lock or be swept away. Either is fine off the UI thread.
        public static ImageSource Thumb(byte[] bytes) =>
            bytes == null || bytes.Length == 0 ? null : Decode(bmp => bmp.StreamSource = new MemoryStream(bytes));

        public static ImageSource Thumb(string path) =>
            string.IsNullOrEmpty(path) || !File.Exists(path) ? null : Decode(bmp =>
            {
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            });

        private static ImageSource Decode(Action<BitmapImage> source)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                source(bmp);
                bmp.DecodePixelWidth = 96;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}
