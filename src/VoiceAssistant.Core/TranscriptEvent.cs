namespace VoiceAssistant.Core
{
    /// <summary>
    /// Something that happened to the transcript. Plain records with no UI types, so
    /// events can later be serialised to another process.
    /// </summary>
    public abstract record TranscriptEvent;

    /// <summary>The text so far for an utterance still being spoken; may be revised.</summary>
    public sealed record SegmentProvisional(TranscriptSegment Segment) : TranscriptEvent;

    /// <summary>The final text for an utterance; replaces any provisional text with the same id and never changes.</summary>
    public sealed record SegmentCommitted(TranscriptSegment Segment) : TranscriptEvent;

    /// <summary>Provisional text that turned out to be noise; remove it.</summary>
    public sealed record SegmentRetracted(Guid Id, AudioSource Source) : TranscriptEvent;
}
