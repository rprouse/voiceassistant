using Microsoft.Extensions.Logging.Abstractions;
using VoiceAssistant.Core.Pipeline;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.Core.Tests.Pipeline
{
    /// <summary>The whole pipeline with the real model. Needs models/ggml-base.en.bin.</summary>
    [Category("Integration")]
    public class CaptionSessionTests
    {
        private WhisperTranscriber _transcriber = null!;

        [OneTimeSetUp]
        public void LoadModel()
        {
            var modelPath = Path.Combine(TestPaths.RepositoryRoot, "models", "ggml-base.en.bin");
            if (!File.Exists(modelPath))
                Assert.Ignore($"Model not found at {modelPath}. Run: VoiceAssistant model get base.en");

            _transcriber = new WhisperTranscriber(new WhisperOptions { ModelPath = modelPath }, NullLogger<WhisperTranscriber>.Instance);
        }

        [OneTimeTearDown]
        public void Unload() => _transcriber?.Dispose();

        [Test]
        public async Task SilenceProducesNoCaptions()
        {
            var events = await CaptionAsync(TestAudio.WithRoomNoise(TestAudio.Silence(5)));

            events.ShouldBeEmpty();
        }

        [Test]
        public async Task SingleSentenceProducesExactlyOneCommittedCaption()
        {
            var events = await CaptionAsync(Padded("quick-brown-fox.wav"));

            var committed = events.OfType<SegmentCommitted>().ShouldHaveSingleItem().Segment;
            committed.Text.ShouldContain("quick brown fox", Case.Insensitive);
        }

        [Test]
        public async Task TwoSentencesSeparatedByAPauseProduceTwoCommittedCaptions()
        {
            var events = await CaptionAsync(Padded("two-sentences.wav"));

            var committed = events.OfType<SegmentCommitted>().Select(e => e.Segment).ToList();
            committed.Count.ShouldBe(2);
            committed[0].Text.ShouldContain("meeting starts", Case.Insensitive);
            committed[1].Text.ShouldContain("notes", Case.Insensitive);
        }

        [Test]
        public async Task MonologueIsCaptionedInPiecesNoLongerThanTheMaximum()
        {
            var events = await CaptionAsync(Padded("monologue.wav"));

            var committed = events.OfType<SegmentCommitted>().Select(e => e.Segment).ToList();
            committed.Count.ShouldBeGreaterThanOrEqualTo(2);
            committed.ShouldAllBe(s => s.End - s.Start <= TimeSpan.FromSeconds(new VadOptions().MaxUtteranceSeconds));
            var text = string.Join(" ", committed.Select(s => s.Text));
            text.ShouldContain("migration", Case.Insensitive);
            text.ShouldContain("risks", Case.Insensitive);
        }

        private static float[] Padded(string sample) =>
            TestAudio.WithRoomNoise(TestAudio.Concat(TestAudio.Silence(1), TestAudio.LoadSample(sample), TestAudio.Silence(1.5)));

        private async Task<List<TranscriptEvent>> CaptionAsync(float[] signal)
        {
            var sink = new CollectingSink();
            var session = new CaptionSession(
                [new FakeCapture(AudioSource.Them, signal)],
                _transcriber,
                new VadOptions(),
                new HallucinationOptions { Blocklist = ["Thank you.", "Thanks for watching!", "Thank you for watching."] },
                promptCharacters: 200,
                [sink],
                NullLoggerFactory.Instance);

            session.Start();
            await session.StopAsync().WaitAsync(TimeSpan.FromMinutes(2));
            return sink.Events;
        }

        private sealed class CollectingSink : ITranscriptSink
        {
            public List<TranscriptEvent> Events { get; } = [];

            public ValueTask OnEventAsync(TranscriptEvent transcriptEvent, CancellationToken cancellationToken)
            {
                Events.Add(transcriptEvent);
                return ValueTask.CompletedTask;
            }
        }
    }
}
