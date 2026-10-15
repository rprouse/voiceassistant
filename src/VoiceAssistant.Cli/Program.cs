using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Spectre.Console.Cli;
using VoiceAssistant.Cli.Commands;
using VoiceAssistant.Cli.Infrastructure;
using VoiceAssistant.Core.Capture;

var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "VoiceAssistant", "logs", "cli-.log");

await using var serilog = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
    .CreateLogger();

// The builder supplies appsettings.json, environment and command-line configuration.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Logging.ClearProviders();
builder.Services.AddSerilog(serilog);
builder.Services.Configure<CaptureOptions>(builder.Configuration.GetSection(CaptureOptions.SectionName));

var app = new CommandApp(new TypeRegistrar(builder.Services));
app.Configure(config =>
{
    config.SetApplicationName("VoiceAssistant");
    config.AddCommand<RecordCommand>("record")
        .WithDescription("Record system audio (them.wav) and the microphone (me.wav) in the device's native format.")
        .WithExample("record", "--seconds", "30");
});

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

return await app.RunAsync(args, cancellation.Token);
