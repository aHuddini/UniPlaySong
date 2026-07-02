using System;

namespace UniPlaySong.Services.ActiveMedia
{
    // Resolves the single audible "active source" (Spotify when it is the active
    // source, else the UPS internal player, else none) and routes transport to it.
    public interface IActiveMediaService
    {
        // Latest resolved state. Never null; ActiveMediaSnapshot.Empty when nothing active.
        ActiveMediaSnapshot GetSnapshot();

        // Toggle play/pause on the active source.
        void PlayPause();

        // Next track (Spotify) / skip to next song (UPS).
        void Next();

        // Previous track (Spotify) / restart current song at 0:00 (UPS).
        void Previous();

        // Toggle mute on the active source.
        void ToggleMute();

        // Set the active source's volume, 0–100. Ignored if the source can't accept it.
        void SetVolume(double volume0to100);

        // Re-read the active source's position (called by the 1s poll timer while playing).
        void Poll();

        // Raised (already marshalled to the UI thread) whenever the snapshot changes.
        event Action Changed;
    }
}
