using Playnite.SDK.Models;

namespace UniPlaySong.Services
{
    // Extracts and caches the audio track of a game's EML video trailer so UPS can
    // play it as default music for games that have no UPS music of their own.
    public interface ITrailerAudioService
    {
        // Returns a playable path to the game's extracted trailer audio, extracting
        // and caching on first call. Returns null if there is no full trailer, FFmpeg
        // is unavailable, or extraction fails (caller stays silent).
        string GetOrExtractAudio(Game game);

        // Deterministic cache path for this game's extracted audio (no I/O, no extraction).
        // Used by IsDefaultMusicPath to recognize the cached file as default music.
        string GetCachedPath(Game game);

        // Deletes all cached trailer audio. Returns (filesDeleted, bytesFreed).
        (int filesDeleted, long bytesFreed) ClearCache();
    }
}
