using Spectre.Console;
using VoiceAssistant.Core;

namespace VoiceAssistant.Cli.Commands
{
    /// <summary>
    /// Prints committed captions as permanent lines, with each source's provisional
    /// caption in grey italics beneath them, rewritten in place as it changes.
    /// </summary>
    internal sealed class ConsoleTranscriptSink : ITranscriptSink
    {
        private const string ClearLine = "\u001b[2K\r";

        private readonly SortedDictionary<AudioSource, TranscriptSegment> _provisional = [];
        private readonly bool _canRewrite = AnsiConsole.Profile.Capabilities.Ansi && !Console.IsOutputRedirected;
        private int _provisionalLinesShown;

        public ValueTask OnEventAsync(TranscriptEvent transcriptEvent, CancellationToken cancellationToken)
        {
            EraseProvisional();

            switch (transcriptEvent)
            {
                case SegmentCommitted committed:
                    RemoveProvisional(committed.Segment.Id);
                    TranscriptConsole.WriteSegment(committed.Segment);
                    break;
                case SegmentProvisional provisional:
                    _provisional[provisional.Segment.Source] = provisional.Segment;
                    break;
                case SegmentRetracted retracted:
                    RemoveProvisional(retracted.Id);
                    break;
            }

            DrawProvisional();
            return ValueTask.CompletedTask;
        }

        private void RemoveProvisional(Guid id)
        {
            foreach (var (source, segment) in _provisional)
            {
                if (segment.Id == id)
                {
                    _provisional.Remove(source);
                    return;
                }
            }
        }

        private void EraseProvisional()
        {
            for (var i = 0; i < _provisionalLinesShown; i++)
            {
                AnsiConsole.Cursor.MoveUp(1);
                AnsiConsole.Write(new ControlCode(ClearLine));
            }

            _provisionalLinesShown = 0;
        }

        private void DrawProvisional()
        {
            // Without cursor control, provisional text can't be replaced, so only committed lines are shown.
            if (!_canRewrite)
                return;

            foreach (var segment in _provisional.Values)
            {
                // One terminal line each, so they can be erased reliably: show the newest words.
                var prefix = $"[{TranscriptConsole.Time(segment.Start)}] {segment.Source,-4} ";
                var room = Math.Max(10, AnsiConsole.Profile.Width - prefix.Length - 1);
                var text = segment.Text.Length <= room ? segment.Text : "…" + segment.Text[^(room - 1)..];

                AnsiConsole.MarkupLine(
                    $"[grey][[{TranscriptConsole.Time(segment.Start)}]][/] {TranscriptConsole.SourceBadge(segment.Source)} [grey italic]{Markup.Escape(text)}[/]");
                _provisionalLinesShown++;
            }
        }
    }
}
