using System;
using System.IO;

namespace UniPlaySong.Audio
{
    // Generates per-track mini-NSFs that each select a single song from the
    // original multi-track NSF by patching byte 7 (starting_song) in the
    // 128-byte header. total_songs is LEFT UNCHANGED so the 6502 program
    // code blob's internal track indices remain valid — GME still sees N
    // tracks, but GmeReader picks the one identified by starting_song.
    internal static class NsfHeaderPatcher
    {
        // NSF magic: "NESM" + 0x1A
        private static readonly byte[] NsfMagic = { 0x4E, 0x45, 0x53, 0x4D, 0x1A };

        private const int HeaderSize = 128;
        private const int TotalSongsOffset = 6;
        private const int StartingSongOffset = 7;

        public static bool IsValidNsfHeader(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HeaderSize) return false;
            for (int i = 0; i < NsfMagic.Length; i++)
            {
                if (bytes[i] != NsfMagic[i]) return false;
            }
            return true;
        }

        public static int ReadTotalSongs(byte[] bytes)
        {
            if (!IsValidNsfHeader(bytes)) return 0;
            return bytes[TotalSongsOffset];
        }

        // Returns a new byte array tagged to play only the requested track.
        // total_songs is preserved so GME's track index space stays valid;
        // only starting_song is changed. Original bytes are not modified.
        public static byte[] PatchForTrack(byte[] originalBytes, int trackIndex0Based)
        {
            if (!IsValidNsfHeader(originalBytes))
                throw new InvalidDataException("Not a valid NSF file (bad magic bytes).");

            int totalSongs = originalBytes[TotalSongsOffset];
            if (trackIndex0Based < 0 || trackIndex0Based >= totalSongs)
                throw new ArgumentOutOfRangeException(nameof(trackIndex0Based),
                    $"Track index {trackIndex0Based} out of range (file has {totalSongs} tracks).");

            var patched = new byte[originalBytes.Length];
            Buffer.BlockCopy(originalBytes, 0, patched, 0, originalBytes.Length);
            patched[StartingSongOffset] = (byte)(trackIndex0Based + 1);
            return patched;
        }
    }
}
