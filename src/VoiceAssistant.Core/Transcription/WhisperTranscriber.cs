using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace VoiceAssistant.Core.Transcription
{
    /// <summary>
    /// Transcribes with whisper.cpp through Whisper.net. One model load is shared by
    /// every caller; a semaphore runs one transcription at a time, because a
    /// Whisper processor is not safe for concurrent use.
    /// </summary>
    public sealed class WhisperTranscriber : ITranscriber, IDisposable
    {
        private readonly WhisperOptions _options;
        private readonly ILogger<WhisperTranscriber> _logger;
        private readonly WhisperFactory _factory;
        private readonly WhisperProcessor _processor;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly IDisposable _nativeLog;

        public WhisperTranscriber(WhisperOptions options, ILogger<WhisperTranscriber> logger)
        {
            _options = options;
            _logger = logger;

            var modelPath = Path.GetFullPath(options.ModelPath);
            if (!File.Exists(modelPath))
            {
                var name = Path.GetFileNameWithoutExtension(modelPath).Replace("ggml-", string.Empty, StringComparison.OrdinalIgnoreCase);
                throw new FileNotFoundException(
                    $"Whisper model not found at {modelPath}. Download it with: VoiceAssistant model get {name}", modelPath);
            }

            ModelName = Path.GetFileName(modelPath);

            // whisper.cpp's own messages (devices found, buffers allocated) go to our log.
            _nativeLog = LogProvider.AddLogger((level, message) =>
                _logger.Log(level is WhisperLogLevel.Error ? LogLevel.Error : level is WhisperLogLevel.Warning ? LogLevel.Warning : LogLevel.Debug,
                    "whisper.cpp: {Message}", message?.TrimEnd()));

            // Must be set before the first model load in the process. Without a GPU, load only
            // the CPU runtime so the log reports what is really doing the work.
            RuntimeOptions.RuntimeLibraryOrder = options.UseGpu
                ? [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx]
                : [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
            _factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = options.UseGpu, GpuDevice = options.GpuDevice });

            // Reused for every call without a prompt; prompts need a processor of their own.
            _processor = CreateProcessor(prompt: null);

            Runtime = RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown";
            _logger.LogInformation("Loaded Whisper model {Model} ({Language}) on the {Runtime} runtime", modelPath, options.Language, Runtime);
            if (options.UseGpu && RuntimeOptions.LoadedLibrary is not RuntimeLibrary.Vulkan)
                _logger.LogWarning("UseGpu is on but the Vulkan runtime did not load; transcribing on {Runtime}", Runtime);
        }

        public string ModelName { get; }

        /// <summary>The native whisper.cpp runtime Whisper.net actually loaded, e.g. Cpu or Cuda.</summary>
        public string Runtime { get; }

        public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
            AudioSource source,
            TimeSpan start,
            float[] samples,
            string? prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var processor = string.IsNullOrWhiteSpace(prompt) ? _processor : CreateProcessor(prompt);
            try
            {
                var started = Stopwatch.GetTimestamp();

                await foreach (var segment in processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
                {
                    var text = segment.Text.Trim();
                    if (text.Length > 0)
                        yield return new TranscriptSegment(Guid.NewGuid(), source, start + segment.Start, start + segment.End, text, IsFinal: true);
                }

                var elapsed = Stopwatch.GetElapsedTime(started);
                var audio = TimeSpan.FromSeconds((double)samples.Length / Conversion.AudioConverter.OutputSampleRate);
                _logger.LogDebug(
                    "{Source}: transcribed {AudioSeconds:F2} s in {ElapsedSeconds:F2} s (real-time factor {Rtf:F2})",
                    source, audio.TotalSeconds, elapsed.TotalSeconds, audio > TimeSpan.Zero ? elapsed / audio : 0);
            }
            finally
            {
                if (!ReferenceEquals(processor, _processor))
                    await processor.DisposeAsync().ConfigureAwait(false);
                _gate.Release();
            }
        }

        public void Dispose()
        {
            _processor.Dispose();
            _factory.Dispose();
            _nativeLog.Dispose();
            _gate.Dispose();
        }

        private WhisperProcessor CreateProcessor(string? prompt)
        {
            var builder = _factory.CreateBuilder().WithLanguage(_options.Language);
            if (!string.IsNullOrWhiteSpace(prompt))
                builder = builder.WithPrompt(prompt);
            return builder.Build();
        }
    }
}
