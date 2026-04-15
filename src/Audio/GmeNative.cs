using System;
using System.Runtime.InteropServices;

namespace UniPlaySong.Audio
{
    // P/Invoke declarations for Game Music Emu (libgme).
    // gme.dll must be in the plugin directory alongside UniPlaySong.dll.
    internal static class GmeNative
    {
        private const string DLL = "gme";

        // Open a music file and create an emulator at the given sample rate.
        // Returns error string or IntPtr.Zero on success.
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern IntPtr gme_open_file(string path, out IntPtr emuOut, int sampleRate);

        // Free emulator
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void gme_delete(IntPtr emu);

        // Number of tracks in the file
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int gme_track_count(IntPtr emu);

        // Start playing a track (0-based index)
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gme_start_track(IntPtr emu, int index);

        // Generate count 16-bit stereo samples into buffer.
        // count is the number of short values (samples * 2 for stereo).
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gme_play(IntPtr emu, int count, short[] outBuf);

        // Current playback position in milliseconds
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int gme_tell(IntPtr emu);

        // Seek to position in milliseconds
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gme_seek(IntPtr emu, int msec);

        // Check if current track has ended
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int gme_track_ended(IntPtr emu);

        // Schedule fade-out starting at the given millisecond
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void gme_set_fade(IntPtr emu, int startMsec);

        // Get track metadata. Caller must free with gme_free_info().
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gme_track_info(IntPtr emu, out IntPtr infoOut, int track);

        // Free track info returned by gme_track_info
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void gme_free_info(IntPtr info);

        // Returns error string from IntPtr, or null if success
        public static string GetError(IntPtr errPtr)
        {
            return errPtr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(errPtr);
        }

        // Read play_length from gme_info_t struct (offset: 3 ints = 12 bytes)
        public static int GetPlayLength(IntPtr infoPtr)
        {
            // gme_info_t layout: length(4), intro_length(4), loop_length(4), play_length(4)
            return Marshal.ReadInt32(infoPtr, 12);
        }

        // Read metadata strings from gme_info_t
        public static string GetInfoString(IntPtr infoPtr, int pointerIndex)
        {
            // After 16 ints (5 known + 11 reserved = 64 bytes), string pointers begin
            int offset = 64 + pointerIndex * IntPtr.Size;
            IntPtr strPtr = Marshal.ReadIntPtr(infoPtr, offset);
            if (strPtr == IntPtr.Zero) return string.Empty;
            string val = Marshal.PtrToStringAnsi(strPtr);
            return val ?? string.Empty;
        }

        // String pointer indices in gme_info_t (after the 16 ints)
        public const int InfoSystem = 0;
        public const int InfoGame = 1;
        public const int InfoSong = 2;
        public const int InfoAuthor = 3;
        public const int InfoCopyright = 4;
        public const int InfoComment = 5;

        // GME-supported file extensions
        public static readonly string[] SupportedExtensions = {
            ".vgm", ".vgz", ".spc", ".nsf", ".nsfe", ".gbs",
            ".gym", ".hes", ".kss", ".sap", ".ay"
        };

        public static bool IsGmeExtension(string extension)
        {
            foreach (var ext in SupportedExtensions)
            {
                if (string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
