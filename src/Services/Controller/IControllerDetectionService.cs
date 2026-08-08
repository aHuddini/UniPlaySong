using System;

namespace UniPlaySong.Services.Controller
{
    // Service for detecting when controller input is the primary method
    public interface IControllerDetectionService : IDisposable
    {
        // Gets whether controller mode is currently active
        bool IsControllerMode { get; }
        
        // Event fired when controller mode changes
        event EventHandler<bool> ControllerModeChanged;
        
        // Starts monitoring for controller state changes
        void StartMonitoring();
        
        // Stops monitoring for controller state changes
        void StopMonitoring();
        
        /// <summary>
        /// Forces a one-time detection check
        /// </summary>
        /// <returns>True if controller is detected</returns>
        bool DetectControllerNow();
    }
}
