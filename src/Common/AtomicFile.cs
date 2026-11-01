using System.IO;

namespace UniPlaySong.Common
{
    // Writes through a .tmp file and swaps it in, so a crash mid-write leaves the previous file instead of a truncated
    // one. Not a true atomic replace - there is a brief window between the delete and the move. Callers keep their own
    // error handling.
    internal static class AtomicFile
    {
        public static void WriteAllText(string path, string contents)
        {
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, contents);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tempPath, path);
        }
    }
}
