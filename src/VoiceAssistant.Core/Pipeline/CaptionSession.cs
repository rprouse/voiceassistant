using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VoiceAssistant.Core.Capture;
using VoiceAssistant.Core.Conversion;
using VoiceAssistant.Core.Segmentation;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.Core.Pipeline
{
    /// <summary>
    /// One captioning session: each capture runs convert → VAD → utterance builder, all
    /// sources share one transcriber, and transcript events go to every sink.
    /// </summary>
    public sealed class CaptionSession
    {
        // Small, because events are tiny and sinks are expected to keep up.
        private const int EventQueueCapacity = 256;

        private readonly IReadOnlyList<IAudioSourceCapture> _captures;
        private readonly VadOptions _vadOptions;
        private readonly Func<IVoiceActivityDetector> _createVad;
        private readonly IReadOnlyList<ITranscriptSink> _sinks;
        private readonly ILogger<CaptionSession> _logger;
        private readonly Channel<TranscriptEvent> _events;
        private readonly List<Task> _pumps = [];
        private Task? _transcribing;
        private Task? _dispatching;

        public CaptionSession(
            IReadOnlyList<IAudioSourceCapture> captures,
            ITranscriber transcriber,
            VadOptions vadOptions,
            HallucinationOptions hallucinationOptions,
            int promptCharacters,
            IReadOnlyList<ITranscriptSink> sinks,
            ILoggerFactory loggerFactory,
            Func<IVoiceActivityDetector>? createVad = null)
        {
            _captures = captures;
            _vadOptions = vadOptions;
            _createVad = createVad ?? (() => new EnergyVad(vadOptions));
            _sinks = sinks;
            _logger = loggerFactory.CreateLogger<CaptionSession>();
            _events = Channel.CreateBounded<TranscriptEvent>(new BoundedChannelOptions(EventQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });
            Coordinator = new TranscriptionCoordinator(
                transcriber,
                new HallucinationFilter(hallucinationOptions),
                _events.Writer,
                promptCharacters,
                loggerFactory.CreateLogger<TranscriptionCoordinator>());
        }

        public TranscriptionCoordinator Coordinator { get; }

        public void Start()
        {
            if (_transcribing is not null)
                throw new InvalidOperationException("The session has already been started.");

            _dispatching = TranscriptEventDispatcher.RunAsync(_events.Reader, _sinks, _logger);
            _transcribing = Coordinator.RunAsync();

            foreach (var capture in _captures)
            {
                capture.Start();
                _pumps.Add(PumpAsync(capture));
            }
        }

        /// <summary>
        /// Stops capturing, commits whatever each source was in the middle of saying,
        /// and waits until every event has reached the sinks.
        /// </summary>
        public async Task StopAsync()
        {
            if (_transcribing is null || _dispatching is null)
                return;

            foreach (var capture in _captures)
                await capture.StopAsync().ConfigureAwait(false);

            try
            {
                await Task.WhenAll(_pumps).ConfigureAwait(false);
            }
            finally
            {
                Coordinator.Complete();
                await _transcribing.ConfigureAwait(false);
                _events.Writer.TryComplete();
                await _dispatching.ConfigureAwait(false);
            }
        }

        private async Task PumpAsync(IAudioSourceCapture capture)
        {
            var converter = new AudioConverter(capture.WaveFormat);
            var builder = new UtteranceBuilder(capture.Source, _vadOptions, _createVad());

            await foreach (var chunk in converter.ConvertAllAsync(capture.ReadAllAsync()).ConfigureAwait(false))
            {
                foreach (var update in builder.Add(chunk))
                    await Coordinator.SubmitAsync(update).ConfigureAwait(false);
            }

            if (builder.Flush() is { } last)
                await Coordinator.SubmitAsync(last).ConfigureAwait(false);
        }
    }
}
