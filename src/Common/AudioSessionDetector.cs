using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UniPlaySong.Common
{
    // Direct COM interop for WASAPI audio session detection.
    // Bypasses NAudio's MMDeviceEnumerator to avoid COM apartment (STA/MTA) issues
    // that cause InvalidCastException on ThreadPool threads.
    public static class AudioSessionDetector
    {
        private static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");

        // Detects if any non-self audio session is outputting above the peak threshold.
        public static bool IsExternalAudioPlaying(int selfPid, float peakThreshold, HashSet<string> excludedProcessNames)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;

            try
            {
                var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type);

                int hr = enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out device);
                if (hr != 0 || device == null) return false;

                var iidSessionManager = typeof(IAudioSessionManager2).GUID;
                hr = device.Activate(ref iidSessionManager, 0x17 /*CLSCTX_ALL*/, IntPtr.Zero, out object obj);
                if (hr != 0 || obj == null) return false;

                var sessionManager = (IAudioSessionManager2)obj;
                hr = sessionManager.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
                if (hr != 0 || sessionEnum == null) return false;

                sessionEnum.GetCount(out int count);

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl control = null;
                    try
                    {
                        sessionEnum.GetSession(i, out control);
                        if (control == null) continue;

                        var control2 = control as IAudioSessionControl2;
                        if (control2 == null) continue;

                        control2.GetProcessId(out uint pid);
                        if (pid == selfPid || pid == 0) continue;

                        // IsSystemSoundsSession returns S_OK (0) if it IS the system sounds session
                        int isSystemHr = control2.IsSystemSoundsSession();
                        if (isSystemHr == 0) continue;

                        // Check exclusion list by process name
                        if (excludedProcessNames != null && excludedProcessNames.Count > 0)
                        {
                            try
                            {
                                var proc = Process.GetProcessById((int)pid);
                                if (excludedProcessNames.Contains(proc.ProcessName.ToLowerInvariant()))
                                    continue;
                            }
                            catch { }
                        }

                        // Check audio meter via QI on the session control
                        var meter = control as IAudioMeterInformation;
                        if (meter != null)
                        {
                            meter.GetPeakValue(out float peak);
                            if (peak > peakThreshold)
                                return true;
                        }
                    }
                    catch { }
                    finally
                    {
                        if (control != null) Marshal.ReleaseComObject(control);
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (device != null) try { Marshal.ReleaseComObject(device); } catch { }
                if (enumerator != null) try { Marshal.ReleaseComObject(enumerator); } catch { }
            }
        }

        #region COM Interfaces

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate([In] ref Guid iid, [In] int clsCtx, [In] IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object obj);
        }

        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            int GetAudioSessionControl(IntPtr groupingParam, int flags, out IntPtr session);
            int GetSimpleAudioVolume(IntPtr groupingParam, int flags, out IntPtr volume);
            int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        }

        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            int GetCount(out int count);
            int GetSession(int index, [MarshalAs(UnmanagedType.Interface)] out IAudioSessionControl session);
        }

        // IAudioSessionControl + IAudioSessionControl2 combined
        // vtable order must match Windows SDK exactly
        [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl methods
            int GetState(out int state);
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, [In] ref Guid context);
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, [In] ref Guid context);
            int GetGroupingParam(out Guid groupingParam);
            int SetGroupingParam([In] ref Guid groupingParam, [In] ref Guid context);
            int RegisterAudioSessionNotification(IntPtr client);
            int UnregisterAudioSessionNotification(IntPtr client);

            // IAudioSessionControl2 methods
            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetProcessId(out uint pid);
            [PreserveSig] int IsSystemSoundsSession();
            int SetDuckingPreference(int optOut);
        }

        // Minimal IAudioSessionControl for QI purposes
        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            int GetState(out int state);
        }

        [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioMeterInformation
        {
            int GetPeakValue(out float peak);
        }

        #endregion
    }
}
