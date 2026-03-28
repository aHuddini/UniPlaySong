using System;
using System.Collections.Generic;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Events;

namespace UniPlaySong.Services.Controller
{
    // Routes Playnite SDK controller events to the currently active dialog.
    // Uses a stack so nested modals (e.g., confirmation dialogs) can temporarily
    // take over input and restore the parent dialog when they close.
    public class ControllerEventRouter
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly Stack<IControllerInputReceiver> _receiverStack = new Stack<IControllerInputReceiver>();
        private readonly object _lock = new object();
        private readonly Common.FileLogger _fileLogger;

        // Brief cooldown after registration to ignore stale button presses
        // from the Playnite UI action that triggered the dialog (e.g., A to open menu item)
        private DateTime _registrationCooldownUntil = DateTime.MinValue;
        private const int RegistrationCooldownMs = 200;

        public ControllerEventRouter(Common.FileLogger fileLogger = null)
        {
            _fileLogger = fileLogger;
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
