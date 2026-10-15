using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using VoiceAssistant.Core;
using VoiceAssistant.Core.Capture;

namespace VoiceAssistant.Cli.Commands
{
    internal sealed class RecordCommand : AsyncCommand<RecordCommand.Settings>
    {
        private readonly CaptureOptions _options;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<RecordCommand> _logger;

        public RecordCommand(IOptions<CaptureOptions> options, ILoggerFactory loggerFactory)
        {
            _options = options.Value;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<RecordCommand>();
        }

        internal sealed class Settings : CommandSettings
        {
            [CommandOption("-s|--seconds <SECONDS>")]
            [Description("How long to record. Ctrl+C stops early and still saves the files.")]
            [DefaultValue(30)]
            public int Seconds { get; set; }

            [CommandOption("-o|--output <DIRECTORY>")]
            [Description("Folder for them.wav and me.wav.")]
            [DefaultValue(".")]
            public string OutputDirectory { get; set; } = ".";

            public override ValidationResult Validate() =>
                Seconds > 0 ? ValidationResult.Success() : ValidationResult.Error("--seconds must be greater than zero.");
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            if (!_options.EnableLoopback && !_options.EnableMic)
            {
                AnsiConsole.MarkupLine("[red]Both captures are disabled in appsettings.json (Capture:EnableLoopback, Capture:EnableMic).[/]");
                return 1;
            }

            Directory.CreateDirectory(settings.OutputDirectory);
            var gapTolerance = TimeSpan.FromMilliseconds(_options.GapToleranceMs);
            var clock = new SessionClock();
            using var deviceMonitor = new DefaultDeviceMonitor(_loggerFactory.CreateLogger<DefaultDeviceMonitor>());

            var recordings = new List<Recording>();
            try
            {
                if (_options.EnableLoopback)
                    recordings.Add(new Recording(new LoopbackCapture(clock, _options, _loggerFactory.CreateLogger<LoopbackCapture>()), Path.Combine(settings.OutputDirectory, "them.wav")));
                if (_options.EnableMic)
                    recordings.Add(new Recording(new MicCapture(clock, _options, _loggerFactory.CreateLogger<MicCapture>()), Path.Combine(settings.OutputDirectory, "me.wav")));

                foreach (var recording in recordings)
                    recording.Start(gapTolerance);

                ShowDevices(recordings);

                await WaitAsync(clock, TimeSpan.FromSeconds(settings.Seconds), cancellationToken);

                foreach (var recording in recordings)
                    await recording.StopAsync();

                // Taken after every capture has drained, so no file holds audio past it
                // and padding to it gives every file the same duration.
                var stopAt = clock.Elapsed;
                foreach (var recording in recordings)
                    recording.PadTo(stopAt);

                _logger.LogInformation("Recording stopped at {StopAt}", stopAt);
                ShowResults(recordings);
                return 0;
            }
            finally
            {
                foreach (var recording in recordings)
                    await recording.DisposeAsync();
            }
        }

        private static async Task WaitAsync(SessionClock clock, TimeSpan duration, CancellationToken cancellationToken)
        {
            await AnsiConsole.Progress()
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new ElapsedTimeColumn())
                .StartAsync(async progress =>
                {
                    var task = progress.AddTask("Recording (Ctrl+C to stop)", maxValue: duration.TotalSeconds);
                    try
                    {
                        while (clock.Elapsed < duration)
                        {
                            task.Value = clock.Elapsed.TotalSeconds;
                            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Ctrl+C: stop early and keep what was recorded.
                    }

                    task.Value = Math.Min(clock.Elapsed.TotalSeconds, duration.TotalSeconds);
                    task.StopTask();
                });
        }

        private static void ShowDevices(IEnumerable<Recording> recordings)
        {
            var table = new Table().AddColumns("Source", "Device", "Native format");
            foreach (var r in recordings)
                table.AddRow(r.Capture.Source.ToString(), Markup.Escape(r.Capture.DeviceName), Markup.Escape(r.Capture.WaveFormat.ToString()));
            AnsiConsole.Write(table);
        }

        private static void ShowResults(IEnumerable<Recording> recordings)
        {
            var table = new Table().AddColumns("File", "Duration", "Silence inserted", "Dropped buffers");
            foreach (var r in recordings)
            {
                table.AddRow(
                    Markup.Escape(Path.GetFullPath(r.Path)),
                    $"{r.Duration.TotalSeconds:F3} s",
                    $"{r.SilenceInserted.TotalSeconds:F3} s",
                    r.Capture.DroppedBufferCount.ToString());
            }
            AnsiConsole.Write(table);
        }

        /// <summary>One capture pumped into one WAV file.</summary>
        private sealed class Recording : IAsyncDisposable
        {
            private PaddedWaveWriter? _writer;
            private Task? _pump;

            public Recording(IAudioSourceCapture capture, string path)
            {
                Capture = capture;
                Path = path;
            }

            public IAudioSourceCapture Capture { get; }

            public string Path { get; }

            public TimeSpan Duration { get; private set; }

            public TimeSpan SilenceInserted { get; private set; }

            public void Start(TimeSpan gapTolerance)
            {
                Capture.Start();
                _writer = new PaddedWaveWriter(File.Create(Path), Capture.WaveFormat, gapTolerance);
                _pump = PumpAsync(_writer);
            }

            public async Task StopAsync()
            {
                await Capture.StopAsync();
                if (_pump is not null)
                    await _pump;
            }

            public void PadTo(TimeSpan stopAt)
            {
                if (_writer is not null)
                {
                    _writer.PadTo(stopAt);
                    Duration = _writer.Duration;
                    SilenceInserted = _writer.SilenceInserted;
                }
            }

            public async ValueTask DisposeAsync()
            {
                _writer?.Dispose();
                await Capture.DisposeAsync();
            }

            private async Task PumpAsync(PaddedWaveWriter writer)
            {
                await foreach (var buffer in Capture.ReadAllAsync())
                    writer.Write(buffer);
            }
        }
    }
}
