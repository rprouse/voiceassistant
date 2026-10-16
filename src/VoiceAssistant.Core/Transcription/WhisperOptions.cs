namespace VoiceAssistant.Core.Transcription
{
    public sealed class WhisperOptions
    {
        public const string SectionName = "Whisper";

        /// <summary>A ggml model file. Relative paths are looked for in the current directory, then above the executable.</summary>
        public string ModelPath { get; set; } = "models/ggml-base.en.bin";

        public string Language { get; set; } = "en";

        /// <summary>
        /// Transcribe on the GPU through the Vulkan runtime, falling back to the CPU
        /// if Vulkan can't load. Applies to the first model loaded in the process.
        /// </summary>
        public bool UseGpu { get; set; }

        /// <summary>
        /// Vulkan device number, or -1 for Auto: the first dedicated GPU, else the first
        /// GPU. Laptops often list the integrated GPU first, so Auto avoids it.
        /// </summary>
        public int GpuDevice { get; set; } = AutoGpuDevice;

        public const int AutoGpuDevice = -1;

        /// <summary>How much of the source's committed text is passed to Whisper as a prompt, for continuity.</summary>
        public int PromptCharacters { get; set; } = 200;
    }
}
