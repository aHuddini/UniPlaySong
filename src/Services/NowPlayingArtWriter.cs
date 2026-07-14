using System;
using System.IO;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Writes the current track's album art to a file, so it can be exposed to themes as a path
    // string (UniPlaySongSettings.NowPlayingAlbumArtPath). Each write produces a UNIQUE file path
    // (rolling counter) and deletes the previously-written file, so the path string genuinely
    // changes per track. This defeats WPF's BitmapImage URI cache: a theme's
    // <Image Source="{PluginSettings ... Path=NowPlayingAlbumArtPath}"/> would otherwise serve the
    // first track's art forever when the file content changes but the path string does not.
    // All operations are fail-safe (never throw); on any failure the returned path is "" and the
    // caller treats the track as having no art.
    public class NowPlayingArtWriter
    {
        private readonly FileLogger _fileLogger;
        private readonly string _artDirectory;
        private readonly string _filePrefix;
        private long _seq;
        private string _lastWritten;

        // The most-recently-written art file path, or "" if nothing is currently on disk.
        public string ArtFilePath => _lastWritten ?? string.Empty;

        public NowPlayingArtWriter(string artDirectory, FileLogger fileLogger = null)
        {
            _fileLogger = fileLogger;
            _artDirectory = artDirectory ?? string.Empty;
            // Derive the filename prefix from the constant (e.g. "nowplaying_art") so files are
            // nowplaying_art_1.png, nowplaying_art_2.png, ...
            _filePrefix = Path.GetFileNameWithoutExtension(Constants.NowPlayingArtFileName);
        }

        // Write raw image bytes (e.g. a Spotify SMTC thumbnail) to a fresh, unique file. Returns the
        // new file path on success, "" on null/empty input or any IO failure. On success the OLD
        // file is best-effort deleted (so art files don't accumulate). Each path is unique, so no
        // theme can be mid-read on the new path — a plain write is safe (no temp+move needed).
        public string WriteBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            try
            {
                Directory.CreateDirectory(_artDirectory);
                var newPath = Path.Combine(_artDirectory, $"{_filePrefix}_{++_seq}.png");
                File.WriteAllBytes(newPath, bytes);
                // Delete the previous file now that the new one is safely on disk.
                if (!string.IsNullOrEmpty(_lastWritten) && _lastWritten != newPath)
                {
                    try { if (File.Exists(_lastWritten)) File.Delete(_lastWritten); }
                    catch { /* best-effort; a theme may still hold the old file briefly */ }
                }
                _lastWritten = newPath;
                return newPath;
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[NowPlaying] WriteBytes failed: {ex.Message}");
                return string.Empty;
            }
        }

        // Extract the first embedded picture from an audio file's tags (ID3/FLAC/OGG via TagLib#)
        // and write it to a fresh art file. Returns the path or "" if the file has no picture / fails.
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

        // Best-effort delete of the current art file (when nothing is playing / track has no art).
        public void Clear()
        {
            try
            {
                if (!string.IsNullOrEmpty(_lastWritten) && File.Exists(_lastWritten)) File.Delete(_lastWritten);
            }
            catch (Exception ex) { _fileLogger?.Debug($"[NowPlaying] Clear failed: {ex.Message}"); }
            _lastWritten = null;
        }
    }
}
