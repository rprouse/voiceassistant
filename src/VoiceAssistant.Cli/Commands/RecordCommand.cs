using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using VoiceAssistant.Core;
using VoiceAssistant.Core.Capture;
using VoiceAssistant.Core.Conversion;

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
            [Description("Folder for the WAV files.")]
            [DefaultValue(".")]
            public string OutputDirectory { get; set; } = ".";

            [CommandOption("-c|--converted")]
            [Description("Also write them-16k.wav and me-16k.wav: 16 kHz mono float, as fed to Whisper.")]
            public bool Converted { get; set; }

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
                    recordings.Add(new Recording(new LoopbackCapture(clock, _options, _loggerFactory.CreateLogger<LoopbackCapture>()), settings, "them", _logger));
                if (_options.EnableMic)
                    recordings.Add(new Recording(new MicCapture(clock, _options, _loggerFactory.CreateLogger<MicCapture>()), settings, "me", _logger));

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
                AnsiConsole.MarkupLineInterpolated($"Saved to [bold]{Path.GetFullPath(settings.OutputDirectory)}[/]");
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
            var table = new Table().AddColumns("File", "Duration", "Silence inserted", "Dropped buffers", "Device glitches");
            foreach (var r in recordings)
            {
                foreach (var (path, writer) in r.Outputs)
                {
                    table.AddRow(
                        Markup.Escape(Path.GetFileName(path)),
                        $"{writer.Duration.TotalSeconds:F3} s",
                        $"{writer.SilenceInserted.TotalSeconds:F3} s",
                        r.Capture.DroppedBufferCount.ToString(),
                        r.Capture.DiscontinuityCount.ToString());
                }
            }
            AnsiConsole.Write(table);
        }

        /// <summary>One capture pumped into its native WAV file and, optionally, a converted 16 kHz one.</summary>
        private sealed class Recording : IAsyncDisposable
        {
            private readonly string _path;
            private readonly string? _convertedPath;
            private readonly ILogger _logger;
            private readonly List<(string Path, PaddedWaveWriter Writer)> _outputs = [];
            private Task? _pump;

            public Recording(IAudioSourceCapture capture, Settings settings, string name, ILogger logger)
            {
                Capture = capture;
                _path = Path.Combine(settings.OutputDirectory, $"{name}.wav");
                _convertedPath = settings.Converted ? Path.Combine(settings.OutputDirectory, $"{name}-16k.wav") : null;
                _logger = logger;
            }

            public IAudioSourceCapture Capture { get; }

            public IReadOnlyList<(string Path, PaddedWaveWriter Writer)> Outputs => _outputs;

            public void Start(TimeSpan gapTolerance)
            {
                Capture.Start();

                var native = new PaddedWaveWriter(File.Create(_path), Capture.WaveFormat, gapTolerance);
                _outputs.Add((_path, native));

                PaddedWaveWriter? converted = null;
                AudioConverter? converter = null;
                if (_convertedPath is not null)
                {
                    converter = new AudioConverter(Capture.WaveFormat);
                    converted = new PaddedWaveWriter(File.Create(_convertedPath), AudioConverter.OutputFormat, gapTolerance);
                    _outputs.Add((_convertedPath, converted));
                }

                _pump = PumpAsync(native, converter, converted);
            }

            public async Task StopAsync()
            {
                await Capture.StopAsync();
                if (_pump is not null)
                    await _pump;
            }

            public void PadTo(TimeSpan stopAt)
            {
                foreach (var (_, writer) in _outputs)
                    writer.PadTo(stopAt);
            }

            public async ValueTask DisposeAsync()
            {
                foreach (var (_, writer) in _outputs)
                    writer.Dispose();
                await Capture.DisposeAsync();
            }

            private async Task PumpAsync(PaddedWaveWriter native, AudioConverter? converter, PaddedWaveWriter? converted)
            {
                var conversionTime = TimeSpan.Zero;
                var format = Capture.WaveFormat;
                var warned = false;
                await foreach (var buffer in Capture.ReadAllAsync())
                {
                    // A WAV file has one format. If the capture moved to a device with another,
                    // skip its audio; the gap is filled with silence so the timeline still holds.
                    if (!buffer.Format.Equals(format))
                    {
                        if (!warned)
                            _logger.LogWarning("{Source} switched to a device with a different format ({Format}); its audio is not recorded", Capture.Source, buffer.Format);
                        warned = true;
                        continue;
                    }

                    native.Write(buffer);

                    if (converter is not null && converted is not null)
                    {
                        var started = Stopwatch.GetTimestamp();
                        var chunk = converter.Convert(buffer);
                        conversionTime += Stopwatch.GetElapsedTime(started);
                        converted.Write(chunk);
                    }
                }

                if (converter is not null)
                    _logger.LogDebug("{Source} conversion took {ConversionMs:F1} ms for {AudioSeconds:F1} s of audio", Capture.Source, conversionTime.TotalMilliseconds, native.Duration.TotalSeconds);
            }
        }
    }
}
