using System;
using System.Runtime.InteropServices;

namespace UniPlaySong.Audio
{
    // P/Invoke declarations for psf.dll, the PlayStation music engine built from native/psf/.
    // psf.dll must be in the plugin directory alongside UniPlaySong.dll.
    //
    // The engine lives entirely inside a caller-owned state buffer and keeps pointers into it, so
    // the buffer is passed as a pinned IntPtr, never as a byte[] the marshaller may copy or the GC
    // may move. See PsfReader for the pin.
    internal static class PsfNative
    {
        private const string DLL = "psf";

        // Return value of psf_start / psf_gen / psf_stop on success.
        public const int Success = 1;

        // Bytes to allocate for one engine. version: 1 for PlayStation.
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint psx_get_state_size(uint version);

        // 50 or 60. Must be set before the first psf_load_section.
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void psx_set_refresh(IntPtr psx, uint refresh);

        // NUL-terminated text of the last error, or empty.
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr psx_get_last_error(IntPtr psx);

        // Loads one decompressed PS-X EXE. first != 0 clears RAM and takes PC/SP from this
        // section. Returns 0 on success.
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint psf_load_section(IntPtr psx, byte[] exe, uint length, uint first);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int psf_start(IntPtr psx);

        // Renders `frames` stereo 16-bit frames at 44100 Hz into buffer (2 * frames shorts).
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int psf_gen(IntPtr psx, short[] buffer, uint frames);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int psf_stop(IntPtr psx);

        public static string LastError(IntPtr psx)
        {
            var text = Marshal.PtrToStringAnsi(psx_get_last_error(psx));
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
    }
}
