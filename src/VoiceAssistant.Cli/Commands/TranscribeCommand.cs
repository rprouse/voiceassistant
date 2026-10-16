using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using Spectre.Console;
using Spectre.Console.Cli;
using VoiceAssistant.Core;
using VoiceAssistant.Core.Conversion;
using VoiceAssistant.Core.Transcription;

namespace VoiceAssistant.Cli.Commands
{
    internal sealed class TranscribeCommand : AsyncCommand<TranscribeCommand.Settings>
    {
        private readonly WhisperOptions _options;
        private readonly ILoggerFactory _loggerFactory;

        public TranscribeCommand(IOptions<WhisperOptions> options, ILoggerFactory loggerFactory)
        {
            _options = options.Value;
            _loggerFactory = loggerFactory;
        }

        internal sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<FILE>")]
            [Description("WAV file to transcribe, in any format NAudio can read.")]
            public string File { get; set; } = string.Empty;

            [CommandOption("--source <SOURCE>")]
            [Description("Label for the output: Me or Them.")]
            [DefaultValue(AudioSource.Them)]
            public AudioSource Source { get; set; }

            public override ValidationResult Validate() =>
                System.IO.File.Exists(File) ? ValidationResult.Success() : ValidationResult.Error($"File not found: {File}");
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            float[] samples;
            using (var reader = new WaveFileReader(settings.File))
            {
                var bytes = new byte[reader.Length];
                reader.ReadExactly(bytes);
                samples = new AudioConverter(reader.WaveFormat).Convert(bytes);
            }

            var audio = TimeSpan.FromSeconds((double)samples.Length / AudioConverter.OutputSampleRate);

            using var transcriber = AnsiConsole.Status().Start("Loading model...", _ =>
                new WhisperTranscriber(_options, _loggerFactory.CreateLogger<WhisperTranscriber>()));
            AnsiConsole.MarkupLineInterpolated($"[grey]{transcriber.Description}, {audio.TotalSeconds:F1} s of audio[/]");

            var started = Stopwatch.GetTimestamp();
            await foreach (var segment in transcriber.TranscribeAsync(settings.Source, TimeSpan.Zero, samples, prompt: null, cancellationToken))
                TranscriptConsole.WriteSegment(segment, $"→ {TranscriptConsole.Time(segment.End)}");

            var elapsed = Stopwatch.GetElapsedTime(started);
            AnsiConsole.MarkupLineInterpolated(
                $"[grey]Transcribed in {elapsed.TotalSeconds:F1} s (real-time factor {(audio > TimeSpan.Zero ? elapsed / audio : 0):F2})[/]");
            return 0;
        }
    }
}
