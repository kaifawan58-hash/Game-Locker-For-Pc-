using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.IO.Pipes.AccessControl;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameLock.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameLock.Service
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly object _stateLock = new();

        // In-memory only: this is what makes the unlock state NOT survive a reboot.
        // The service always starts up with _locked = true, and there is no code path
        // anywhere that persists an "unlocked" flag to disk.
        private bool _locked = true;
        private GameLockConfig _config = new();
        private HashSet<string> _lockedPathSet = new(StringComparer.OrdinalIgnoreCase);

        private ProcessWatcher? _watcher;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            ReloadConfigFromDisk();

            _watcher = new ProcessWatcher(
                isLocked: () => { lock (_stateLock) { return _locked; } },
                getLockedPaths: () => { lock (_stateLock) { return _lockedPathSet; } },
                log: msg => _logger.LogInformation(msg));
            _watcher.Start();

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("GameLock service started. Protection is LOCKED by default on every boot.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunPipeServerOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pipe server iteration failed; restarting listener.");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }

        private async Task RunPipeServerOnceAsync(CancellationToken token)
        {
            using var server = NamedPipeServerStreamAcl.Create(
                PipeChannel.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0, 0,
                PipeChannel.BuildServerSecurity());

            await server.WaitForConnectionAsync(token);

            var request = await PipeChannel.ReadMessageAsync<PipeRequest>(server);
            var response = HandleRequest(request);
            await PipeChannel.WriteMessageAsync(server, response);

            server.Disconnect();
        }

        private PipeResponse HandleRequest(PipeRequest? request)
        {
            if (request == null)
                return new PipeResponse { Success = false, Message = "Malformed request." };

            switch (request.Command)
            {
                case PipeCommandType.Ping:
                    return new PipeResponse { Success = true, Message = "pong", Locked = GetLocked() };

                case PipeCommandType.GetStatus:
                    return new PipeResponse { Success = true, Message = "ok", Locked = GetLocked() };

                case PipeCommandType.ReloadConfig:
                    ReloadConfigFromDisk();
                    return new PipeResponse { Success = true, Message = "Configuration reloaded.", Locked = GetLocked() };

                case PipeCommandType.LockNow:
                    SetLocked(true);
                    // The watcher's periodic sweep (every 5s) will terminate any already-running
                    // locked game; no extra action needed here.
                    return new PipeResponse { Success = true, Message = "Games locked.", Locked = true };

                case PipeCommandType.Unlock:
                    return HandleUnlock(request.Payload);

                default:
                    return new PipeResponse { Success = false, Message = "Unknown command." };
            }
        }

        private PipeResponse HandleUnlock(string? password)
        {
            // Always re-read the config file so a stale in-memory hash can never grant access.
            ReloadConfigFromDisk();

            if (string.IsNullOrEmpty(_config.PasswordHashBase64))
                return new PipeResponse { Success = false, Message = "No password configured yet.", Locked = GetLocked() };

            bool ok = PasswordHasher.Verify(
                password ?? "",
                _config.PasswordHashBase64,
                _config.PasswordSaltBase64,
                _config.PasswordIterations);

            if (!ok)
            {
                _logger.LogWarning("Unlock attempt failed: incorrect password.");
                return new PipeResponse { Success = false, Message = "Incorrect password.", Locked = GetLocked() };
            }

            SetLocked(false);
            _logger.LogInformation("Games unlocked by authenticated parent. Will re-lock automatically on next boot.");
            return new PipeResponse { Success = true, Message = "Unlocked until next restart.", Locked = false };
        }

        private void ReloadConfigFromDisk()
        {
            var cfg = ConfigStore.Load();
            lock (_stateLock)
            {
                _config = cfg;
                _lockedPathSet = new HashSet<string>(cfg.GamePaths, StringComparer.OrdinalIgnoreCase);
            }
        }

        private bool GetLocked() { lock (_stateLock) { return _locked; } }

        private void SetLocked(bool value) { lock (_stateLock) { _locked = value; } }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _watcher?.Dispose();
            await base.StopAsync(cancellationToken);
        }
    }
}
