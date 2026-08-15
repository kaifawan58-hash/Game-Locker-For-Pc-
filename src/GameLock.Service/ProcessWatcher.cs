using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;

namespace GameLock.Service
{
    /// <summary>
    /// Watches for new process creation via WMI's Win32_ProcessStartTrace (event-driven, near-zero
    /// idle CPU) and kills any process whose full executable path matches a locked game.
    /// A low-frequency periodic sweep acts as a safety net for anything the event misses
    /// (e.g. very short-lived launcher processes) and for already-running processes.
    /// </summary>
    public sealed class ProcessWatcher : IDisposable
    {
        private readonly Func<bool> _isLocked;
        private readonly Func<IReadOnlySet<string>> _getLockedPaths;
        private readonly Action<string> _log;

        private ManagementEventWatcher? _startWatcher;
        private Timer? _sweepTimer;
        private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

        public ProcessWatcher(Func<bool> isLocked, Func<IReadOnlySet<string>> getLockedPaths, Action<string> log)
        {
            _isLocked = isLocked;
            _getLockedPaths = getLockedPaths;
            _log = log;
        }

        public void Start()
        {
            try
            {
                var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
                _startWatcher = new ManagementEventWatcher(query);
                _startWatcher.EventArrived += OnProcessStarted;
                _startWatcher.Start();
                _log("Process start watcher (WMI trace) started.");
            }
            catch (Exception ex)
            {
                // WMI trace requires elevated/SYSTEM context; the periodic sweep below still
                // provides protection (with slightly higher latency) if this fails to start.
                _log($"WMI trace watcher unavailable ({ex.Message}); relying on periodic sweep only.");
            }

            _sweepTimer = new Timer(_ => SafeSweep(), null, TimeSpan.Zero, SweepInterval);
        }

        private void OnProcessStarted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                if (!_isLocked()) return;

                string processName = (string)e.NewEvent.Properties["ProcessName"].Value;
                uint pid = (uint)e.NewEvent.Properties["ProcessID"].Value;

                var lockedPaths = _getLockedPaths();
                if (lockedPaths.Count == 0) return;

                // Match by filename first (cheap), then confirm/kill by exact path when possible.
                bool nameMatches = lockedPaths.Any(p =>
                    string.Equals(Path.GetFileName(p), processName, StringComparison.OrdinalIgnoreCase));
                if (!nameMatches) return;

                TryKillIfLockedPath((int)pid, lockedPaths);
            }
            catch (Exception ex)
            {
                _log($"Error handling process-start event: {ex.Message}");
            }
        }

        private void SafeSweep()
        {
            try
            {
                if (!_isLocked()) return;
                var lockedPaths = _getLockedPaths();
                if (lockedPaths.Count == 0) return;

                foreach (var proc in Process.GetProcesses())
                {
                    using (proc)
                    {
                        TryKillIfLockedPath(proc.Id, lockedPaths, proc);
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"Sweep error: {ex.Message}");
            }
        }

        private void TryKillIfLockedPath(int pid, IReadOnlySet<string> lockedPaths, Process? existingHandle = null)
        {
            Process? proc = existingHandle;
            try
            {
                proc ??= Process.GetProcessById(pid);
                string? fullPath;
                try
                {
                    fullPath = proc.MainModule?.FileName;
                }
                catch
                {
                    // Access-denied reading MainModule (e.g. protected process) - fall back to name-only match.
                    fullPath = null;
                }

                bool isLockedTarget = fullPath != null
                    ? lockedPaths.Any(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase))
                    : lockedPaths.Any(p => string.Equals(Path.GetFileName(p), proc.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase));

                if (isLockedTarget)
                {
                    _log($"Terminating locked game process: {fullPath ?? proc.ProcessName} (PID {proc.Id})");
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch (ArgumentException)
            {
                // Process already exited - fine.
            }
            catch (Exception ex)
            {
                _log($"Could not terminate PID {pid}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _sweepTimer?.Dispose();
            if (_startWatcher != null)
            {
                try { _startWatcher.Stop(); } catch { /* ignore */ }
                _startWatcher.Dispose();
            }
        }
    }
}
