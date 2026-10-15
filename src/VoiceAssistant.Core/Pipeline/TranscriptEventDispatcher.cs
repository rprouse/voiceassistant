using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace VoiceAssistant.Core.Pipeline
{
    /// <summary>Delivers every transcript event to every sink, in order.</summary>
    public static class TranscriptEventDispatcher
    {
        public static async Task RunAsync(
            ChannelReader<TranscriptEvent> events,
            IReadOnlyList<ITranscriptSink> sinks,
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            await foreach (var transcriptEvent in events.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var sink in sinks)
                {
                    try
                    {
                        await sink.OnEventAsync(transcriptEvent, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // One broken sink must not stop the others, or the transcript.
                        logger.LogError(ex, "Transcript sink {Sink} failed on {Event}", sink.GetType().Name, transcriptEvent.GetType().Name);
                    }
                }
            }
        }
    }
}
