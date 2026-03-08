using System;
using System.Windows.Controls;

namespace UniPlaySong.Services.Controller
{
    /// <summary>
    /// Actions that can be triggered by controller input
    /// </summary>
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

    /// <summary>
    /// Service for handling controller input mapping and events
    /// </summary>
    public interface IControllerInputService : IDisposable
    {
        /// <summary>
        /// Event fired when a controller action is requested
        /// </summary>
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
        
        /// <summary>
        /// Gets whether the service is currently attached to any controls
        /// </summary>
        bool IsAttached { get; }
    }
}