namespace VoiceAssistant.Core.Vad
{
    /// <summary>
    /// Classifies consecutive frames of 16 kHz mono audio. Implementations may keep
    /// state between frames (noise floor, hysteresis), so use one instance per source.
    /// </summary>
    public interface IVoiceActivityDetector
    {
        bool IsSpeech(ReadOnlySpan<float> frame);
    }
}
