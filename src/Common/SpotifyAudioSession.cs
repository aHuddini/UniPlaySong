using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UniPlaySong.Common
{
    // Mutes/unmutes the Spotify DESKTOP app at the Windows level via its WASAPI audio
    // session (ISimpleAudioVolume.SetMute) — the same thing as toggling Spotify in the
    // Windows Volume Mixer. Used for the theme "mute" button when Spotify is the active
    // source: SMTC (GlobalSystemMediaTransportControlsSession) is transport-only and has
    // no volume/mute, so the audio-session plane is the only way to mute Spotify.
    //
    // Direct COM interop (not NAudio) to avoid the STA/MTA InvalidCastException issues that
    // AudioSessionDetector documents. Spotify runs several processes but only ONE owns the
    // audio session; matching by process name on the enumerated sessions finds it (verified
    // live: 7 Spotify processes, exactly 1 session). All calls fail-soft — if Spotify isn't
    // running or the session is gone, they no-op and report unmuted.
    public static class SpotifyAudioSession
    {
        private static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static Guid _eventContext = Guid.Empty;
        private const string SpotifyProcessName = "spotify";

        // Toggles Spotify's session mute; returns the NEW muted state (false on any failure).
        public static bool ToggleMute()
        {
            InvalidateMuteVolumeCache();
            return WithSpotifyVolume(vol =>
            {
                vol.GetMute(out bool muted);
                bool next = !muted;
                vol.SetMute(next, ref _eventContext);
                return next;
            });
        }

        // Deterministic mute set (the effects invariant needs an explicit target, not a toggle).
        // Returns true if Spotify's session was found and set.
        public static bool SetMuted(bool muted)
        {
            InvalidateMuteVolumeCache();
            return WithSpotifyVolume(vol =>
            {
                vol.SetMute(muted, ref _eventContext);
                return true;
            });
        }

        // Sets Spotify's session master volume (0.0-1.0). Used by the live-effects duck:
        // the process-loopback tap is post-session-volume, so a hard mute would silence the
        // capture too — ducking to 2^-10 keeps the capture alive (restored by gain in the provider).
        public static bool SetSessionVolume(float level)
        {
            InvalidateMuteVolumeCache();
            return WithSpotifyVolume(vol =>
            {
                vol.SetMasterVolume(level, ref _eventContext);
                return true;
            });
        }

        // Reads Spotify's session master volume (fallback if not found). Ignores mute state —
        // this is the raw slider level, used to save/restore around the effects duck.
        public static float GetSessionVolume(float fallback = 1f)
        {
            float result = fallback;
            bool found = WithSpotifyVolume(vol =>
            {
                vol.GetMasterVolume(out float level);
                result = level;
                return true;
            });
            return found ? result : fallback;
        }

        // Cached (muted, volume) — one enumeration for both, ~1s TTL. Snapshot builders and theme
        // bindings poll on the UI thread every ~2s; enumerating an ACTIVE Spotify session takes
        // ~90ms, so a live read there was a 90ms UI stall per tick (worse while playing). The cache
        // makes the read instant; the enumeration still happens, just at most ~1x/sec off the caller.
        private static readonly object _cacheLock = new object();
        private static bool _cachedMuted;
        private static float _cachedVolume;
        private static DateTime _cacheStampUtc = DateTime.MinValue;

        private static void RefreshMuteVolumeCache()
        {
            bool m = false; float v = 0f;
            WithSpotifyVolume(vol =>
            {
                vol.GetMute(out m);
                vol.GetMasterVolume(out v);
                return true;
            });
            lock (_cacheLock) { _cachedMuted = m; _cachedVolume = v; _cacheStampUtc = DateTime.UtcNow; }
        }

        private static int _refreshing; // 0/1 guard so only one background refresh runs at a time

        // Never blocks the caller: returns the last cached value and kicks an OFF-THREAD refresh if
        // stale. The COM enumeration (~90ms on an active session) thus never runs on the UI thread —
        // callers get slightly stale mute/volume for up to ~1s, which is imperceptible for an icon.
        private static void EnsureCacheFreshAsync()
        {
            bool stale;
            lock (_cacheLock) stale = (DateTime.UtcNow - _cacheStampUtc) > TimeSpan.FromSeconds(1);
            if (!stale) return;
            if (System.Threading.Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { RefreshMuteVolumeCache(); }
                finally { System.Threading.Interlocked.Exchange(ref _refreshing, 0); }
            });
        }

        // Cached mute state — never blocks; refreshes off-thread. Use in per-frame/poll paths.
        public static bool IsMutedCached() { EnsureCacheFreshAsync(); lock (_cacheLock) return _cachedMuted; }

        // Cached effective volume 0..1 (0 when muted) — never blocks. Use in per-frame/poll paths.
        public static double GetEffectiveVolumeCached()
        {
            EnsureCacheFreshAsync();
            lock (_cacheLock) return _cachedMuted ? 0.0 : _cachedVolume;
        }

        // Invalidate the cache so the next read reflects a change we just made (mute/duck toggle).
        public static void InvalidateMuteVolumeCache()
        {
            lock (_cacheLock) _cacheStampUtc = DateTime.MinValue;
        }

        // Reads Spotify's current session mute state (false if not found).
        public static bool IsMuted()
        {
            return WithSpotifyVolume(vol =>
            {
                vol.GetMute(out bool muted);
                return muted;
            });
        }

        // Reads Spotify's session volume 0.0–1.0 (0 if muted or not found). Effective volume:
        // returns 0 when muted so a volume-based theme icon (ActiveMediaVolume==0) also reflects
        // the mute, and the real level otherwise.
        public static double GetEffectiveVolume()
        {
            double result = 0.0;
            WithSpotifyVolume(vol =>
            {
                vol.GetMute(out bool muted);
                if (muted) { result = 0.0; return true; }
                vol.GetMasterVolume(out float level);
                result = level;
                return true;
            });
            return result;
        }

        // Enumerates render sessions, finds the Spotify-owned one, and runs an action against
        // its ISimpleAudioVolume. Returns false if Spotify has no session or anything throws.
        private static bool WithSpotifyVolume(Func<ISimpleAudioVolume, bool> action)
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
                        if (pid == 0) continue;

                        string name;
                        try { name = Process.GetProcessById((int)pid).ProcessName; }
                        catch { continue; }
                        if (!string.Equals(name, SpotifyProcessName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // QI ISimpleAudioVolume off the session control (same as the meter QI
                        // in AudioSessionDetector).
                        if (control is ISimpleAudioVolume vol)
                            return action(vol);
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

        // IAudioSessionControl + IAudioSessionControl2 combined; vtable order must match the
        // Windows SDK exactly (same declaration as AudioSessionDetector).
        [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            int GetState(out int state);
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, [In] ref Guid context);
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, [In] ref Guid context);
            int GetGroupingParam(out Guid groupingParam);
            int SetGroupingParam([In] ref Guid groupingParam, [In] ref Guid context);
            int RegisterAudioSessionNotification(IntPtr client);
            int UnregisterAudioSessionNotification(IntPtr client);
            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetProcessId(out uint pid);
            [PreserveSig] int IsSystemSoundsSession();
            int SetDuckingPreference(int optOut);
        }

        // Minimal IAudioSessionControl — the object we enumerate and QI from.
        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            int GetState(out int state);
        }

        // Per-app volume/mute — QI'd off the session control. Vtable order verified live
        // (GetMute/SetMute round-trip returned correct values on a running Spotify).
        [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            int SetMasterVolume(float level, [In] ref Guid eventContext);
            int GetMasterVolume(out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, [In] ref Guid eventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        #endregion
    }
}
