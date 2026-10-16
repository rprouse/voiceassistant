using System.Globalization;
using System.Text;

namespace VoiceAssistant.Core.Pipeline
{
    /// <summary>
    /// Saves a session's committed captions as Markdown, named by when the session
    /// started (yyyy-MM-dd_HHmm.md). The whole file is rewritten after each commit, via
    /// a temporary file, so it is always complete and in start-time order, even if the
    /// app is killed. Nothing is written until the first caption is committed.
    /// </summary>
    public sealed class SessionTranscriptSink : ITranscriptSink
    {
        private readonly CaptionTimeline _timeline = new();
        private readonly DateTimeOffset _startedAt;

        public SessionTranscriptSink(string directory, DateTimeOffset startedAt)
        {
            _startedAt = startedAt;
            System.IO.Directory.CreateDirectory(directory);
            FilePath = UniquePath(directory, startedAt);
        }

        public string FilePath { get; }

        /// <summary>True once at least one caption has been saved.</summary>
        public bool HasContent => _timeline.Captions.Count > 0;

        public async ValueTask OnEventAsync(TranscriptEvent transcriptEvent, CancellationToken cancellationToken)
        {
            // Provisional text may still change; only committed captions belong in the record.
            if (transcriptEvent is not SegmentCommitted)
                return;

            _timeline.Apply(transcriptEvent);

            var temporaryPath = FilePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, Render(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, FilePath, overwrite: true);
        }

        public string Render()
        {
            var builder = new StringBuilder();
            builder.Append(CultureInfo.InvariantCulture, $"# Transcript {_startedAt:yyyy-MM-dd HH:mm}").AppendLine().AppendLine();

            foreach (var caption in _timeline.Captions)
            {
                var label = caption.Source == AudioSource.Me ? "Me" : "Them";
                builder.Append(CultureInfo.InvariantCulture, $"**[{caption.Start:hh\\:mm\\:ss}] {label}:** {caption.Text}").AppendLine().AppendLine();
            }

            return builder.ToString();
        }

        private static string UniquePath(string directory, DateTimeOffset startedAt)
        {
            var name = startedAt.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
            var path = Path.Combine(directory, $"{name}.md");
            for (var n = 2; File.Exists(path); n++)
                path = Path.Combine(directory, $"{name}_{n}.md");
            return path;
        }
    }
}
