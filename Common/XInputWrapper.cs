using System;
using System.Runtime.InteropServices;

namespace UniPlaySong.Common
{
    // P/Invoke wrapper for XInput (Xbox controller) with version fallback
    public static class XInputWrapper
    {
        #region P/Invoke Declarations

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_1_4(int dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_1_3(int dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_9_1_0(int dwUserIndex, ref XINPUT_STATE pState);

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        #endregion

        #region Button Constants

        public const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        public const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        public const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        public const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        public const ushort XINPUT_GAMEPAD_START = 0x0010;
        public const ushort XINPUT_GAMEPAD_BACK = 0x0020;
        public const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
        public const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
        public const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        public const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
        public const ushort XINPUT_GAMEPAD_A = 0x1000;
        public const ushort XINPUT_GAMEPAD_B = 0x2000;
        public const ushort XINPUT_GAMEPAD_X = 0x4000;
        public const ushort XINPUT_GAMEPAD_Y = 0x8000;

        #endregion

        #region Public Methods

        // Gets controller state, trying XInput 1.4 -> 1.3 -> 9.1.0. Returns 0 on success.
        public static int XInputGetState(int dwUserIndex, ref XINPUT_STATE pState)
        {
            try
            {
                // Try XInput 1.4 first (Windows 8+)
                return XInputGetState_1_4(dwUserIndex, ref pState);
            }
            catch
            {
                try
                {
                    // Fall back to XInput 1.3 (Windows 7+)
                    return XInputGetState_1_3(dwUserIndex, ref pState);
                }
                catch
                {
                    try
                    {
                        // Fall back to XInput 9.1.0 (older systems)
                        return XInputGetState_9_1_0(dwUserIndex, ref pState);
                    }
                    catch
                    {
                        // No XInput available
                        return -1;
                    }
                }
            }
        }

        #endregion
    }
}
