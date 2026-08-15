using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameLock.Common
{
    /// <summary>
    /// Common named-pipe name + JSON framing used by both the service (server) and the GUI (client).
    /// The pipe's ACL only allows Administrators + SYSTEM to connect, so a standard/child account
    /// cannot open a raw connection and cannot spam the endpoint. Unlock still requires the correct
    /// password on top of that.
    /// </summary>
    public static class PipeChannel
    {
        public const string PipeName = "GameLockControlPipe";

        public static PipeSecurity BuildServerSecurity()
        {
            var security = new PipeSecurity();
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            security.AddAccessRule(new PipeAccessRule(administrators, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
            // Note: BUILTIN\Users is intentionally NOT granted access here.
            return security;
        }

        public static async Task WriteMessageAsync<T>(PipeStream pipe, T message)
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message);
            byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);
            await pipe.WriteAsync(lengthPrefix, 0, 4);
            await pipe.WriteAsync(payload, 0, payload.Length);
            await pipe.FlushAsync();
        }

        public static async Task<T?> ReadMessageAsync<T>(PipeStream pipe)
        {
            byte[] lengthPrefix = new byte[4];
            int read = await ReadExactAsync(pipe, lengthPrefix, 4);
            if (read < 4) return default;

            int length = BitConverter.ToInt32(lengthPrefix, 0);
            if (length <= 0 || length > 1_000_000) return default;

            byte[] payload = new byte[length];
            await ReadExactAsync(pipe, payload, length);
            return JsonSerializer.Deserialize<T>(payload);
        }

        private static async Task<int> ReadExactAsync(PipeStream pipe, byte[] buffer, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int n = await pipe.ReadAsync(buffer, totalRead, count - totalRead);
                if (n == 0) break;
                totalRead += n;
            }
            return totalRead;
        }
    }
}
