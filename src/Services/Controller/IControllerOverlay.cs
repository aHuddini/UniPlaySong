using System;
using System.Windows.Controls;

namespace UniPlaySong.Services.Controller
{
    // Main coordination service that manages all controller-related functionality Acts as a facade over
    // the individual controller services
    public interface IControllerOverlay : IDisposable
    {
        // Gets whether the overlay is currently attached to a control
        bool IsAttached { get; }
        
        // Gets whether controller mode is currently active
        bool IsControllerMode { get; }
        
        // Event fired when controller mode changes
        event EventHandler<bool> ControllerModeChanged;
        
        // Event fired when a controller action is requested
        event EventHandler<ControllerAction> ActionRequested;
        
        /// <summary>
        /// Attaches the controller overlay to a target control
        /// </summary>
        /// <param name="targetControl">The control to enhance with controller support</param>
        void AttachTo(Control targetControl);
        
        // Detaches the controller overlay from its current target
        void Detach();
        
        /// <summary>
        /// Forces controller mode on or off (overrides automatic detection)
        /// </summary>
        /// <param name="enabled">True to force controller mode, false to force keyboard/mouse mode</param>
        void ForceControllerMode(bool enabled);
        
        // Restores automatic controller detection
        void RestoreAutomaticDetection();
    }
}
