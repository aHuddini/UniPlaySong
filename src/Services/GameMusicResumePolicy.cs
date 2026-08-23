using System;
using System.IO;
using UniPlaySong.Audio;

namespace UniPlaySong.Services
{
    // Where a game's music was when you last left it, so returning to that game picks up there
    // instead of restarting. Session-only — the marks live in MusicPlaybackService and die with the
    // process, the same way the default-music position already behaves.
    //
    // Pure, so the rules can be asserted without a player, a dispatcher or an audio device. That is
    // the same reason HoverSettlePolicy is shaped this way, and the parts of playback shaped like
    // this are the parts that have not regressed.
    public static class GameMusicResumePolicy
    {
        // Resuming with less than this left hands the listener a stub that immediately auto-advances,
        // which reads as a bug rather than as resume. Start the track over instead.
        public const double MinRemainingSeconds = 5.0;

        public struct Mark
        {
            public string SongPath;
            public TimeSpan Position;
        }

        // Whether the outgoing song's position is worth keeping.
        //
        // Chiptune is excluded deliberately: NAudio's Play(TimeSpan) assigns _audioFile.CurrentTime
        // synchronously on the calling thread, and a GME seek takes hundreds of milliseconds to
        // seconds (see the comment on NAudioMusicPlayer.Pause). Resuming into one would freeze
        // Playnite, so chiptune tracks always start from the beginning.
        public static bool ShouldRemember(bool enabled, string songPath, TimeSpan position, TimeSpan? totalTime)
        {
            if (!enabled) return false;
            if (string.IsNullOrEmpty(songPath)) return false;
            if (position <= TimeSpan.Zero) return false;
            if (GmeNative.IsGmeExtension(Path.GetExtension(songPath))) return false;

            if (totalTime.HasValue &&
                (totalTime.Value - position).TotalSeconds < MinRemainingSeconds)
            {
                return false;
            }

            return true;
        }

        // Where the incoming song should start. TimeSpan.Zero means "from the beginning", which is
        // every case except a clean match.
        //
        // The song path is compared, not just the game, because randomization can hand back a
        // different track for the same game on the next visit. Dropping a position from song A into
        // song B is worse than starting over.
        public static TimeSpan ResumeFrom(bool enabled, string songToPlay, Mark mark)
        {
            if (!enabled) return TimeSpan.Zero;
            if (string.IsNullOrEmpty(songToPlay)) return TimeSpan.Zero;
            if (string.IsNullOrEmpty(mark.SongPath)) return TimeSpan.Zero;
            if (mark.Position <= TimeSpan.Zero) return TimeSpan.Zero;

            if (!string.Equals(mark.SongPath, songToPlay, StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.Zero;
            }

            return mark.Position;
        }
    }
}
