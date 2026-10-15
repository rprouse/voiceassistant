using Spectre.Console;
using VoiceAssistant.Core;

namespace VoiceAssistant.Cli.Commands
{
    /// <summary>Shared console formatting for transcript output.</summary>
    internal static class TranscriptConsole
    {
        public static string Time(TimeSpan time) => time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss\.f" : @"mm\:ss\.f");

        public static string SourceBadge(AudioSource source) => source switch
        {
            AudioSource.Me => "[green]Me  [/]",
            _ => "[blue]Them[/]"
        };

        public static void WriteSegment(TranscriptSegment segment, string? suffix = null)
        {
            AnsiConsole.MarkupLine(
                $"[grey][[{Time(segment.Start)}]][/] {SourceBadge(segment.Source)} {Markup.Escape(segment.Text)}{(suffix is null ? string.Empty : $" [grey]{Markup.Escape(suffix)}[/]")}");
        }
    }
}
