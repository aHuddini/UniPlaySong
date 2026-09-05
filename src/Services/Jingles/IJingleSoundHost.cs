using System;

namespace UniPlaySong.Services.Jingles
{
    // Plays achievement sounds from somewhere other than Playnite's own process.
    //
    // Exists for one consumer: PlayniteAchievements records unlocks and re-times the chime in
    // post, which needs the real-time chime isolated from the recording. Its capture is
    // process-tree based (Windows Application Loopback), and a game launched beneath Playnite —
    // an emulator — puts game audio in the same tree as the chime, so the chime can only be
    // recovered by cancellation, which fails when the two correlate. A host in its own process
    // tree is captured cleanly instead. See features/JINGLE_SOUND_HOST.md.
    //
    // The requirement is a PID, not an output device.
    //
    // Deliberately narrow: achievement sounds are already a stateless fire-and-forget emission
    // with no fader, no pause sources, no visualizer and no effects (see
    // JingleService.PlayExternalSound), which is the only reason this path can leave the process
    // at all. Nothing else in UniPlaySong can.
    public interface IJingleSoundHost
    {
        // The host's process id, or 0 when it is not running. Read by PlayniteAchievements to
        // point its process-loopback capture at the right tree. Stable for the session, because
        // the host is resident — the PID must exist before the first unlock or the consumer
        // races to attach and loses exactly the unlock it was recording.
        int ProcessId { get; }

        // True when the host accepted the sound and will play it. False means the caller must
        // play in process instead, immediately.
        //
        // NEVER throws and never blocks for long: a capture feature must not become a new way
        // for achievement sounds to go missing, so every failure — not started, crashed,
        // quarantined by antivirus, slow to acknowledge — has to read as a plain false here.
        bool TryPlay(string filePath, double volume);

        void Start();

        void Stop();
    }
}
