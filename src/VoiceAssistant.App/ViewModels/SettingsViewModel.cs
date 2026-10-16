using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.App.ViewModels
{
    /// <param name="Index">whisper.cpp's device number, or <see cref="WhisperOptions.AutoGpuDevice"/>.</param>
    public sealed record GpuChoice(int Index, string Name)
    {
        // Screen readers and UI automation read list items by their text.
        public override string ToString() => Name;
    }

    /// <summary>Edits a copy of the model, language and VAD settings; nothing changes until <see cref="ApplyTo"/>.</summary>
    public sealed partial class SettingsViewModel : ObservableObject
    {
        public SettingsViewModel(WhisperOptions whisper, VadOptions vad)
        {
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

            Models = CreateModelChoices(whisper.ModelPath);
            SelectedModel = Models.First(m => string.Equals(m.RelativePath, Normalize(whisper.ModelPath), StringComparison.OrdinalIgnoreCase));
            Gpus = CreateGpuChoices(whisper.GpuDevice);
        }

        public IReadOnlyList<ModelChoice> Models { get; }

        public IReadOnlyList<GpuChoice> Gpus { get; }

        /// <summary>Device names come from whisper.cpp, so they're only known after a model has loaded on the GPU.</summary>
        public bool GpuNamesKnown => VulkanDevices.All.Count > 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
        public partial ModelChoice SelectedModel { get; set; }

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

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotDownloading))]
        [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
        public partial bool IsDownloading { get; set; }

        public bool IsNotDownloading => !IsDownloading;

        [ObservableProperty]
        public partial double DownloadPercent { get; set; }

        [ObservableProperty]
        public partial string? DownloadStatus { get; set; }

        /// <returns>True if the values make sense; otherwise sets <see cref="Error"/>.</returns>
        public bool Validate()
        {
            Error = true switch
            {
                _ when IsDownloading => "Wait for the download to finish, or cancel it.",
                _ when !File.Exists(SelectedModel.FullPath) => $"{SelectedModel.Name} isn't downloaded yet. Press Download, or choose another model.",
                _ when string.IsNullOrWhiteSpace(Language) => "Enter a language code, e.g. en.",
                _ when SelectedModel.Name.EndsWith(".en", StringComparison.OrdinalIgnoreCase) && Language.Trim() != "en" => "English-only (.en) models need the language set to en.",
                _ when MinSpeechMs <= 0 || PreRollMs < 0 || EndSilenceMs <= 0 || ProvisionalIntervalMs <= 0 => "Durations must be greater than zero.",
                _ when MaxUtteranceSeconds < 5 => "Maximum utterance must be at least 5 seconds.",
                _ when SilenceThresholdDb > SpeechThresholdDb => "The stay-in-speech threshold can't be above the start threshold.",
                _ => null
            };
            return Error is null;
        }

        public void ApplyTo(WhisperOptions whisper, VadOptions vad)
        {
            whisper.ModelPath = SelectedModel.RelativePath;
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

        // The toolkit adds a matching DownloadCancelCommand that cancels the token.
        [RelayCommand(CanExecute = nameof(CanDownload), IncludeCancelCommand = true)]
        private async Task DownloadAsync(CancellationToken cancellationToken)
        {
            var model = SelectedModel;
            IsDownloading = true;
            Error = null;
            DownloadPercent = 0;
            try
            {
                // The download has no reliable total, so progress is measured against the catalog's size.
                var expectedBytes = model.ApproximateSizeMb!.Value * 1_048_576.0;
                var progress = new Progress<long>(bytes =>
                {
                    DownloadPercent = Math.Min(99, bytes / expectedBytes * 100);
                    DownloadStatus = $"Downloading {model.Name}: {bytes / 1_048_576.0:F0} of ~{model.ApproximateSizeMb} MB";
                });

                await Task.Run(() => ModelCatalog.DownloadAsync(model.Name, model.FullPath, progress, cancellationToken), cancellationToken);

                model.IsDownloaded = true;
                DownloadPercent = 100;
                DownloadStatus = $"{model.Name} downloaded.";
            }
            catch (OperationCanceledException)
            {
                DownloadStatus = "Download cancelled.";
                DownloadPercent = 0;
            }
            catch (Exception ex)
            {
                DownloadStatus = null;
                Error = $"Download failed: {ex.Message}";
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private bool CanDownload() => !IsDownloading && SelectedModel is { CanDownload: true, IsDownloaded: false };

        private static List<ModelChoice> CreateModelChoices(string configured)
        {
            // Downloads go next to the configured model, saved in the same relative form.
            var directory = Path.GetDirectoryName(ModelPath.Resolve(configured)) ?? Environment.CurrentDirectory;
            var relativeDirectory = Path.GetDirectoryName(configured) ?? string.Empty;

            var choices = ModelCatalog.Models
                .Select(m => new ModelChoice(
                    m.Name,
                    Normalize(Path.Combine(relativeDirectory, ModelCatalog.FileName(m.Name))),
                    Path.Combine(directory, ModelCatalog.FileName(m.Name)),
                    m.ApproximateSizeMb,
                    m.Note))
                .ToList();

            if (!choices.Exists(c => string.Equals(c.RelativePath, Normalize(configured), StringComparison.OrdinalIgnoreCase)))
                choices.Add(new ModelChoice(Path.GetFileName(configured), Normalize(configured), ModelPath.Resolve(configured), null, "Custom model"));

            return choices;
        }

        private static List<GpuChoice> CreateGpuChoices(int configured)
        {
            var choices = new List<GpuChoice> { new(WhisperOptions.AutoGpuDevice, "Auto (prefer a dedicated GPU)") };
            choices.AddRange(VulkanDevices.All.Select(d => new GpuChoice(d.Index, d.DisplayName)));
            if (configured >= 0 && !choices.Exists(c => c.Index == configured))
                choices.Add(new GpuChoice(configured, $"Device {configured}"));
            return choices;
        }

        private static string Normalize(string path) => path.Replace('\\', '/');
    }
}
