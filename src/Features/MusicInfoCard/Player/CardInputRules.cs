using System;
using Playnite.SDK.Events;
using UniPlaySong.Common;

namespace UniPlaySong.Features.MusicInfoCard.Player
{
    internal enum CardPage { Player, Details }

    internal enum CardCursor { Songs, Controls }

    // The dock's controls, left to right
    internal enum CardControl { Previous, PlayPause, Next, CalmDown, LiveEffects }

    internal enum CardCommandKind { None, Redraw, Close, PlaySong, Press, Seek }

    // What the dialog should do after a button press
    internal struct CardCommand
    {
        public CardCommandKind Kind;
        public int SongIndex;
        public CardControl Control;
        public double SeekSeconds;

        public static CardCommand None => new CardCommand { Kind = CardCommandKind.None };
        public static CardCommand Redraw => new CardCommand { Kind = CardCommandKind.Redraw };
        public static CardCommand Close => new CardCommand { Kind = CardCommandKind.Close };
        public static CardCommand Play(int songIndex) => new CardCommand { Kind = CardCommandKind.PlaySong, SongIndex = songIndex };
        public static CardCommand PressControl(CardControl control) => new CardCommand { Kind = CardCommandKind.Press, Control = control };
        public static CardCommand SeekBy(double seconds) => new CardCommand { Kind = CardCommandKind.Seek, SeekSeconds = seconds };
    }

    // The Fullscreen Music Info Card's controller rules, without WPF. Two cursors: up/down (D-pad or left stick) move through
    // the songs, left/right along the dock's controls; the one moved last is live and A acts on it, the other keeps its
    // place. LB/RB switch pages; LT/RT seek; B or Back closes. The right stick jogs (seeks) only while Play/Pause is live
    // on the Player page - elsewhere it scrolls.
    internal sealed class CardInputRules
    {
        private static readonly int ControlCount = Enum.GetValues(typeof(CardControl)).Length;
        private readonly int _songCount;

        public CardPage Page { get; private set; } = CardPage.Player;
        public CardCursor Live { get; private set; } = CardCursor.Songs;
        public int SongIndex { get; private set; }
        public CardControl Control { get; private set; } = CardControl.PlayPause;

        public bool RightStickJogs => Page == CardPage.Player && Live == CardCursor.Controls && Control == CardControl.PlayPause;

        public CardInputRules(int songCount, int startSong)
        {
            _songCount = Math.Max(0, songCount);
            SongIndex = _songCount == 0 ? 0 : Math.Max(0, Math.Min(_songCount - 1, startSong));
        }

        public CardCommand Press(ControllerInput button)
        {
            switch (button)
            {
                case ControllerInput.B:
                case ControllerInput.Back:
                    return CardCommand.Close;
                case ControllerInput.LeftShoulder:
                    return SetPage(CardPage.Player);
                case ControllerInput.RightShoulder:
                    return SetPage(CardPage.Details);
            }

            if (Page != CardPage.Player) return CardCommand.None;

            switch (button)
            {
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    return MoveSong(-1);
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    return MoveSong(1);
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                    return MoveControl(-1);
                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                    return MoveControl(1);
                case ControllerInput.TriggerLeft:
                    return CardCommand.SeekBy(-Constants.MusicInfoCardSeekStepSeconds);
                case ControllerInput.TriggerRight:
                    return CardCommand.SeekBy(Constants.MusicInfoCardSeekStepSeconds);
                case ControllerInput.A:
                    if (Live == CardCursor.Controls) return CardCommand.PressControl(Control);
                    return _songCount == 0 ? CardCommand.None : CardCommand.Play(SongIndex);
                default:
                    return CardCommand.None;
            }
        }

        private CardCommand SetPage(CardPage page)
        {
            if (Page == page) return CardCommand.None;
            Page = page;
            return CardCommand.Redraw;
        }

        // A move makes its cursor live even when clamped at an end; it redraws when anything changed
        private CardCommand MoveSong(int step)
        {
            bool changed = Live != CardCursor.Songs;
            Live = CardCursor.Songs;
            if (_songCount > 0)
            {
                int next = Math.Max(0, Math.Min(_songCount - 1, SongIndex + step));
                changed |= next != SongIndex;
                SongIndex = next;
            }
            return changed ? CardCommand.Redraw : CardCommand.None;
        }

        private CardCommand MoveControl(int step)
        {
            bool changed = Live != CardCursor.Controls;
            Live = CardCursor.Controls;
            var next = (CardControl)Math.Max(0, Math.Min(ControlCount - 1, (int)Control + step));
            changed |= next != Control;
            Control = next;
            return changed ? CardCommand.Redraw : CardCommand.None;
        }
    }
}
