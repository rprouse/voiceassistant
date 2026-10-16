using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.App.ViewModels
{
    /// <summary>Edits a copy of the model, language and VAD settings; nothing changes until <see cref="ApplyTo"/>.</summary>
    public sealed partial class SettingsViewModel : ObservableObject
    {
        public SettingsViewModel(WhisperOptions whisper, VadOptions vad)
        {
            ModelPath = whisper.ModelPath;
            Language = whisper.Language;
            UseGpu = whisper.UseGpu;
            GpuDevice = whisper.GpuDevice;
            MinSpeechMs = vad.MinSpeechMs;
            PreRollMs = vad.PreRollMs;
            EndSilenceMs = vad.EndSilenceMs;
            MaxUtteranceSeconds = vad.MaxUtteranceSeconds;
            ProvisionalIntervalMs = vad.ProvisionalIntervalMs;
            SpeechThresholdDb = vad.SpeechThresholdDb;
            SilenceThresholdDb = vad.SilenceThresholdDb;
            MinSpeechLevelDb = vad.MinSpeechLevelDb;
            Models = FindModels(whisper.ModelPath);
        }

        /// <summary>Models already downloaded next to the configured one.</summary>
        public IReadOnlyList<string> Models { get; }

        [ObservableProperty]
        public partial string ModelPath { get; set; }

        [ObservableProperty]
        public partial string Language { get; set; }

        [ObservableProperty]
        public partial bool UseGpu { get; set; }

        [ObservableProperty]
        public partial int GpuDevice { get; set; }

        [ObservableProperty]
        public partial int MinSpeechMs { get; set; }

        [ObservableProperty]
        public partial int PreRollMs { get; set; }

        [ObservableProperty]
        public partial int EndSilenceMs { get; set; }

        [ObservableProperty]
        public partial int MaxUtteranceSeconds { get; set; }

        [ObservableProperty]
        public partial int ProvisionalIntervalMs { get; set; }

        [ObservableProperty]
        public partial double SpeechThresholdDb { get; set; }

        [ObservableProperty]
        public partial double SilenceThresholdDb { get; set; }

        [ObservableProperty]
        public partial double MinSpeechLevelDb { get; set; }

        [ObservableProperty]
        public partial string? Error { get; set; }

        /// <returns>True if the values make sense; otherwise sets <see cref="Error"/>.</returns>
        public bool Validate()
        {
            Error = true switch
            {
                _ when string.IsNullOrWhiteSpace(ModelPath) => "Choose a model.",
                _ when !File.Exists(Core.Transcription.ModelPath.Resolve(ModelPath)) => $"Model not found: {ModelPath}",
                _ when string.IsNullOrWhiteSpace(Language) => "Enter a language code, e.g. en.",
                _ when GpuDevice < 0 => "GPU device must be 0 or more.",
                _ when MinSpeechMs <= 0 || PreRollMs < 0 || EndSilenceMs <= 0 || ProvisionalIntervalMs <= 0 => "Durations must be greater than zero.",
                _ when MaxUtteranceSeconds < 5 => "Maximum utterance must be at least 5 seconds.",
                _ when SilenceThresholdDb > SpeechThresholdDb => "The stay-in-speech threshold can't be above the start threshold.",
                _ => null
            };
            return Error is null;
        }

        public void ApplyTo(WhisperOptions whisper, VadOptions vad)
        {
            whisper.ModelPath = ModelPath.Trim();
            whisper.Language = Language.Trim();
            whisper.UseGpu = UseGpu;
            whisper.GpuDevice = GpuDevice;
            vad.MinSpeechMs = MinSpeechMs;
            vad.PreRollMs = PreRollMs;
            vad.EndSilenceMs = EndSilenceMs;
            vad.MaxUtteranceSeconds = MaxUtteranceSeconds;
            vad.ProvisionalIntervalMs = ProvisionalIntervalMs;
            vad.SpeechThresholdDb = SpeechThresholdDb;
            vad.SilenceThresholdDb = SilenceThresholdDb;
            vad.MinSpeechLevelDb = MinSpeechLevelDb;
        }

        private static IReadOnlyList<string> FindModels(string configured)
        {
            var directory = Path.GetDirectoryName(Core.Transcription.ModelPath.Resolve(configured));
            if (directory is null || !Directory.Exists(directory))
                return [configured];

            // Offer them in the same relative form as the configured path.
            var relativeDirectory = Path.GetDirectoryName(configured) ?? string.Empty;
            return Directory.EnumerateFiles(directory, "ggml-*.bin")
                .Select(f => Path.Combine(relativeDirectory, Path.GetFileName(f)).Replace('\\', '/'))
                .Append(configured)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
