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
        
        // One-off check; true if a controller is detected.
        bool DetectControllerNow();
    }
}
