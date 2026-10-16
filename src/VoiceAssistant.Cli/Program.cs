using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Spectre.Console.Cli;
using VoiceAssistant.Cli.Commands;
using VoiceAssistant.Cli.Infrastructure;
using VoiceAssistant.Core;
using VoiceAssistant.Core.Capture;
using VoiceAssistant.Core.Pipeline;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

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
// Settings saved from the app's settings dialog override appsettings.json.
builder.Configuration.AddJsonFile(UserSettingsFile.DefaultPath, optional: true, reloadOnChange: false);
builder.Logging.ClearProviders();
builder.Services.AddSerilog(serilog);
builder.Services.Configure<CaptureOptions>(builder.Configuration.GetSection(CaptureOptions.SectionName));
builder.Services.Configure<WhisperOptions>(builder.Configuration.GetSection(WhisperOptions.SectionName));
builder.Services.Configure<VadOptions>(builder.Configuration.GetSection(VadOptions.SectionName));
builder.Services.Configure<HallucinationOptions>(builder.Configuration.GetSection(HallucinationOptions.SectionName));
builder.Services.Configure<TranscriptOptions>(builder.Configuration.GetSection(TranscriptOptions.SectionName));

var app = new CommandApp(new TypeRegistrar(builder.Services));
app.Configure(config =>
{
    config.SetApplicationName("VoiceAssistant");
    config.AddCommand<RecordCommand>("record")
        .WithDescription("Record system audio (them.wav) and the microphone (me.wav) in the device's native format.")
        .WithExample("record", "--seconds", "30");
    config.AddBranch("model", model =>
    {
        model.SetDescription("Manage Whisper models.");
        model.AddCommand<ModelGetCommand>("get")
            .WithDescription("Download a ggml Whisper model into the models folder.")
            .WithExample("model", "get", "base.en");
    });
    config.AddCommand<TranscribeCommand>("transcribe")
        .WithDescription("Transcribe a WAV file and print timestamped segments.")
        .WithExample("transcribe", "them.wav");
    config.AddCommand<LiveCommand>("live")
        .WithDescription("Caption live system audio and microphone.")
        .WithExample("live")
        .WithExample("live", "--naive");
});

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

return await app.RunAsync(args, cancellation.Token);
