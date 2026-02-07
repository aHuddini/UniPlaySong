using System;
using System.Runtime.InteropServices;
using System.Threading;
using Playnite.SDK;

namespace UniPlaySong.Services.Controller
{
    /// <summary>
    /// Service for detecting controller input as the primary method
    /// Uses multiple detection strategies for maximum compatibility
    /// </summary>
    public class ControllerDetectionService : IControllerDetectionService
    {
        private readonly ILogger _logger;
        private Timer _detectionTimer;
        private bool _isControllerMode = false;
        private bool _isMonitoring = false;
        private int _failureCount = 0;
        private const int MAX_FAILURES = 5;
        private const int DETECTION_INTERVAL_MS = 2000; // Check every 2 seconds
        
        // Delegate type for XInput methods
        private delegate uint XInputGetStateDelegate(uint dwUserIndex, ref XINPUT_STATE pState);
        
        // XInput structures for controller detection
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        // XInput API imports (safe - will handle if DLL not available)
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState", SetLastError = true)]
        private static extern uint XInputGetState_1_4(uint dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState", SetLastError = true)]
        private static extern uint XInputGetState_9_1_0(uint dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState", SetLastError = true)]
        private static extern uint XInputGetState_1_3(uint dwUserIndex, ref XINPUT_STATE pState);

        public event EventHandler<bool> ControllerModeChanged;

        public bool IsControllerMode
        {
            get => _isControllerMode;
            private set
            {
                if (_isControllerMode != value)
                {
                    _isControllerMode = value;
                    try
                    {
                        ControllerModeChanged?.Invoke(this, value);
                        _logger?.Debug($"Controller mode changed to: {value}");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warn(ex, "Error notifying controller mode change");
                    }
                }
            }
        }

        public ControllerDetectionService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            try
            {
                _isMonitoring = true;
                _failureCount = 0;
                
                // Do initial detection
                IsControllerMode = DetectControllerNow();
                
                // Start periodic monitoring
                _detectionTimer = new Timer(DetectionCallback, null, DETECTION_INTERVAL_MS, DETECTION_INTERVAL_MS);
                
                _logger?.Debug("Controller detection monitoring started");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to start controller monitoring - falling back to keyboard/mouse mode");
                IsControllerMode = false;
                _isMonitoring = false;
            }
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            try
            {
                _isMonitoring = false;
                _detectionTimer?.Dispose();
                _detectionTimer = null;
                _logger?.Debug("Controller detection monitoring stopped");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error stopping controller monitoring - no functional impact");
            }
        }

        public bool DetectControllerNow()
        {
            try
            {
                // Method 1: XInput detection (most reliable)
                if (DetectXInputController())
                {
                    _logger?.Debug("Controller detected via XInput");
                    return true;
                }

                // Method 2: Playnite API detection (if available)
                if (DetectPlayniteController())
                {
                    _logger?.Debug("Controller detected via Playnite API");
                    return true;
                }

                // Method 3: Windows API detection (fallback)
                if (DetectWindowsController())
                {
                    _logger?.Debug("Controller detected via Windows API");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Controller detection failed - assuming keyboard/mouse");
                return false;
            }
        }

        private void DetectionCallback(object state)
        {
            try
            {
                bool wasControllerMode = IsControllerMode;
                bool isControllerMode = DetectControllerNow();
                
                // Only update if state changed
                if (wasControllerMode != isControllerMode)
                {
                    IsControllerMode = isControllerMode;
                }
                
                // Reset failure count on successful detection
                _failureCount = 0;
            }
            catch (Exception ex)
            {
                _failureCount++;
                _logger?.Debug(ex, $"Controller detection failed (attempt {_failureCount}/{MAX_FAILURES})");
                
                // If we have too many failures, stop monitoring to prevent spam
                if (_failureCount >= MAX_FAILURES)
                {
                    _logger?.Warn($"Controller detection failed {MAX_FAILURES} times, stopping monitoring");
                    StopMonitoring();
                    IsControllerMode = false;
                }
            }
        }

        private bool DetectXInputController()
        {
            try
            {
                // Try XInput 1.4 first (Windows 8+)
                if (TryXInputGetState(XInputGetState_1_4))
                    return true;

                // Try XInput 1.3 (Windows Vista+)
                if (TryXInputGetState(XInputGetState_1_3))
                    return true;

                // Try XInput 9.1.0 (Windows XP+)
                if (TryXInputGetState(XInputGetState_9_1_0))
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "XInput detection failed");
                return false;
            }
        }

        private bool TryXInputGetState(XInputGetStateDelegate xinputMethod)
        {
            try
            {
                // Check all 4 possible controller slots
                for (uint i = 0; i < 4; i++)
                {
                    var state = new XINPUT_STATE();
                    uint result = xinputMethod(i, ref state);
                    
                    // ERROR_SUCCESS = 0 means controller is connected
                    if (result == 0)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (DllNotFoundException)
            {
                // XInput DLL not available on this system
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                // Function not available in this XInput version
                return false;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "XInput method call failed");
                return false;
            }
        }

        private bool DetectPlayniteController()
        {
            try
            {
                // TODO: Add Playnite API controller detection if available
                // This would require checking Playnite's input system
                // For now, return false as this is a future enhancement
                return false;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Playnite controller detection failed");
                return false;
            }
        }

        private bool DetectWindowsController()
        {
            try
            {
                // Use Windows API to detect game controllers
                // This is a fallback method for when XInput is not available
                uint numDevices = NativeMethods.joyGetNumDevs();
                
                if (numDevices == 0)
                    return false;

                // Check each potential joystick device
                for (uint i = 0; i < numDevices && i < 16; i++) // Limit to 16 devices for performance
                {
                    var caps = new NativeMethods.JOYCAPS();
                    uint result = NativeMethods.joyGetDevCaps(i, ref caps, (uint)Marshal.SizeOf(caps));
                    
                    if (result == NativeMethods.JOYERR_NOERROR)
                    {
                        // Device exists, check if it's connected
                        var info = new NativeMethods.JOYINFO();
                        uint posResult = NativeMethods.joyGetPos(i, ref info);
                        
                        if (posResult == NativeMethods.JOYERR_NOERROR)
                        {
                            return true; // Found a connected controller
                        }
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Windows controller detection failed");
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                StopMonitoring();
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error disposing ControllerDetectionService");
            }
        }

        /// <summary>
        /// Native methods for Windows controller detection
        /// </summary>
        private static class NativeMethods
        {
            public const uint JOYERR_NOERROR = 0;
            
            [DllImport("winmm.dll")]
            public static extern uint joyGetNumDevs();

            [DllImport("winmm.dll")]
            public static extern uint joyGetDevCaps(uint uJoyID, ref JOYCAPS pjc, uint cbjc);

            [DllImport("winmm.dll")]
            public static extern uint joyGetPos(uint uJoyID, ref JOYINFO pji);

            [StructLayout(LayoutKind.Sequential)]
            public struct JOYCAPS
            {
                public ushort wMid;
                public ushort wPid;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
                public string szPname;
                public uint wXmin;
                public uint wXmax;
                public uint wYmin;
                public uint wYmax;
                public uint wZmin;
                public uint wZmax;
                public uint wNumButtons;
                public uint wPeriodMin;
                public uint wPeriodMax;
                public uint wRmin;
                public uint wRmax;
                public uint wUmin;
                public uint wUmax;
                public uint wVmin;
                public uint wVmax;
                public uint wCaps;
                public uint wMaxAxes;
                public uint wNumAxes;
                public uint wMaxButtons;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
                public string szRegKey;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                public string szOEMVKey;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct JOYINFO
            {
                public uint wXpos;
                public uint wYpos;
                public uint wZpos;
                public uint wButtons;
            }
        }
    }
}