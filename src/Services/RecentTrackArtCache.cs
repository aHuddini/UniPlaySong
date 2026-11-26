using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Thumbnails for the Recently played list, so its rows show their art after a restart without the songs playing
    // again (RecentTrack.Art is otherwise this session's only). For the list and nothing else: only songs still in it
    // are kept (Prune), so the cache never holds more than RecentTracks.Max small files.
    //
    // Game songs and other music (Custom Folders, music from elsewhere) are separate caches:
    // <root>\RecentTrackArt\Games and \OtherMusic, each file named by a hash of the song's path. A thumbnail older than
    // its song file is stale (the file was replaced) and is neither shown nor kept.
    public class RecentTrackArtCache
    {
        private readonly string _gamesFolder;
        private readonly string _otherFolder;
        private readonly FileLogger _log;

        // rootDirectory is ExtraMetadata\UniPlaySong
        public RecentTrackArtCache(string rootDirectory, FileLogger log = null)
        {
            var root = Path.Combine(rootDirectory ?? string.Empty, Constants.RecentTrackArtFolderName);
            _gamesFolder = Path.Combine(root, Constants.GamesFolderName);
            _otherFolder = Path.Combine(root, Constants.RecentTrackArtOtherFolderName);
            _log = log;
        }

        // A UPS game song lives in Games\{GameId}\
        internal static bool IsGameSong(string songPath)
        {
            var dir = Path.GetDirectoryName(songPath) ?? string.Empty;
            var parent = Path.GetFileName(Path.GetDirectoryName(dir) ?? string.Empty);
            return Guid.TryParse(Path.GetFileName(dir), out _) && parent.Equals(Constants.GamesFolderName, StringComparison.OrdinalIgnoreCase);
        }

        internal string PathFor(string songPath)
        {
            string name;
            using (var sha = SHA1.Create())
                name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(songPath.ToLowerInvariant()))).Replace("-", string.Empty);
            return Path.Combine(IsGameSong(songPath) ? _gamesFolder : _otherFolder, name.ToLowerInvariant() + ".png");
        }

        // Off the UI thread; the thumbnail must be frozen. Nothing written when a current one is already there.
        public void Save(string songPath, ImageSource thumb)
        {
            if (!(thumb is BitmapSource bmp) || string.IsNullOrEmpty(songPath)) return;
            try
            {
                var file = PathFor(songPath);
                if (IsCurrent(file, songPath)) return;
                var encoder = new PngBitmapEncoder();   // lossless: no artefacts on small art
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllBytes(file, ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                _log?.Debug($"[RecentTrackArt] Not saved for '{songPath}': {ex.Message}");
            }
        }

        // The song's thumbnail, or null (none kept, stale, or the key is not a song path)
        public ImageSource Load(string songPath)
        {
            try
            {
                var file = PathFor(songPath);
                return IsCurrent(file, songPath) ? RecentTracks.Thumb(file) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Keeps only the listed songs' thumbnails
        public void Prune(IEnumerable<string> songPathsInList)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var song in songPathsInList ?? Enumerable.Empty<string>())
            {
                try { keep.Add(PathFor(song)); }
                catch (Exception) { }   // an old row's key that is not a path
            }
            foreach (var folder in new[] { _gamesFolder, _otherFolder })
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (var file in Directory.GetFiles(folder, "*.png"))
                        if (!keep.Contains(file)) File.Delete(file);
                }
                catch (Exception ex)
                {
                    _log?.Debug($"[RecentTrackArt] Prune of '{folder}' incomplete: {ex.Message}");
                }
            }
        }

        private static bool IsCurrent(string file, string songPath) =>
            File.Exists(file) && File.Exists(songPath) && File.GetLastWriteTimeUtc(file) >= File.GetLastWriteTimeUtc(songPath);
    }
}
