using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceAssistant.App.ViewModels
{
    /// <summary>A model in the settings dropdown: one from the catalog, or a custom file.</summary>
    public sealed partial class ModelChoice : ObservableObject
    {
        public ModelChoice(string name, string relativePath, string fullPath, int? approximateSizeMb, string note)
        {
            Name = name;
            RelativePath = relativePath;
            FullPath = fullPath;
            ApproximateSizeMb = approximateSizeMb;
            Note = note;
            IsDownloaded = File.Exists(fullPath);
        }

        public string Name { get; }

        /// <summary>The path saved in settings, in the same relative form as the configured model.</summary>
        public string RelativePath { get; }

        public string FullPath { get; }

        /// <summary>Null for a custom model that isn't in the catalog (and can't be downloaded).</summary>
        public int? ApproximateSizeMb { get; }

        public string Note { get; }

        public bool CanDownload => ApproximateSizeMb is not null;

        public string Size => ApproximateSizeMb switch
        {
            null => string.Empty,
            >= 1_000 and var mb => $"{mb / 1000.0:0.#} GB",
            var mb => $"{mb} MB"
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Status))]
        public partial bool IsDownloaded { get; set; }

        public string Status => IsDownloaded ? "✓ downloaded" : "not downloaded";

        // Screen readers and UI automation read list items by their text.
        public override string ToString() => $"{Name}, {Size}, {Status}";
    }
}
