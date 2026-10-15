using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using VoiceAssistant.Core;
using VoiceAssistant.Core.Capture;
using VoiceAssistant.Core.Conversion;
using VoiceAssistant.Core.Segmentation;
using VoiceAssistant.Core.Transcription;

namespace VoiceAssistant.Cli.Commands
{
    internal sealed class LiveCommand : AsyncCommand<LiveCommand.Settings>
    {
        private readonly CaptureOptions _captureOptions;
        private readonly WhisperOptions _whisperOptions;
        private readonly ILoggerFactory _loggerFactory;

        public LiveCommand(IOptions<CaptureOptions> captureOptions, IOptions<WhisperOptions> whisperOptions, ILoggerFactory loggerFactory)
        {
            _captureOptions = captureOptions.Value;
            _whisperOptions = whisperOptions.Value;
            _loggerFactory = loggerFactory;
        }

        internal sealed class Settings : CommandSettings
        {
            [CommandOption("--naive")]
            [Description("Transcribe fixed, non-overlapping chunks (the stage 3 baseline).")]
            public bool Naive { get; set; }

            [CommandOption("--chunk-seconds <SECONDS>")]
            [Description("Chunk length for --naive.")]
            [DefaultValue(5)]
            public int ChunkSeconds { get; set; }

            [CommandOption("-s|--seconds <SECONDS>")]
            [Description("Stop after this many seconds. By default, runs until Ctrl+C.")]
            public int? Seconds { get; set; }

            public override ValidationResult Validate()
            {
                if (!Naive)
                    return ValidationResult.Error("Utterance-based captions arrive in stage 4. Use --naive for now.");
                if (ChunkSeconds <= 0)
                    return ValidationResult.Error("--chunk-seconds must be greater than zero.");
                return Seconds is null or > 0 ? ValidationResult.Success() : ValidationResult.Error("--seconds must be greater than zero.");
            }
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            if (!_captureOptions.EnableLoopback && !_captureOptions.EnableMic)
            {
                AnsiConsole.MarkupLine("[red]Both captures are disabled in appsettings.json (Capture:EnableLoopback, Capture:EnableMic).[/]");
                return 1;
            }

            using var transcriber = AnsiConsole.Status().Start("Loading model...", _ =>
                new WhisperTranscriber(_whisperOptions, _loggerFactory.CreateLogger<WhisperTranscriber>()));

            var clock = new SessionClock();
            using var deviceMonitor = new DefaultDeviceMonitor(_loggerFactory.CreateLogger<DefaultDeviceMonitor>());
            var captures = new List<IAudioSourceCapture>();
            if (_captureOptions.EnableLoopback)
                captures.Add(new LoopbackCapture(clock, _captureOptions, _loggerFactory.CreateLogger<LoopbackCapture>()));
            if (_captureOptions.EnableMic)
                captures.Add(new MicCapture(clock, _captureOptions, _loggerFactory.CreateLogger<MicCapture>()));

            // Both sources share one transcriber through one queue. Wait, never drop:
            // if transcription falls behind, back-pressure reaches the capture queues,
            // which drop (and count) the oldest audio instead.
            var windows = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.Wait });

            try
            {
                foreach (var capture in captures)
                {
                    capture.Start();
                    AnsiConsole.MarkupLineInterpolated($"[grey]{capture.Source}: {capture.DeviceName}[/]");
                }

                AnsiConsole.MarkupLineInterpolated(
                    $"[grey]{transcriber.ModelName} on {transcriber.Runtime}, {settings.ChunkSeconds} s chunks. Ctrl+C to stop.[/]");
                AnsiConsole.WriteLine();

                var chunkLength = TimeSpan.FromSeconds(settings.ChunkSeconds);
                var pumps = captures.Select(capture => PumpAsync(capture, chunkLength, windows.Writer)).ToList();
                var transcribing = TranscribeAsync(transcriber, windows.Reader);

                try
                {
                    await Task.Delay(settings.Seconds is { } seconds ? TimeSpan.FromSeconds(seconds) : Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Ctrl+C: stop capturing, then finish transcribing what was captured.
                }

                AnsiConsole.MarkupLine("[grey]Stopping...[/]");
                foreach (var capture in captures)
                    await capture.StopAsync();
                await Task.WhenAll(pumps);
                windows.Writer.Complete();
                await transcribing;

                foreach (var capture in captures)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[grey]{capture.Source}: {capture.DroppedBufferCount} dropped buffers, {capture.DiscontinuityCount} device glitches[/]");
                }

                return 0;
            }
            finally
            {
                foreach (var capture in captures)
                    await capture.DisposeAsync();
            }
        }

        private static async Task PumpAsync(IAudioSourceCapture capture, TimeSpan chunkLength, ChannelWriter<AudioChunk> windows)
        {
            var converter = new AudioConverter(capture.WaveFormat);
            var chunker = new FixedChunker(chunkLength);

            await foreach (var chunk in converter.ConvertAllAsync(capture.ReadAllAsync()))
            {
                foreach (var window in chunker.Add(chunk))
                    await windows.WriteAsync(window);
            }

            if (chunker.Flush() is { } last)
                await windows.WriteAsync(last);
        }

        private static async Task TranscribeAsync(ITranscriber transcriber, ChannelReader<AudioChunk> windows)
        {
            await foreach (var window in windows.ReadAllAsync())
            {
                var audio = TimeSpan.FromSeconds((double)window.Samples.Length / AudioConverter.OutputSampleRate);
                var started = Stopwatch.GetTimestamp();
                var segments = new List<TranscriptSegment>();

                await foreach (var segment in transcriber.TranscribeAsync(window.Source, window.Timestamp, window.Samples, prompt: null, CancellationToken.None))
                    segments.Add(segment);

                var rtf = Stopwatch.GetElapsedTime(started) / audio;
                foreach (var segment in segments)
                    TranscriptConsole.WriteSegment(segment, $"(RTF {rtf:F2})");
            }
        }
    }
}
