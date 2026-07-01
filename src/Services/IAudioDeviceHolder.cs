namespace UniPlaySong.Services
{
    // A component that holds an OS audio output device/stream open. Registered with
    // AudioDeviceRegistry so the sleep coordinator can close every open device when the
    // system is going idle / locking / suspending (issue #81). All members must be fail-safe.
    public interface IAudioDeviceHolder
    {
        // Closes this holder's OS audio device/stream if open. Idempotent; never throws.
        // The device is expected to reopen lazily on the next Load()/Play().
        void ReleaseAudioDevice();

        // True if this holder currently has an OS audio device/stream open.
        bool IsAudioDeviceOpen { get; }

        // Short label for diagnostics, e.g. "MainPlayer(NAudio)", "DashboardPlayer".
        string AudioDeviceLabel { get; }
    }
}
