using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace GameLock.Common
{
    /// <summary>
    /// Reads/writes GameLockConfig from a protected folder under C:\ProgramData\GameLock.
    /// Only Administrators and SYSTEM get write access; ordinary users get read-only (or nothing).
    /// </summary>
    public static class ConfigStore
    {
        public static readonly string RootFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameLock");

        public static readonly string ConfigPath = Path.Combine(RootFolder, "config.json");

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        /// <summary>
        /// Creates C:\ProgramData\GameLock and locks its ACL down to Administrators + SYSTEM (full control)
        /// and Users (read + execute only, no write/delete). Must be called elevated.
        /// </summary>
        public static void EnsureProtectedFolder()
        {
            Directory.CreateDirectory(RootFolder);

            var dirInfo = new DirectoryInfo(RootFolder);
            var security = new DirectorySecurity();

            // Disable inheritance and wipe existing rules so we start from a known-clean ACL.
            security.SetAccessRuleProtection(true, false);

            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

            var inheritFlags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

            security.AddAccessRule(new FileSystemAccessRule(
                administrators, FileSystemRights.FullControl, inheritFlags, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                system, FileSystemRights.FullControl, inheritFlags, PropagationFlags.None, AccessControlType.Allow));
            // Ordinary users (the child's standard account) can read the folder/status but cannot write/delete.
            security.AddAccessRule(new FileSystemAccessRule(
                users,
                FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory,
                inheritFlags, PropagationFlags.None, AccessControlType.Allow));

            dirInfo.SetAccessControl(security);
        }

        public static bool ConfigExists() => File.Exists(ConfigPath);

        public static GameLockConfig Load()
        {
            if (!File.Exists(ConfigPath))
                return new GameLockConfig();

            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<GameLockConfig>(json) ?? new GameLockConfig();
        }

        /// <summary>Writes the config file atomically. Caller must have write access (Administrators/SYSTEM only).</summary>
        public static void Save(GameLockConfig config)
        {
            EnsureProtectedFolder();
            string json = JsonSerializer.Serialize(config, JsonOpts);
            string tmpPath = ConfigPath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Copy(tmpPath, ConfigPath, overwrite: true);
            File.Delete(tmpPath);
        }
    }
}
