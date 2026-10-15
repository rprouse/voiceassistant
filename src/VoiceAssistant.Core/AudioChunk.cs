namespace VoiceAssistant.Core
{
    /// <summary>
    /// Converted audio ready for VAD and transcription: 16 kHz, mono, 32-bit float
    /// (<see cref="Conversion.AudioConverter.OutputFormat"/>).
    /// </summary>
    /// <param name="Timestamp">Session time of the first sample.</param>
    public sealed record AudioChunk(AudioSource Source, float[] Samples, TimeSpan Timestamp);
}
