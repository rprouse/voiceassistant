namespace VoiceAssistant.Core.Vad
{
    public static class AudioLevel
    {
        /// <summary>Reported for digital silence, instead of minus infinity.</summary>
        public const double SilenceDb = -100;

        public static double MeanSquare(ReadOnlySpan<float> samples)
        {
            if (samples.IsEmpty)
                return 0;

            double sum = 0;
            foreach (var sample in samples)
                sum += sample * sample;
            return sum / samples.Length;
        }

        /// <summary>RMS level in dB relative to full scale.</summary>
        public static double RmsDb(ReadOnlySpan<float> samples) => ToDb(MeanSquare(samples));

        public static double ToDb(double meanSquare) => meanSquare <= 1e-10 ? SilenceDb : 10 * Math.Log10(meanSquare);
    }
}
