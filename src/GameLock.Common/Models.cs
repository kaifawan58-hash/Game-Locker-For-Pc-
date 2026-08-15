using System;
using System.Collections.Generic;

namespace GameLock.Common
{
    /// <summary>
    /// Persisted configuration: password hash + list of protected game executables.
    /// Serialized to JSON and stored in the protected ProgramData folder.
    /// </summary>
    public class GameLockConfig
    {
        public string PasswordHashBase64 { get; set; } = "";
        public string PasswordSaltBase64 { get; set; } = "";
        public int PasswordIterations { get; set; } = 200_000;

        /// <summary>Full paths to configured game executables.</summary>
        public List<string> GamePaths { get; set; } = new List<string>();
    }

    /// <summary>Commands the GUI can send to the background service over the named pipe.</summary>
    public enum PipeCommandType
    {
        GetStatus,
        ReloadConfig,
        LockNow,
        Unlock,          // payload = plaintext password, verified by the service
        Ping
    }

    public class PipeRequest
    {
        public PipeCommandType Command { get; set; }
        public string? Payload { get; set; }
    }

    public class PipeResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool Locked { get; set; }
    }
}
