using System;

namespace UniPlaySong.Features.MusicInfoCard.Player
{
    // One source of repeats for a held seek. Playnite resends a held trigger's press (after 0.7 s, then every 80 ms) and
    // the card runs its own steady repeat; both together made the speed jump. While a seek is held, a press in the same
    // direction is the resend and is ignored; the other direction starts a new hold.
    internal sealed class HeldSeek
    {
        public double Step { get; private set; }
        public bool IsHeld => Step != 0;

        // True when this press starts a hold (seek now and start repeating); false for a resend of the held one
        public bool Begin(double seconds)
        {
            if (IsHeld && Math.Sign(Step) == Math.Sign(seconds)) return false;
            Step = seconds;
            return true;
        }

        public void End() => Step = 0;
    }
}
