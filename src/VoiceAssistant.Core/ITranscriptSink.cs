namespace VoiceAssistant.Core
{
    /// <summary>
    /// A consumer of transcript events: the captions UI, a session file, or (later) an
    /// LLM assistant. Events arrive one at a time, in order; keep handlers quick.
    /// </summary>
    public interface ITranscriptSink
    {
        ValueTask OnEventAsync(TranscriptEvent transcriptEvent, CancellationToken cancellationToken);
    }
}
