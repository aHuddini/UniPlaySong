using Playnite.SDK.Events;

namespace UniPlaySong.Services.Controller
{
    // Implemented by dialogs that receive controller input from the centralized event router.
    // Only one receiver is active at a time (last registered wins).
    public interface IControllerInputReceiver
    {
        void OnControllerButtonPressed(ControllerInput button);
        void OnControllerButtonReleased(ControllerInput button);
    }
}
