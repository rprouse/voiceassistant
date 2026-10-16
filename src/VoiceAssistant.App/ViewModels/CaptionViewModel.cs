using CommunityToolkit.Mvvm.ComponentModel;
using VoiceAssistant.Core;

namespace VoiceAssistant.App.ViewModels
{
    /// <summary>One caption row: an utterance, provisional until its final text arrives.</summary>
    public sealed partial class CaptionViewModel : ObservableObject
    {
        public CaptionViewModel(TranscriptSegment segment)
        {
            Id = segment.Id;
            Source = segment.Source;
            Start = segment.Start;
            Text = segment.Text;
            IsProvisional = !segment.IsFinal;
        }

        public Guid Id { get; }

        public AudioSource Source { get; }

        public TimeSpan Start { get; }

        public string Time => Start.ToString(Start.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");

        public string SourceLabel => Source == AudioSource.Me ? "Me" : "Them";

        [ObservableProperty]
        public partial string Text { get; set; }

        [ObservableProperty]
        public partial bool IsProvisional { get; set; }

        public void Update(TranscriptSegment segment)
        {
            Text = segment.Text;
            IsProvisional = !segment.IsFinal;
        }
    }
}
