using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace UniPlaySong.Controls
{
    // Makes a plain WPF Button respond to the Fullscreen gamepad "confirm" press (A, or B when
    // the user has Swap Confirm/Cancel on). Playnite's Fullscreen only auto-clicks its OWN
    // ButtonEx on the A press — that control is internal to Playnite.FullscreenApp and not in the
    // SDK, so a plugin control's plain Button never fires from the gamepad. UniPlaySong already
    // observes the press via the SDK's OnControllerButtonStateChanged; ConfirmFocused() bridges it
    // to whichever confirm-target Button currently holds keyboard focus (exactly what ButtonEx does
    // internally). Mouse/touch clicks are unaffected — this only adds the gamepad path.
    //
    // Usage in a control's XAML: <Button local:GamepadConfirm.IsTarget="True" Command="..."/>
    public static class GamepadConfirm
    {
        public static readonly DependencyProperty IsTargetProperty =
            DependencyProperty.RegisterAttached(
                "IsTarget", typeof(bool), typeof(GamepadConfirm),
                new PropertyMetadata(false));

        public static void SetIsTarget(DependencyObject element, bool value) =>
            element.SetValue(IsTargetProperty, value);

        public static bool GetIsTarget(DependencyObject element) =>
            (bool)element.GetValue(IsTargetProperty);

        // Called from the plugin when the gamepad confirm button is pressed and no UniPlaySong
        // modal is capturing input. Clicks the focused confirm-target button, if any. Returns true
        // when it handled the press (a target was focused and clicked). UI-thread only.
        public static bool ConfirmFocused()
        {
            if (Keyboard.FocusedElement is ButtonBase btn && GetIsTarget(btn))
            {
                if (btn.Command != null)
                {
                    if (btn.Command.CanExecute(btn.CommandParameter))
                        btn.Command.Execute(btn.CommandParameter);
                }
                else
                {
                    // No command bound — fall back to the Click routed event so Click handlers fire.
                    btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, btn));
                }
                return true;
            }
            return false;
        }
    }
}
