using System.Windows.Input;
using Playnite.SDK.Events;

namespace UniPlaySong.Features.MusicInfoCard.Player
{
    // Keyboard for the Fullscreen Music Info Card, as the controller buttons it stands for. PageUp/PageDown are left
    // alone on purpose: Playnite fakes them for LT/RT (and the arrows for the D-pad), so mapping them would make every
    // trigger press also switch the page. Pages are Q/E; [ and ] seek; Space (handled by the dialog) plays/pauses.
    internal static class CardKeyMap
    {
        public static ControllerInput? ToInput(Key key)
        {
            switch (key)
            {
                case Key.Escape: return ControllerInput.B;
                case Key.Enter: return ControllerInput.A;
                case Key.Up: return ControllerInput.DPadUp;
                case Key.Down: return ControllerInput.DPadDown;
                case Key.Left: return ControllerInput.DPadLeft;
                case Key.Right: return ControllerInput.DPadRight;
                case Key.Q: return ControllerInput.LeftShoulder;
                case Key.E: return ControllerInput.RightShoulder;
                case Key.OemOpenBrackets: return ControllerInput.TriggerLeft;
                case Key.OemCloseBrackets: return ControllerInput.TriggerRight;
                default: return null;
            }
        }
    }
}
