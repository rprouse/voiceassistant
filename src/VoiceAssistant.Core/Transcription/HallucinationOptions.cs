namespace VoiceAssistant.Core.Transcription
{
    public sealed class HallucinationOptions
    {
        public const string SectionName = "Hallucinations";

        /// <summary>
        /// Phrases Whisper tends to invent from silence or noise. Matching ignores case
        /// and punctuation. Defaults live in appsettings.json: configuration binding
        /// appends to, rather than replaces, collections initialised here.
        /// </summary>
        public string[] Blocklist { get; set; } = [];

        /// <summary>A blocklisted phrase is only dropped if its audio was within this many dB of the ambient level.</summary>
        public double NearNoiseFloorDb { get; set; } = 6;
    }
}
