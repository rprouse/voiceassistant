namespace VoiceAssistant.Core.Pipeline
{
    public sealed class TranscriptOptions
    {
        public const string SectionName = "Transcript";

        /// <summary>Save each session's committed captions as a Markdown file.</summary>
        public bool Save { get; set; } = true;

        /// <summary>Where transcripts go; empty means the user's Documents\VoiceAssistant folder, which is easy to find.</summary>
        public string Directory { get; set; } = string.Empty;

        public string ResolvedDirectory => string.IsNullOrWhiteSpace(Directory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VoiceAssistant")
            : Environment.ExpandEnvironmentVariables(Directory);
    }
}
