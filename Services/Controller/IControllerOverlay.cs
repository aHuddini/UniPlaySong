using System;
using System.Windows.Controls;

namespace UniPlaySong.Services.Controller
{
    /// <summary>
    /// Main coordination service that manages all controller-related functionality
    /// Acts as a facade over the individual controller services
    /// </summary>
    public interface IControllerOverlay : IDisposable
    {
        /// <summary>
        /// Gets whether the overlay is currently attached to a control
        /// </summary>
        bool IsAttached { get; }
        
        /// <summary>
        /// Gets whether controller mode is currently active
        /// </summary>
        bool IsControllerMode { get; }
        
        /// <summary>
        /// Event fired when controller mode changes
        /// </summary>
        event EventHandler<bool> ControllerModeChanged;
        
        /// <summary>
        /// Event fired when a controller action is requested
        /// </summary>
        event EventHandler<ControllerAction> ActionRequested;
        
        /// <summary>
        /// Attaches the controller overlay to a target control
        /// </summary>
        /// <param name="targetControl">The control to enhance with controller support</param>
        void AttachTo(Control targetControl);
        
        /// <summary>
        /// Detaches the controller overlay from its current target
        /// </summary>
        void Detach();
        
        /// <summary>
        /// Forces controller mode on or off (overrides automatic detection)
        /// </summary>
        /// <param name="enabled">True to force controller mode, false to force keyboard/mouse mode</param>
        void ForceControllerMode(bool enabled);
        
        /// <summary>
        /// Restores automatic controller detection
        /// </summary>
        void RestoreAutomaticDetection();
    }
}