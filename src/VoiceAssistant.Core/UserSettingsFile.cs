using System.Text.Json;
using System.Text.Json.Nodes;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.Core
{
    /// <summary>
    /// The per-user settings file, layered over appsettings.json by both the CLI and the
    /// app. Lives outside the build output, so settings survive rebuilds.
    /// </summary>
    public static class UserSettingsFile
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        public static string DefaultPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceAssistant", "settings.json");

        /// <summary>
        /// Writes the settings the dialog edits, keeping any other sections or keys
        /// already in the file. Written via a temporary file so it is never half-saved.
        /// </summary>
        public static void Save(string path, WhisperOptions whisper, VadOptions vad)
        {
            var root = File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing ? existing : [];

            var whisperSection = Section(root, WhisperOptions.SectionName);
            whisperSection[nameof(WhisperOptions.ModelPath)] = whisper.ModelPath;
            whisperSection[nameof(WhisperOptions.Language)] = whisper.Language;
            whisperSection[nameof(WhisperOptions.UseGpu)] = whisper.UseGpu;
            whisperSection[nameof(WhisperOptions.GpuDevice)] = whisper.GpuDevice;

            var vadSection = Section(root, VadOptions.SectionName);
            vadSection[nameof(VadOptions.MinSpeechMs)] = vad.MinSpeechMs;
            vadSection[nameof(VadOptions.PreRollMs)] = vad.PreRollMs;
            vadSection[nameof(VadOptions.EndSilenceMs)] = vad.EndSilenceMs;
            vadSection[nameof(VadOptions.MaxUtteranceSeconds)] = vad.MaxUtteranceSeconds;
            vadSection[nameof(VadOptions.ProvisionalIntervalMs)] = vad.ProvisionalIntervalMs;
            vadSection[nameof(VadOptions.SpeechThresholdDb)] = vad.SpeechThresholdDb;
            vadSection[nameof(VadOptions.SilenceThresholdDb)] = vad.SilenceThresholdDb;
            vadSection[nameof(VadOptions.MinSpeechLevelDb)] = vad.MinSpeechLevelDb;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, root.ToJsonString(WriteOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }

        private static JsonObject Section(JsonObject root, string name)
        {
            if (root[name] is JsonObject section)
                return section;

            section = [];
            root[name] = section;
            return section;
        }
    }
}
