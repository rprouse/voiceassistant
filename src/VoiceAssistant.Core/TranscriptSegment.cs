namespace VoiceAssistant.Core
{
    /// <param name="Start">Session time the speech starts.</param>
    /// <param name="End">Session time the speech ends.</param>
    /// <param name="IsFinal">False while the text may still be revised.</param>
    public sealed record TranscriptSegment(
        Guid Id,
        AudioSource Source,
        TimeSpan Start,
        TimeSpan End,
        string Text,
        bool IsFinal);
}
