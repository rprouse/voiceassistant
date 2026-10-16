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
using VoiceAssistant.Core.Pipeline;
using VoiceAssistant.Core.Segmentation;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.Cli.Commands
{
    internal sealed class LiveCommand : AsyncCommand<LiveCommand.Settings>
    {
        private readonly CaptureOptions _captureOptions;
        private readonly WhisperOptions _whisperOptions;
        private readonly VadOptions _vadOptions;
        private readonly HallucinationOptions _hallucinationOptions;
        private readonly TranscriptOptions _transcriptOptions;
        private readonly ILoggerFactory _loggerFactory;

        public LiveCommand(
            IOptions<CaptureOptions> captureOptions,
            IOptions<WhisperOptions> whisperOptions,
            IOptions<VadOptions> vadOptions,
            IOptions<HallucinationOptions> hallucinationOptions,
            IOptions<TranscriptOptions> transcriptOptions,
            ILoggerFactory loggerFactory)
        {
            _captureOptions = captureOptions.Value;
            _whisperOptions = whisperOptions.Value;
            _vadOptions = vadOptions.Value;
            _hallucinationOptions = hallucinationOptions.Value;
            _transcriptOptions = transcriptOptions.Value;
            _loggerFactory = loggerFactory;
        }

        internal sealed class Settings : CommandSettings
        {
            [CommandOption("--naive")]
            [Description("Transcribe fixed, non-overlapping chunks instead of utterances (the stage 3 baseline).")]
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

            try
            {
                var mode = settings.Naive ? $"{settings.ChunkSeconds} s chunks" : "utterances";
                AnsiConsole.MarkupLineInterpolated($"[grey]{transcriber.Description}, {mode}. Ctrl+C to stop.[/]");

                if (settings.Naive)
                    await RunNaiveAsync(captures, transcriber, settings, cancellationToken);
                else
                    await RunCaptionsAsync(captures, transcriber, settings, cancellationToken);

                foreach (var capture in captures)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[grey]{capture.Source}: {capture.DroppedBufferCount} dropped buffers, {capture.DiscontinuityCount} device glitches, {capture.ReconnectCount} device switches[/]");
                }

                return 0;
            }
            finally
            {
                foreach (var capture in captures)
                    await capture.DisposeAsync();
            }
        }

        private async Task RunCaptionsAsync(List<IAudioSourceCapture> captures, ITranscriber transcriber, Settings settings, CancellationToken cancellationToken)
        {
            List<ITranscriptSink> sinks = [new ConsoleTranscriptSink()];
            var transcript = _transcriptOptions.Save ? new SessionTranscriptSink(_transcriptOptions.ResolvedDirectory, DateTimeOffset.Now) : null;
            if (transcript is not null)
                sinks.Add(transcript);

            var session = new CaptionSession(
                captures,
                transcriber,
                _vadOptions,
                _hallucinationOptions,
                _whisperOptions.PromptCharacters,
                sinks,
                _loggerFactory);

            session.Start();
            ShowDevices(captures);

            await WaitAsync(settings, cancellationToken);

            AnsiConsole.MarkupLine("[grey]Stopping...[/]");
            await session.StopAsync();

            AnsiConsole.MarkupLineInterpolated(
                $"[grey]Provisional passes skipped while busy: {session.Coordinator.SkippedProvisionalCount}. Hallucinations dropped: {session.Coordinator.HallucinationCount}.[/]");
            if (transcript is { HasContent: true })
                AnsiConsole.MarkupLineInterpolated($"Transcript saved to [bold]{transcript.FilePath}[/]");
        }

        private static async Task RunNaiveAsync(List<IAudioSourceCapture> captures, ITranscriber transcriber, Settings settings, CancellationToken cancellationToken)
        {
            // Both sources share one transcriber through one queue. Wait, never drop:
            // if transcription falls behind, back-pressure reaches the capture queues,
            // which drop (and count) the oldest audio instead.
            var windows = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.Wait });

            foreach (var capture in captures)
                capture.Start();
            ShowDevices(captures);

            var chunkLength = TimeSpan.FromSeconds(settings.ChunkSeconds);
            var pumps = captures.Select(capture => PumpNaiveAsync(capture, chunkLength, windows.Writer)).ToList();
            var transcribing = TranscribeNaiveAsync(transcriber, windows.Reader);

            await WaitAsync(settings, cancellationToken);

            AnsiConsole.MarkupLine("[grey]Stopping...[/]");
            foreach (var capture in captures)
                await capture.StopAsync();
            await Task.WhenAll(pumps);
            windows.Writer.Complete();
            await transcribing;
        }

        private static void ShowDevices(IEnumerable<IAudioSourceCapture> captures)
        {
            foreach (var capture in captures)
                AnsiConsole.MarkupLineInterpolated($"[grey]{capture.Source}: {capture.DeviceName}[/]");
            AnsiConsole.WriteLine();
        }

        private static async Task WaitAsync(Settings settings, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(settings.Seconds is { } seconds ? TimeSpan.FromSeconds(seconds) : Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ctrl+C: stop capturing, then finish transcribing what was captured.
            }
        }

        private static async Task PumpNaiveAsync(IAudioSourceCapture capture, TimeSpan chunkLength, ChannelWriter<AudioChunk> windows)
        {
            var chunker = new FixedChunker(chunkLength);

            await foreach (var chunk in AudioConverter.ConvertAllAsync(capture.ReadAllAsync()))
            {
                foreach (var window in chunker.Add(chunk))
                    await windows.WriteAsync(window);
            }

            if (chunker.Flush() is { } last)
                await windows.WriteAsync(last);
        }

        private static async Task TranscribeNaiveAsync(ITranscriber transcriber, ChannelReader<AudioChunk> windows)
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
