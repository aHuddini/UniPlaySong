using System;
using System.Windows.Controls;

namespace UniPlaySong.Services.Controller
{
    // Actions that can be triggered by controller input
    public enum ControllerAction
    {
        None,
        Confirm,        // A/Enter - Primary action
        Cancel,         // B/Escape - Cancel/Back
        Preview,        // Y - Preview current item
        MultiSelect,    // X - Select All/Toggle selection
        PageUp,         // LB/RB - Page navigation
        PageDown,
        JumpToTop,      // LT/RT - Jump navigation
        JumpToBottom,
        Search,         // Special action to trigger search
        ShowOSK         // Show On-Screen Keyboard for text input
    }

    // Service for handling controller input mapping and events
    public interface IControllerInputService : IDisposable
    {
        // Event fired when a controller action is requested
        event EventHandler<ControllerAction> ActionRequested;
        
        /// <summary>
        /// Attaches input handling to a control
        /// </summary>
        /// <param name="control">The control to monitor for input</param>
        void AttachToControl(Control control);
        
        /// <summary>
        /// Detaches input handling from a control
        /// </summary>
        /// <param name="control">The control to stop monitoring</param>
        void DetachFromControl(Control control);
        
        // Gets whether the service is currently attached to any controls
        bool IsAttached { get; }
    }
}
