using System;
using System.IO;
using System.Security.Cryptography;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Writes the current track's album art to a file, so it can be exposed to themes as a path string
    // (UniPlaySongSettings.NowPlayingAlbumArtPath).
    //
    // Files are named by the SHA-1 of their bytes, in a folder that holds nothing else:
    //
    //   - A theme's <Image Source="{PluginSettings ... Path=NowPlayingAlbumArtPath}"/> holds the file open
    //     while it is displayed, and keeps holding it after the binding moves on until a GC finalizes the
    //     decoder. A delete issued at the moment the next track is written therefore always fails.
    //   - WPF caches decoded images by URI, so a path must never be reused for different bytes, and a
    //     cached file cannot be overwritten either.
    //
    // A content-addressed name satisfies both: new art gets a new path (bindings update), the same art gets
    // the same path with nothing written (cache hits are correct by construction), and an existing file is
    // never touched. Old files are removed by Sweep(), which spares the current and previous paths and
    // simply retries anything still locked on the next call.
    //
    // All operations are fail-safe (never throw); on a write failure the returned path is "" and the caller
    // treats the track as having no art.
    public class NowPlayingArtWriter
    {
        // Anything written more recently than this is left alone by Sweep(): it may be a write in flight on
        // another thread, or a file just moved into place and about to become current. UPS and Spotify
        // publish from different threads, so this is the one race the folder has.
        private static readonly TimeSpan RecentWriteGrace = TimeSpan.FromMinutes(1);

        private readonly FileLogger _fileLogger;
        private readonly string _rootDirectory;
        private readonly string _artDirectory;
        private readonly string _legacyPrefix;
        private readonly object _lock = new object();
        private string _current;
        private string _previous;

        // The most recently written art file path, or "" if the current track has none.
        public string ArtFilePath
        {
            get { lock (_lock) return _current ?? string.Empty; }
        }

        public string ArtDirectory => _artDirectory;

        // rootDirectory is ExtraMetadata\UniPlaySong. Art goes into its NowPlayingArt subfolder; the root is
        // only ever searched for the files earlier versions left there.
        public NowPlayingArtWriter(string rootDirectory, FileLogger fileLogger = null)
        {
            _fileLogger = fileLogger;
            _rootDirectory = rootDirectory ?? string.Empty;
            _artDirectory = Path.Combine(_rootDirectory, Constants.NowPlayingArtFolderName);
            _legacyPrefix = Path.GetFileNameWithoutExtension(Constants.LegacyNowPlayingArtFileName) + "_";
        }

        // Write raw image bytes (e.g. a Spotify SMTC thumbnail). Returns the file path, or "" on null/empty
        // input or any IO failure. Identical bytes return the existing file without writing.
        public string WriteBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            try
            {
                var path = Path.Combine(_artDirectory, Hash(bytes) + ".png");

                // Same art as a file already on disk: claim it as current under the lock Sweep() deletes
                // under, so a sweep on another thread cannot remove it between the check and the claim.
                lock (_lock)
                {
                    if (File.Exists(path))
                    {
                        MarkCurrentLocked(path);
                        return path;
                    }
                }

                // Written under a unique temp name, then moved into place: a concurrent writer of the same
                // art (UPS and Spotify publish from different threads) loses the move and simply uses the
                // winner's identical file. Nobody can observe a half-written .png. A freshly moved file is
                // inside Sweep()'s recent-write grace, so it survives until it is claimed below.
                Directory.CreateDirectory(_artDirectory);
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, bytes);
                try
                {
                    File.Move(temp, path);
                }
                catch (IOException) when (File.Exists(path))
                {
                    TryDelete(temp);
                }

                lock (_lock) MarkCurrentLocked(path);
                return path;
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[NowPlaying] WriteBytes failed: {ex.Message}");
                return string.Empty;
            }
        }

        // Re-claims an art file this writer returned earlier (the publisher remembers the art per song). Returns
        // the path if it is still on disk, or "" if a sweep has removed it and the art must be written again.
        public string Reuse(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            lock (_lock)
            {
                if (!File.Exists(path)) return string.Empty;
                MarkCurrentLocked(path);
                return path;
            }
        }

        // Extract the first embedded picture from an audio file's tags (ID3/FLAC/OGG via TagLib#)
        // and write it. Returns the path or "" if the file has no picture / fails.
        public string WriteFromAudioFile(string audioFilePath)
        {
            if (string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath)) return string.Empty;
            try
            {
                using (var tag = TagLib.File.Create(audioFilePath))
                {
                    var pics = tag.Tag?.Pictures;
                    if (pics == null || pics.Length == 0) return string.Empty;
                    var data = pics[0].Data?.Data;
                    return WriteBytes(data);
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[NowPlaying] WriteFromAudioFile failed: {ex.Message}");
                return string.Empty;
            }
        }

        // The current track has no written art (nothing playing, or the publisher points at a game cover
        // instead). Nothing is deleted here: the file that was showing may still be displayed and locked,
        // so it becomes "previous" and is left for a later Sweep().
        public void Clear()
        {
            lock (_lock)
            {
                if (_current != null)
                {
                    _previous = _current;
                    _current = null;
                }
            }
        }

        // Deletes every art file except the current and previous ones. A file still held open by a theme is
        // skipped and retried on the next call, so the folder stays at a handful of files instead of growing.
        // Returns the number deleted.
        public int Sweep()
        {
            int deleted = 0;
            try
            {
                if (!Directory.Exists(_artDirectory)) return 0;
                foreach (var file in Directory.GetFiles(_artDirectory))
                {
                    // Per file, under the lock WriteBytes claims under: current/previous are read fresh,
                    // so a file claimed a moment ago is never the one deleted.
                    lock (_lock)
                    {
                        if (SamePath(file, _current) || SamePath(file, _previous)) continue;
                        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < RecentWriteGrace) continue;
                        if (TryDelete(file)) deleted++;
                    }
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[NowPlaying] Sweep failed: {ex.Message}");
            }
            return deleted;
        }

        // Deletes the nowplaying_art_<n>.png files earlier versions wrote into the root and could not remove.
        // Matches that exact shape (prefix, digits, .png) in the root only, never recursing, so the Games
        // music folders and every other side file are out of reach. Returns the number deleted.
        public int SweepLegacy()
        {
            int deleted = 0;
            try
            {
                if (!Directory.Exists(_rootDirectory)) return 0;
                foreach (var file in Directory.EnumerateFiles(_rootDirectory, _legacyPrefix + "*", SearchOption.TopDirectoryOnly))
                {
                    if (!IsLegacyArtName(Path.GetFileName(file))) continue;
                    if (TryDelete(file)) deleted++;
                }
                if (deleted > 0)
                    _fileLogger?.Info($"[NowPlaying] Removed {deleted} leftover art file(s) from {_rootDirectory}");
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[NowPlaying] SweepLegacy failed: {ex.Message}");
            }
            return deleted;
        }

        private bool IsLegacyArtName(string name)
        {
            if (!name.StartsWith(_legacyPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
            var middle = name.Substring(_legacyPrefix.Length, name.Length - _legacyPrefix.Length - ".png".Length);
            if (middle.Length == 0) return false;
            foreach (var c in middle)
                if (c < '0' || c > '9') return false;
            return true;
        }

        // Caller holds _lock.
        private void MarkCurrentLocked(string path)
        {
            if (SamePath(path, _current)) return;
            if (_current != null) _previous = _current;
            _current = path;
        }

        private static bool SamePath(string a, string b)
        {
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryDelete(string file)
        {
            try
            {
                File.Delete(file);
                return true;
            }
            catch
            {
                return false; // held open by a theme, or already gone: the next sweep retries
            }
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA1.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
