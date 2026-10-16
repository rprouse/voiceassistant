using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using VoiceAssistant.App.ViewModels;
using VoiceAssistant.Core;
using VoiceAssistant.Core.Capture;
using VoiceAssistant.Core.Pipeline;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.App
{
    public partial class App : Application
    {
        private IHost? _host;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoiceAssistant", "logs", "app-.log");
            var serilog = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = e.Args,
                ContentRootPath = AppContext.BaseDirectory
            });
            // Settings saved from the settings dialog override appsettings.json.
            builder.Configuration.AddJsonFile(UserSettingsFile.DefaultPath, optional: true, reloadOnChange: false);
            builder.Logging.ClearProviders();
            builder.Services.AddSerilog(serilog, dispose: true);
            builder.Services.Configure<CaptureOptions>(builder.Configuration.GetSection(CaptureOptions.SectionName));
            builder.Services.Configure<WhisperOptions>(builder.Configuration.GetSection(WhisperOptions.SectionName));
            builder.Services.Configure<VadOptions>(builder.Configuration.GetSection(VadOptions.SectionName));
            builder.Services.Configure<HallucinationOptions>(builder.Configuration.GetSection(HallucinationOptions.SectionName));
            builder.Services.Configure<TranscriptOptions>(builder.Configuration.GetSection(TranscriptOptions.SectionName));
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddSingleton<MainWindow>();

            _host = builder.Build();
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            DispatcherUnhandledException += (_, args) => logger.LogError(args.Exception, "Unhandled UI exception");

            await _host.StartAsync();

            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();

            await _host.Services.GetRequiredService<MainViewModel>().InitializeAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
