using System;

namespace UniPlaySong.Services.Controller
{
    /// <summary>
    /// Service for detecting when controller input is the primary method
    /// </summary>
    public interface IControllerDetectionService : IDisposable
    {
        /// <summary>
        /// Gets whether controller mode is currently active
        /// </summary>
        bool IsControllerMode { get; }
        
        /// <summary>
        /// Event fired when controller mode changes
        /// </summary>
        event EventHandler<bool> ControllerModeChanged;
        
        /// <summary>
        /// Starts monitoring for controller state changes
        /// </summary>
        void StartMonitoring();
        
        /// <summary>
        /// Stops monitoring for controller state changes
        /// </summary>
        void StopMonitoring();
        
        /// <summary>
        /// Forces a one-time detection check
        /// </summary>
        /// <returns>True if controller is detected</returns>
        bool DetectControllerNow();
    }
}