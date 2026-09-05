using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using UniPlaySong.Common;

namespace UniPlaySong.Services.Jingles
{
    // Runs UpsSound.exe and hands achievement sounds to it.
    //
    // The reason this exists is a PID: PlayniteAchievements captures audio by process tree, and a
    // chime played from Playnite's own tree cannot be separated from an emulator Playnite launched.
    // See docs/dev_docs/features/JINGLE_SOUND_HOST.md.
    //
    // Every failure here has to end in "false" so the caller plays the sound in process. A capture
    // feature must never become a new way for achievement sounds to go missing, and the ways this
    // can fail are not exotic: the exe can be missing from a partial install, quarantined by
    // antivirus, killed by a user, or simply slow to answer on a loaded machine.
    public sealed class ProcessJingleSoundHost : IJingleSoundHost
    {
        // How long to wait for the helper's ack before giving up and playing in process. Generous
        // enough for a busy machine, short enough that nobody perceives the fallback as a delay.
        private const int AckTimeoutMs = 250;

        private readonly string _exePath;
        private readonly FileLogger _fileLogger;
        private readonly Action<string, string> _userWarning;
        private readonly Action<string> _withdrawWarning;
        private readonly object _gate = new object();

        private Process _process;
        private IntPtr _job = IntPtr.Zero;
        private ManualResetEventSlim _ack;
        private volatile bool _lastAckWasOk;
        private int _restarts;
        private bool _permanentlyFailed;
        private string _failureReason;

        public ProcessJingleSoundHost(
            string exePath,
            FileLogger fileLogger = null,
            Action<string, string> userWarning = null,
            Action<string> withdrawWarning = null)
        {
            _exePath = exePath;
            _fileLogger = fileLogger;
            _userWarning = userWarning;
            _withdrawWarning = withdrawWarning;
        }

        public int ProcessId
        {
            get
            {
                lock (_gate)
                {
                    try { return _process != null && !_process.HasExited ? _process.Id : 0; }
                    catch { return 0; }
                }
            }
        }

        public bool IsRunning => ProcessId != 0;

