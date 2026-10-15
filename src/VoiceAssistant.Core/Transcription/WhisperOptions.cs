namespace VoiceAssistant.Core.Transcription
{
    public sealed class WhisperOptions
    {
        public const string SectionName = "Whisper";

        /// <summary>A ggml model file; relative paths resolve against the current directory.</summary>
        public string ModelPath { get; set; } = "models/ggml-base.en.bin";

        public string Language { get; set; } = "en";

        /// <summary>Only has an effect once a GPU runtime package is installed (stage 7).</summary>
        public bool UseGpu { get; set; }
    }
}
