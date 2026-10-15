namespace VoiceAssistant.Core.Vad
{
    /// <summary>
    /// Energy-based voice activity detection: a frame is speech when its RMS level is
    /// far enough above an adaptive noise floor. Starting speech needs a bigger margin
    /// than continuing it, so levels hovering near the threshold don't flicker.
    /// </summary>
    public sealed class EnergyVad : IVoiceActivityDetector
    {
        // Fraction of the gap closed per frame when the level drops below the floor.
        private const double FallRate = 0.3;

        private readonly VadOptions _options;
        private readonly double _risePerFrame;
        private bool _speaking;

        public EnergyVad(VadOptions options)
        {
            _options = options;
            _risePerFrame = options.NoiseFloorRiseDbPerSecond * options.FrameMs / 1000.0;
        }

        /// <summary>Current noise floor estimate in dBFS; NaN before the first frame.</summary>
        public double NoiseFloorDb { get; private set; } = double.NaN;

        public bool IsSpeech(ReadOnlySpan<float> frame)
        {
            var level = AudioLevel.RmsDb(frame);
            UpdateNoiseFloor(level);

            var margin = _speaking ? _options.SilenceThresholdDb : _options.SpeechThresholdDb;
            _speaking = level >= _options.MinSpeechLevelDb && level > NoiseFloorDb + margin;
            return _speaking;
        }

        // Falls quickly to quieter frames (the gaps between words) and rises slowly, so
        // sustained speech isn't mistaken for noise but a louder room is learned in seconds.
        private void UpdateNoiseFloor(double level)
        {
            if (double.IsNaN(NoiseFloorDb))
                NoiseFloorDb = level;
            else if (level < NoiseFloorDb)
                NoiseFloorDb += (level - NoiseFloorDb) * FallRate;
            else
                NoiseFloorDb = Math.Min(level, NoiseFloorDb + _risePerFrame);

            NoiseFloorDb = Math.Max(NoiseFloorDb, _options.MinNoiseFloorDb);
        }
    }
}
