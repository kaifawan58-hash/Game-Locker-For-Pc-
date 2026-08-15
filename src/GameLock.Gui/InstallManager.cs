using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using GameLock.Common;

namespace GameLock.Gui
{
    /// <summary>
    /// Installs/uninstalls the GameLockService Windows Service via sc.exe, and creates the
    /// protected ProgramData configuration folder. All operations require the caller (this GUI)
    /// to already be running elevated - the process manifest enforces that at launch.
    /// </summary>
    public static class InstallManager
    {
        public const string ServiceName = "GameLockService";

        /// <summary>Path where the GUI's installer copies the service binaries. Adjust if you change layout.</summary>
        public static string ServiceInstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameLock", "Service");

        public static string ServiceExePath => Path.Combine(ServiceInstallDir, "GameLockService.exe");

        public static bool IsServiceInstalled()
        {
            try
            {
                using var sc = new ServiceController(ServiceName);
                var _ = sc.Status; // throws if not installed
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static (bool ok, string message) InstallService()
        {
            if (!File.Exists(ServiceExePath))
                return (false, $"Service executable not found at {ServiceExePath}. Run the installer/publish step first.");

            ConfigStore.EnsureProtectedFolder();

            if (IsServiceInstalled())
                return (true, "Service already installed.");

            var create = RunSc($"create {ServiceName} binPath= \"{ServiceExePath}\" start= auto obj= LocalSystem DisplayName= \"GameLock Protection Service\"");
            if (create.exitCode != 0)
                return (false, $"sc create failed: {create.output}");

            RunSc($"description {ServiceName} \"Enforces parental game locks; runs at boot as SYSTEM.\"");
            RunSc($"failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000");

            var start = RunSc($"start {ServiceName}");
            if (start.exitCode != 0)
                return (false, $"Service installed but failed to start: {start.output}");

            return (true, "Protection service installed and started.");
        }

        public static (bool ok, string message) UninstallService()
        {
            if (!IsServiceInstalled())
                return (true, "Service is not installed.");

            RunSc($"stop {ServiceName}");
            var delete = RunSc($"delete {ServiceName}");
            if (delete.exitCode != 0)
                return (false, $"sc delete failed: {delete.output}");

            return (true, "Protection service removed. Game locking is no longer enforced.");
        }

        private static (int exitCode, string output) RunSc(string arguments)
        {
            var psi = new ProcessStartInfo("sc.exe", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode, string.IsNullOrWhiteSpace(stdout) ? stderr : stdout);
        }
    }
}
