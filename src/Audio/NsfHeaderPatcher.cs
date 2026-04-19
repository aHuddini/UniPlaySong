using System;
using System.IO;

namespace UniPlaySong.Audio
{
    // Splits a multi-track NSF into valid single-track mini-NSFs by patching
    // bytes 6 (total_songs) and 7 (starting_song) in the 128-byte NSF header.
    // The 6502 program code blob (offset 0x80+) is shared verbatim across all
    // mini-NSFs — only the header differs.
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

        // Returns a new byte array containing a single-track NSF for the given
        // 0-based track index. Original bytes are not modified.
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
            patched[TotalSongsOffset] = 1;
            patched[StartingSongOffset] = (byte)(trackIndex0Based + 1);
            return patched;
        }
    }
}
