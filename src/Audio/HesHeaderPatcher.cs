using System;
using System.IO;

namespace UniPlaySong.Audio
{
    // Generates per-track mini-HES files that each select a single song from
    // the original multi-track HES by patching byte 5 (first_track) in the
    // 16-byte header tag block. The HES format has no total_songs field —
    // first_track alone determines which song dispatches, so a single-byte
    // patch is sufficient. The full code blob and bank data are preserved
    // so the game's player code can still decode the requested track.
    //
    // Mirrors the pattern used by NsfHeaderPatcher (which patches NSF byte 7,
    // starting_song). HES dispatch on first_track is verified against the
    // GME source: gme/Hes_Emu.h declares the header struct as
    //   { tag[4], vers, first_track, init_addr[2], banks[8], data_tag[4],
    //     size[4], addr[4], unused[4] }
    public static class HesHeaderPatcher
    {
        // HES magic: "HESM"
        private static readonly byte[] HesMagic = { 0x48, 0x45, 0x53, 0x4D };

        // Minimum header bytes required to identify and patch first_track.
        // The full HES header including bank info and data_tag is longer
        // (~32+ bytes), but we only need to read up to first_track (offset 5)
        // for the magic check + patch.
        private const int MinHeaderSize = 6;
        private const int FirstTrackOffset = 5;

        public static bool IsValidHesHeader(byte[] bytes)
        {
            if (bytes == null || bytes.Length < MinHeaderSize) return false;
            for (int i = 0; i < HesMagic.Length; i++)
            {
                if (bytes[i] != HesMagic[i]) return false;
            }
            return true;
        }

        // Returns the byte stored at first_track offset, or -1 if the file
        // doesn't have a valid HES header. Useful for diagnostics.
        public static int ReadFirstTrack(byte[] bytes)
        {
            if (!IsValidHesHeader(bytes)) return -1;
            return bytes[FirstTrackOffset];
        }

        // Returns a new byte array tagged to play only the requested track index
        // (0-255). Original bytes are not modified. The caller is responsible for
        // confirming the trackIndex actually corresponds to a real track in the
        // source file's M3U sidecar — patching to a non-existent index produces
        // silence (the dispatch jumps into uninitialized memory).
        public static byte[] PatchForTrack(byte[] originalBytes, int trackIndex)
        {
            if (!IsValidHesHeader(originalBytes))
                throw new InvalidDataException("Not a valid HES file (bad magic bytes).");

            if (trackIndex < 0 || trackIndex > 255)
                throw new ArgumentOutOfRangeException(nameof(trackIndex),
                    $"HES track index must be 0-255 (was {trackIndex}).");

            var patched = new byte[originalBytes.Length];
            Buffer.BlockCopy(originalBytes, 0, patched, 0, originalBytes.Length);
            patched[FirstTrackOffset] = (byte)trackIndex;
            return patched;
        }
    }
}
