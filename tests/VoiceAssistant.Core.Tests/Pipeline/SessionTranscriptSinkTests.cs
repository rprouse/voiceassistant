using VoiceAssistant.Core.Pipeline;

namespace VoiceAssistant.Core.Tests.Pipeline
{
    public class SessionTranscriptSinkTests
    {
        private static readonly DateTimeOffset StartedAt = new(2026, 10, 15, 9, 5, 0, TimeSpan.FromHours(-6));
        private string _directory = null!;

        [SetUp]
        public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory("va-transcripts-").FullName;

        [TearDown]
        public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

        [Test]
        public void FileIsNamedBySessionStart()
        {
            var sink = new SessionTranscriptSink(_directory, StartedAt);

            Path.GetFileName(sink.FilePath).ShouldBe("2026-10-15_0905.md");
        }

        [Test]
        public async Task SecondSessionInTheSameMinuteGetsItsOwnFile()
        {
            var first = new SessionTranscriptSink(_directory, StartedAt);
            await first.OnEventAsync(Committed(AudioSource.Me, 1, "Hello."), CancellationToken.None);

            var second = new SessionTranscriptSink(_directory, StartedAt);

            Path.GetFileName(second.FilePath).ShouldBe("2026-10-15_0905_2.md");
        }

        [Test]
        public void NothingIsWrittenUntilACaptionIsCommitted()
        {
            var sink = new SessionTranscriptSink(_directory, StartedAt);

            File.Exists(sink.FilePath).ShouldBeFalse();
            sink.HasContent.ShouldBeFalse();
        }

        [Test]
        public async Task CommittedCaptionsAreSavedInStartTimeOrderAfterEachCommit()
        {
            var sink = new SessionTranscriptSink(_directory, StartedAt);

            await sink.OnEventAsync(Committed(AudioSource.Me, 75, "Sounds good to me."), CancellationToken.None);
            File.ReadAllText(sink.FilePath).ShouldContain("Sounds good to me.");

            // A long utterance that started earlier but committed later.
            await sink.OnEventAsync(Committed(AudioSource.Them, 62, "Shall we move the review to Thursday?"), CancellationToken.None);

            File.ReadAllText(sink.FilePath).ShouldBe(
                """
                # Transcript 2026-10-15 09:05

                **[00:01:02] Them:** Shall we move the review to Thursday?

                **[00:01:15] Me:** Sounds good to me.


                """.ReplaceLineEndings());
            File.Exists(sink.FilePath + ".tmp").ShouldBeFalse();
        }

        [Test]
        public async Task ProvisionalAndRetractedTextIsNotSaved()
        {
            var sink = new SessionTranscriptSink(_directory, StartedAt);
            var id = Guid.NewGuid();

            await sink.OnEventAsync(new SegmentProvisional(Segment(AudioSource.Them, 5, "thank", isFinal: false, id)), CancellationToken.None);
            await sink.OnEventAsync(new SegmentRetracted(id, AudioSource.Them), CancellationToken.None);

            File.Exists(sink.FilePath).ShouldBeFalse();
        }

        private static SegmentCommitted Committed(AudioSource source, double start, string text) =>
            new(Segment(source, start, text, isFinal: true, Guid.NewGuid()));

        private static TranscriptSegment Segment(AudioSource source, double start, string text, bool isFinal, Guid id) =>
            new(id, source, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 2), text, isFinal);
    }
}
