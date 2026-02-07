using System;
using System.Runtime.InteropServices;

namespace UniPlaySong.Common
{
    // P/Invoke wrapper for SDL2_mixer (native music suppression in fullscreen mode)
    public static class SDL2MixerWrapper
    {
        private const string NativeLibName = "SDL2_mixer";

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_HaltMusic(); // 0 on success, -1 on error

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Mix_FreeMusic(IntPtr music);
    }
}