        // Why it is not running, for the API the consumer reads. Null while healthy.
        public string FailureReason
        {
            get { lock (_gate) return _permanentlyFailed || _process == null ? _failureReason : null; }
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_permanentlyFailed) return;
                StartLocked();
            }
        }

        private void StartLocked()
        {
            try
            {
                if (_process != null && !_process.HasExited) return;
            }
            catch { /* HasExited on a disposed process */ }

            if (!File.Exists(_exePath))
            {
                // Most likely antivirus removed it after packaging, or a partial install.
                Fail("quarantined", $"sound host not found at {_exePath}");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _exePath,
                    Arguments = $"--parent {Process.GetCurrentProcess().Id}",
                    UseShellExecute = false,       // required for the redirects
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };

                var proc = Process.Start(psi);
                if (proc == null)
                {
                    Fail("failed", "Process.Start returned null");
                    return;
                }

                _process = proc;
                _ack = new ManualResetEventSlim(false);
                proc.OutputDataReceived += OnHostOutput;
                proc.BeginOutputReadLine();

                AssignToJobLocked(proc);

                _fileLogger?.Lifecycle($"Sound host started (pid {proc.Id})");
                _withdrawWarning?.Invoke(WarningId);
                _failureReason = null;
            }
            catch (Exception ex)
            {
                Fail("failed", $"could not start the sound host: {ex.Message}");
            }
        }

        public bool TryPlay(string filePath, double volume)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            lock (_gate)
            {
                if (_permanentlyFailed) return false;

                if (!EnsureAliveLocked()) return false;

                try
                {
                    _ack.Reset();
                    _lastAckWasOk = false;

                    var line = "play "
                        + volume.ToString("0.000", CultureInfo.InvariantCulture)
                        + " " + filePath;

                    _process.StandardInput.WriteLine(line);
                    _process.StandardInput.Flush();
                }
                catch (Exception ex)
                {
                    // Pipe broken: the helper died between the liveness check and the write.
                    _fileLogger?.Warn($"Sound host write failed: {ex.Message}");
                    KillLocked();
                    return false;
                }
            }

            // Waited on outside the lock: the ack arrives on the output-reader thread, which would
            // otherwise be blocked behind us.
            if (!_ack.Wait(AckTimeoutMs))
            {
                _fileLogger?.Warn($"Sound host did not acknowledge within {AckTimeoutMs}ms - playing in process");
                return false;
            }

            return _lastAckWasOk;
        }

        // Restarts once after an unexpected exit; a second failure gives up for the session rather
        // than respawning a process that clearly cannot run here.
        private bool EnsureAliveLocked()
        {
            bool alive;
            try { alive = _process != null && !_process.HasExited; }
            catch { alive = false; }

            if (alive) return true;

            if (_restarts >= 1)
            {
                Fail("failed", "the sound host stopped twice; achievement sounds will play in Playnite");
                return false;
            }

            _restarts++;
            _fileLogger?.Warn("Sound host is not running - restarting once");
            StartLocked();

            try { return _process != null && !_process.HasExited; }
            catch { return false; }
        }

        private void OnHostOutput(object sender, DataReceivedEventArgs e)
        {
            var line = e.Data;
            if (string.IsNullOrEmpty(line)) return;

            if (line.StartsWith("ok ", StringComparison.Ordinal))
            {
                _lastAckWasOk = true;
                _ack?.Set();
                return;
            }

            if (line.StartsWith("err ", StringComparison.Ordinal))
            {
                _fileLogger?.Warn($"Sound host declined: {line}");
                _lastAckWasOk = false;
                _ack?.Set();
                return;
            }

            // "ready" and "done <id>" need no action: nothing waits on the sound finishing, because
            // achievement sounds fire over a running game with no pause to release.
            _fileLogger?.Debug($"[SoundHost] {line}");
        }

        public void Stop()
        {
            lock (_gate)
            {
                try
                {
                    if (_process != null && !_process.HasExited)
                    {
                        _process.StandardInput.WriteLine("quit");
                        _process.StandardInput.Flush();
                        if (!_process.WaitForExit(500)) _process.Kill();
                    }
                }
                catch { /* already gone */ }

                KillLocked();
                CloseJobLocked();
            }
        }

        private void KillLocked()
        {
            try { if (_process != null && !_process.HasExited) _process.Kill(); } catch { }
            try { _process?.Dispose(); } catch { }
            _process = null;
            try { _ack?.Dispose(); } catch { }
            _ack = null;
        }

        internal const string WarningId = "ups-sound-host-unavailable";

        private void Fail(string reason, string detail)
        {
            _permanentlyFailed = true;
            _failureReason = reason;
            _fileLogger?.Warn($"Sound host unavailable ({reason}): {detail}");

            _userWarning?.Invoke(WarningId,
                "UniPlaySong could not start its separate sound player, so achievement sounds are "
                + "playing from Playnite as usual." + Environment.NewLine + Environment.NewLine
                + "Recording tools that separate audio by process will not be able to isolate them."
                + Environment.NewLine + Environment.NewLine
                + "Turn off the separate sound player under UniPlaySong Settings → Advanced if you "
                + "do not need it.");
        }

        #region Job object — the helper must not outlive Playnite

        // A job object with KILL_ON_JOB_CLOSE is the only reliable way to guarantee the helper dies
        // with Playnite: the handle closes when this process ends, however it ends, including a
        // crash. The helper also watches the parent pid itself, but that is the belt, not the braces.
        private void AssignToJobLocked(Process proc)
        {
            try
            {
                if (_job == IntPtr.Zero)
                {
                    _job = CreateJobObject(IntPtr.Zero, null);
                    if (_job == IntPtr.Zero) return;

                    var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE };
                    var extended = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = info };

                    int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr ptr = Marshal.AllocHGlobal(length);
                    try
                    {
                        Marshal.StructureToPtr(extended, ptr, false);
                        SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ptr, (uint)length);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }

                AssignProcessToJobObject(_job, proc.Handle);
            }
            catch (Exception ex)
            {
                // Not fatal: the helper's own parent watchdog still ends it.
                _fileLogger?.Debug($"Sound host job assignment failed: {ex.Message}");
            }
        }

        private void CloseJobLocked()
        {
            if (_job == IntPtr.Zero) return;
            try { CloseHandle(_job); } catch { }
            _job = IntPtr.Zero;
        }

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr hJob, int infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll")]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        #endregion
    }
}
