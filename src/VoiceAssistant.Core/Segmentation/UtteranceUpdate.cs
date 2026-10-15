using VoiceAssistant.Core.Conversion;

namespace VoiceAssistant.Core.Segmentation
{
    /// <summary>
    /// A snapshot of one utterance to transcribe: provisional while the speaker is
    /// still talking, final once the utterance has ended.
    /// </summary>
    /// <param name="UtteranceId">Shared by every snapshot of the same utterance, so captions can be replaced in place.</param>
    /// <param name="Samples">16 kHz mono audio from the start of the utterance.</param>
    /// <param name="MeanLevelDb">Mean RMS level of the utterance, in dBFS.</param>
    /// <param name="AmbientLevelDb">Recent level of non-speech audio from this source, in dBFS.</param>
    public sealed record UtteranceUpdate(
        Guid UtteranceId,
        AudioSource Source,
        TimeSpan Start,
        float[] Samples,
        bool IsFinal,
        double MeanLevelDb,
        double AmbientLevelDb)
    {
        public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / AudioConverter.OutputSampleRate);

        public TimeSpan End => Start + Duration;
    }
}
