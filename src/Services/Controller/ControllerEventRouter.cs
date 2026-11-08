using System;
using System.Collections.Generic;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Events;

namespace UniPlaySong.Services.Controller
{
    // Decides where each Playnite SDK controller press goes (Route): the login-screen bypass, the confirm button on a
    // UPS button embedded in a theme, or the currently active UPS dialog. Dialogs sit on a stack so nested modals
    // (e.g., confirmation dialogs) can temporarily take over input and restore the parent dialog when they close.
    public class ControllerEventRouter
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();
        private readonly Stack<IControllerInputReceiver> _receiverStack = new Stack<IControllerInputReceiver>();
        private readonly object _lock = new object();
        private readonly Common.FileLogger _fileLogger;
        private readonly Func<ControllerInput> _confirmButton; // A, or B with Playnite's Swap Confirm/Cancel
        private readonly Action _onLoginDismiss;

        // Set while a theme's login screen is up (ArmLoginBypass); the next A/Start dismisses it.
        private volatile bool _loginBypassArmed;

        // Brief cooldown after registration to ignore stale button presses
        // from the Playnite UI action that triggered the dialog (e.g., A to open menu item)
        private DateTime _registrationCooldownUntil = DateTime.MinValue;
        private const int RegistrationCooldownMs = 200;

        public ControllerEventRouter(Common.FileLogger fileLogger = null, Func<ControllerInput> confirmButton = null,
            Action onLoginDismiss = null)
        {
            _fileLogger = fileLogger;
            _confirmButton = confirmButton ?? (() => ControllerInput.A);
            _onLoginDismiss = onLoginDismiss;
        }

        public void ArmLoginBypass() => _loginBypassArmed = true;

        public void Route(OnControllerButtonStateChangedArgs args)
        {
            if (args == null) return;

            // No per-press logging here — this is a hot path (fires on every button press AND
            // release, Desktop + Fullscreen). Dialog register/unregister is logged below.

            // Login bypass — check before routing to active dialog
            if (_loginBypassArmed && args.State == ControllerInputState.Pressed
                && (args.Button == ControllerInput.A || args.Button == ControllerInput.Start))
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    _onLoginDismiss?.Invoke();
                }));
                _loginBypassArmed = false;
                return;
            }

            if (args.State == ControllerInputState.Pressed)
            {
                // Embedded media-controller buttons: Playnite only auto-clicks its own internal ButtonEx on the confirm press,
                // so our plugin Buttons need this bridge. Only when no UPS modal owns input (a dialog's own A/B handling takes
                // precedence) and the confirm button (A, or B if Swap Confirm/Cancel is on) is pressed. If a focused
                // confirm-target button handles it, don't also route to the modal stack.
                if (!HasActiveReceiver
                    && args.Button == _confirmButton())
                {
                    bool handled = false;
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        handled = Controls.GamepadConfirm.ConfirmFocused();
                    });
                    if (handled) return;
                }

                HandleButtonPressed(args.Button);
            }
            else if (args.State == ControllerInputState.Released)
                HandleButtonReleased(args.Button);
        }

        // True when a dialog is actively receiving controller input (don't intercept A/B for play-on-select)
        public bool HasActiveReceiver
        {
            get { lock (_lock) { return _receiverStack.Count > 0; } }
        }

        public void Register(IControllerInputReceiver receiver)
        {
            lock (_lock)
            {
                _receiverStack.Push(receiver);
                _registrationCooldownUntil = DateTime.Now.AddMilliseconds(RegistrationCooldownMs);
                _fileLogger?.Debug($"[ControllerRouter] Registered: {receiver.GetType().Name} (stack depth: {_receiverStack.Count})");
            }
        }

        public void Unregister(IControllerInputReceiver receiver)
        {
            lock (_lock)
            {
                if (_receiverStack.Count > 0 && _receiverStack.Peek() == receiver)
                {
                    _receiverStack.Pop();
                    _fileLogger?.Debug($"[ControllerRouter] Unregistered: {receiver.GetType().Name} (stack depth: {_receiverStack.Count})");
                }
                else
                {
                    // Receiver isn't on top — remove it from wherever it is in the stack
                    var temp = new Stack<IControllerInputReceiver>();
                    while (_receiverStack.Count > 0)
                    {
                        var item = _receiverStack.Pop();
                        if (item != receiver)
                            temp.Push(item);
                    }
                    while (temp.Count > 0)
                        _receiverStack.Push(temp.Pop());
                    _fileLogger?.Debug($"[ControllerRouter] Unregistered (non-top): {receiver.GetType().Name} (stack depth: {_receiverStack.Count})");
                }
            }
        }

        public void HandleButtonPressed(ControllerInput button)
        {
            IControllerInputReceiver receiver;
            lock (_lock)
            {
                if (DateTime.Now < _registrationCooldownUntil) return;
                receiver = _receiverStack.Count > 0 ? _receiverStack.Peek() : null;
            }

            if (receiver == null) return;

            // Use Input priority so the nested ShowDialog() message pump processes our events.
            // Default BeginInvoke priority (Normal) may be starved by the modal's Send-priority pump.
            Application.Current?.Dispatcher?.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() =>
                {
                    receiver.OnControllerButtonPressed(button);
                }));
        }

        public void HandleButtonReleased(ControllerInput button)
        {
            IControllerInputReceiver receiver;
            lock (_lock)
            {
                receiver = _receiverStack.Count > 0 ? _receiverStack.Peek() : null;
            }

            if (receiver == null) return;

            Application.Current?.Dispatcher?.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() =>
                {
                    receiver.OnControllerButtonReleased(button);
                }));
        }
    }
}
