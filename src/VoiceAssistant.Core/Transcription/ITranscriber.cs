namespace VoiceAssistant.Core.Transcription
{
    public interface ITranscriber
    {
        /// <summary>
        /// Transcribes 16 kHz mono float audio. Calls are serialised: one transcriber
        /// is shared by every source because models are memory-heavy.
        /// </summary>
        /// <param name="start">Session time of the first sample, added to every segment's times.</param>
        /// <param name="prompt">Preceding text, to help Whisper continue naturally; null for none.</param>
        IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
            AudioSource source,
            TimeSpan start,
            float[] samples,
            string? prompt,
            CancellationToken cancellationToken);
    }
}
