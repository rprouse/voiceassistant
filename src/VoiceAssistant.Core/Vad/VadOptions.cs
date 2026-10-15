namespace VoiceAssistant.Core.Vad
{
    public sealed class VadOptions
    {
        public const string SectionName = "Vad";

        /// <summary>Length of each frame the detector classifies.</summary>
        public int FrameMs { get; set; } = 30;

        /// <summary>Continuous speech needed before an utterance starts.</summary>
        public int MinSpeechMs { get; set; } = 200;

        /// <summary>Audio kept from before speech was detected, so first syllables aren't clipped.</summary>
        public int PreRollMs { get; set; } = 300;

        /// <summary>Silence that ends an utterance.</summary>
        public int EndSilenceMs { get; set; } = 600;

        /// <summary>Utterances are cut, at the quietest recent frame, before growing longer than this.</summary>
        public int MaxUtteranceSeconds { get; set; } = 15;

        /// <summary>How often the utterance so far is re-transcribed while someone is speaking.</summary>
        public int ProvisionalIntervalMs { get; set; } = 1000;

        /// <summary>How far above the noise floor a frame must be to start speech.</summary>
        public double SpeechThresholdDb { get; set; } = 10;

        /// <summary>How far above the noise floor a frame must stay to continue speech (hysteresis).</summary>
        public double SilenceThresholdDb { get; set; } = 5;

        /// <summary>Frames quieter than this are never speech, however quiet the room.</summary>
        public double MinSpeechLevelDb { get; set; } = -50;

        /// <summary>The noise floor never drops below this (digital silence would otherwise pull it to minus infinity).</summary>
        public double MinNoiseFloorDb { get; set; } = -75;

        /// <summary>How fast the noise floor can rise; it falls almost immediately.</summary>
        public double NoiseFloorRiseDbPerSecond { get; set; } = 2;
    }
}
