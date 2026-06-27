using System;
using System.IO;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Writes the current track's album art to a single known file, so it can be exposed to
    // themes as a path string (UniPlaySongSettings.NowPlayingAlbumArtPath). One responsibility:
    // produce/clear the art file. All operations are fail-safe (never throw); on any failure the
    // returned path is "" and the caller treats the track as having no art.
    public class NowPlayingArtWriter
    {
        private readonly FileLogger _fileLogger;

        public string ArtFilePath { get; }

        public NowPlayingArtWriter(string artDirectory, FileLogger fileLogger = null)
        {
            _fileLogger = fileLogger;
            ArtFilePath = Path.Combine(artDirectory ?? string.Empty, Constants.NowPlayingArtFileName);
        }

        // Write raw image bytes (e.g. a Spotify SMTC thumbnail) to the art file. Returns the file
        // path on success, "" on null/empty input or any IO failure. Atomic: writes a temp file
        // then replaces, so a theme never reads a half-written file.
        public string WriteBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ArtFilePath));
                var tmp = ArtFilePath + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                // Replace atomically. File.Copy(overwrite) + delete tmp keeps it simple and works
                // even when the destination is locked briefly by a theme image load.
                if (File.Exists(ArtFilePath)) File.Delete(ArtFilePath);
                File.Move(tmp, ArtFilePath);
                return ArtFilePath;
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[NowPlaying] WriteBytes failed: {ex.Message}");
                return string.Empty;
            }
        }

        // Extract the first embedded picture from an audio file's tags (ID3/FLAC/OGG via TagLib#)
        // and write it to the art file. Returns the path or "" if the file has no picture / fails.
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

        // Best-effort delete of the art file (when nothing is playing / track has no art).
        public void Clear()
        {
            try { if (File.Exists(ArtFilePath)) File.Delete(ArtFilePath); }
            catch (Exception ex) { _fileLogger?.Debug($"[NowPlaying] Clear failed: {ex.Message}"); }
        }
    }
}
