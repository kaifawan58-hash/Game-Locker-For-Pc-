using GameLock.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;

var builder = Host.CreateApplicationBuilder(args);

// Registers the process as a proper Windows Service (SCM start/stop, recovery, etc.)
// when launched by the Service Control Manager; runs as a normal console app otherwise
// (handy for `dotnet run` testing during development).
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "GameLockService";
});

builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "GameLockService";
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
