using System;
using System.Runtime.InteropServices;

namespace UniPlaySong.Common
{
    /// <summary>
    /// P/Invoke wrapper for SDL2_mixer library functions
    /// Used for native music suppression in Playnite fullscreen mode
    /// </summary>
    public static class SDL2MixerWrapper
    {
        private const string NativeLibName = "SDL2_mixer";

        /// <summary>
        /// Halts playback of music (stops the currently playing music)
        /// </summary>
        /// <returns>0 on success, -1 on errors</returns>
        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_HaltMusic();

        /// <summary>
        /// Frees music pointer and related resources
        /// </summary>
        /// <param name="music">Pointer to Mix_Music to free</param>
        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Mix_FreeMusic(IntPtr music);
    }
}
