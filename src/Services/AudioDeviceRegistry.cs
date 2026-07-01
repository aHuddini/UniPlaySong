using System;
using System.Collections.Generic;
using System.Linq;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Central registry of audio-device holders (issue #81). The sleep coordinator calls
    // ReleaseAllDevices() on idle/lock/suspend to close EVERY open audio device so Windows
    // can sleep. Holders register on creation and unregister on dispose, so any future
    // device holder participates automatically. Thread-safe (releases may run off the UI thread).
    public class AudioDeviceRegistry
    {
        private readonly FileLogger _fileLogger;
        private readonly object _lock = new object();
        private readonly List<IAudioDeviceHolder> _holders = new List<IAudioDeviceHolder>();

        public AudioDeviceRegistry(FileLogger fileLogger)
        {
            _fileLogger = fileLogger;
        }

        public void Register(IAudioDeviceHolder holder)
        {
            if (holder == null) return;
            lock (_lock)
            {
                if (!_holders.Contains(holder)) _holders.Add(holder);
            }
        }

        public void Unregister(IAudioDeviceHolder holder)
        {
            if (holder == null) return;
            lock (_lock)
            {
                _holders.Remove(holder);
            }
        }

        public bool IsAnyDeviceOpen
        {
            get
            {
                lock (_lock)
                {
                    foreach (var h in _holders)
                    {
                        try { if (h.IsAudioDeviceOpen) return true; } catch { }
                    }
                    return false;
                }
            }
        }

        // Closes every currently-open holder. Returns how many were released. Logs the trigger
        // + each released label. A holder that throws does not abort the rest. Safe from any thread.
        public int ReleaseAllDevices(string reason)
        {
            List<IAudioDeviceHolder> snapshot;
            lock (_lock) { snapshot = _holders.ToList(); }

            var openOnes = new List<IAudioDeviceHolder>();
            foreach (var h in snapshot)
            {
                try { if (h.IsAudioDeviceOpen) openOnes.Add(h); } catch { }
            }

            if (openOnes.Count == 0)
            {
                _fileLogger?.Debug($"[Sleep] {reason} — no open audio devices to release");
                return 0;
            }

            _fileLogger?.Debug($"[Sleep] {reason} — releasing {openOnes.Count} audio device(s) so Windows can sleep");
            int released = 0;
            foreach (var h in openOnes)
            {
                try
                {
                    h.ReleaseAudioDevice();
                    released++;
                    _fileLogger?.Debug($"[Sleep]   released: {h.AudioDeviceLabel}");
                }
                catch (Exception ex)
                {
                    _fileLogger?.Debug($"[Sleep]   release failed for {h.AudioDeviceLabel}: {ex.Message}");
                }
            }
            return released;
        }
    }
}
