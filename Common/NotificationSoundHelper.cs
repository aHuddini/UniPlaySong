using System;
using UniPlaySong.Services;

namespace UniPlaySong.Common
{
    // Plays notification sounds for download events
    internal static class NotificationSoundHelper
    {
        // Plays the download complete notification sound
        public static void PlayDownloadComplete(IMusicPlaybackService playbackService)
        {
            try
            {
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch { }
        }
    }
}
