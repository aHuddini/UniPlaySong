using System;
using System.IO;
using System.Threading.Tasks;

namespace UniPlaySong.Services
{
    // Moves a game's song to the Recycle Bin (the Now Playing card's "Move to Recycle Bin"), so it can be restored. The
    // player holds the file it plays open, so the playing song is let go first (the caller skips or stops), then the file
    // moves as soon as nothing holds it. Nothing here knows about games or the card.
    internal static class SongRecycler
    {
        private const int WaitStepMs = 150, WaitSteps = 40; // up to 6 s: room for a fade-out or a crossfade

        // True once the file is gone from its folder. letGo runs first when the song is the one playing.
        public static async Task<bool> RecycleAsync(string path, bool isPlaying, Action letGo, Action<string> log)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            if (isPlaying) letGo?.Invoke();
            for (int i = 0; i < WaitSteps && IsHeld(path); i++) await Task.Delay(WaitStepMs);
            try
            {
                // Windows' own recycle (the shell file operation): no confirmation; only an unexpected error shows a dialog
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Recycle failed for {Path.GetFileName(path)}: {ex.Message}");
            }
            return !File.Exists(path);
        }

        // Something still has the file open (the player, mid fade-out)
        private static bool IsHeld(string path)
        {
            try { using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return false; }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return false; } // read-only: the move itself reports it
        }
    }
}
