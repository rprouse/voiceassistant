using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VoiceAssistant.Core.Segmentation;
using VoiceAssistant.Core.Transcription;

namespace VoiceAssistant.Core.Pipeline
{
    /// <summary>
    /// Feeds utterance snapshots from every source through the one shared transcriber
    /// and turns the results into transcript events. Final snapshots queue with
    /// back-pressure and are never dropped; provisional snapshots are skipped whenever
    /// the transcriber is busy or work is waiting.
    /// </summary>
    public sealed class TranscriptionCoordinator
    {
        // Enough to absorb a burst of finals from both sources without stalling capture.
        private const int QueueCapacity = 8;

        private readonly ITranscriber _transcriber;
        private readonly HallucinationFilter _filter;
        private readonly ChannelWriter<TranscriptEvent> _events;
        private readonly ILogger<TranscriptionCoordinator> _logger;
        private readonly int _promptCharacters;
        private readonly Channel<UtteranceUpdate> _queue;
        private readonly Dictionary<AudioSource, string> _committedText = [];
        private readonly HashSet<Guid> _shownProvisional = [];
        private int _busy;
        private long _skippedProvisional;
        private long _hallucinations;

        public TranscriptionCoordinator(
            ITranscriber transcriber,
            HallucinationFilter filter,
            ChannelWriter<TranscriptEvent> events,
            int promptCharacters,
            ILogger<TranscriptionCoordinator> logger)
        {
            _transcriber = transcriber;
            _filter = filter;
            _events = events;
            _promptCharacters = promptCharacters;
            _logger = logger;
            _queue = Channel.CreateBounded<UtteranceUpdate>(new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true
            });
        }

        /// <summary>Provisional passes skipped because the transcriber was busy.</summary>
        public long SkippedProvisionalCount => Interlocked.Read(ref _skippedProvisional);

        /// <summary>Committed utterances discarded as hallucinations.</summary>
        public long HallucinationCount => Interlocked.Read(ref _hallucinations);

        public ValueTask SubmitAsync(UtteranceUpdate update, CancellationToken cancellationToken = default)
        {
            if (update.IsFinal)
                return _queue.Writer.WriteAsync(update, cancellationToken);

            if (Volatile.Read(ref _busy) == 0 && _queue.Reader.Count == 0 && _queue.Writer.TryWrite(update))
                return ValueTask.CompletedTask;

            Interlocked.Increment(ref _skippedProvisional);
            return ValueTask.CompletedTask;
        }

        /// <summary>No more snapshots will be submitted; <see cref="RunAsync"/> finishes once the queue drains.</summary>
        public void Complete() => _queue.Writer.TryComplete();

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            await foreach (var update in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                Volatile.Write(ref _busy, 1);
                try
                {
                    await ProcessAsync(update, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "{Source}: transcription failed for utterance at {Start}", update.Source, update.Start);
                }
                finally
                {
                    Volatile.Write(ref _busy, 0);
                }
            }
        }

        private async Task ProcessAsync(UtteranceUpdate update, CancellationToken cancellationToken)
        {
            var parts = new List<string>();
            await foreach (var segment in _transcriber.TranscribeAsync(update.Source, update.Start, update.Samples, PromptFor(update.Source), cancellationToken).ConfigureAwait(false))
                parts.Add(segment.Text);

            var text = HallucinationFilter.Clean(string.Join(" ", parts));

            if (!update.IsFinal)
            {
                if (HallucinationFilter.IsNonSpeech(text))
                    return;

                _shownProvisional.Add(update.UtteranceId);
                await _events.WriteAsync(new SegmentProvisional(ToSegment(update, text)), cancellationToken).ConfigureAwait(false);
                return;
            }

            var wasShown = _shownProvisional.Remove(update.UtteranceId);
            if (_filter.ShouldDrop(text, update.MeanLevelDb, update.AmbientLevelDb))
            {
                Interlocked.Increment(ref _hallucinations);
                _logger.LogDebug(
                    "{Source}: dropped \"{Text}\" as a likely hallucination (level {Level:F1} dB, ambient {Ambient:F1} dB)",
                    update.Source, text, update.MeanLevelDb, update.AmbientLevelDb);
                if (wasShown)
                    await _events.WriteAsync(new SegmentRetracted(update.UtteranceId, update.Source), cancellationToken).ConfigureAwait(false);
                return;
            }

            Remember(update.Source, text);
            await _events.WriteAsync(new SegmentCommitted(ToSegment(update, text)), cancellationToken).ConfigureAwait(false);
        }

        private static TranscriptSegment ToSegment(UtteranceUpdate update, string text) =>
            new(update.UtteranceId, update.Source, update.Start, update.End, text, update.IsFinal);

        private string? PromptFor(AudioSource source) => _committedText.GetValueOrDefault(source);

        // Keep the tail of the source's committed text, starting at a word boundary.
        private void Remember(AudioSource source, string text)
        {
            var all = _committedText.TryGetValue(source, out var previous) ? $"{previous} {text}" : text;
            if (all.Length > _promptCharacters)
            {
                all = all[^_promptCharacters..];
                var space = all.IndexOf(' ');
                if (space >= 0)
                    all = all[(space + 1)..];
            }

            _committedText[source] = all;
        }
    }
}
