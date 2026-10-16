using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceAssistant.Core.Pipeline;
using VoiceAssistant.Core.Segmentation;
using VoiceAssistant.Core.Transcription;

namespace VoiceAssistant.Core.Tests.Pipeline
{
    public class TranscriptionCoordinatorTests
    {
        private FakeTranscriber _transcriber = null!;
        private Channel<TranscriptEvent> _events = null!;
        private TranscriptionCoordinator _coordinator = null!;
        private Task _running = null!;

        [SetUp]
        public void Start()
        {
            _transcriber = new FakeTranscriber();
            _events = Channel.CreateUnbounded<TranscriptEvent>();
            _coordinator = new TranscriptionCoordinator(
                _transcriber,
                new HallucinationFilter(new HallucinationOptions { Blocklist = ["Thank you."] }),
                _events.Writer,
                promptCharacters: 40,
                NullLogger<TranscriptionCoordinator>.Instance);
            _running = _coordinator.RunAsync();
        }

        [TearDown]
        public void Stop() => _transcriber.Release();

        [Test]
        public async Task FinalSnapshotBecomesACommittedSegmentWithTheUtterancesIdAndTimes()
        {
            var update = Final(AudioSource.Me, start: 2);

            await _coordinator.SubmitAsync(update);
            var events = await FinishAsync();

            var committed = events.ShouldHaveSingleItem().ShouldBeOfType<SegmentCommitted>().Segment;
            committed.Id.ShouldBe(update.UtteranceId);
            committed.Source.ShouldBe(AudioSource.Me);
            committed.Start.ShouldBe(update.Start);
            committed.End.ShouldBe(update.End);
            committed.Text.ShouldBe("utterance 0");
            committed.IsFinal.ShouldBeTrue();
        }

        [Test]
        public async Task ProvisionalSnapshotBecomesAProvisionalSegmentWithTheSameId()
        {
            var id = Guid.NewGuid();

            await _coordinator.SubmitAsync(Provisional(id));
            await _transcriber.Started.WaitAsync();
            await _coordinator.SubmitAsync(Final(id: id));
            var events = await FinishAsync();

            events.Count.ShouldBe(2);
            events[0].ShouldBeOfType<SegmentProvisional>().Segment.ShouldSatisfyAllConditions(
                s => s.Id.ShouldBe(id),
                s => s.IsFinal.ShouldBeFalse());
            events[1].ShouldBeOfType<SegmentCommitted>().Segment.Id.ShouldBe(id);
        }

        [Test]
        public async Task ProvisionalSnapshotIsSkippedWhileTheTranscriberIsBusy()
        {
            _transcriber.Block();
            await _coordinator.SubmitAsync(Final());
            await _transcriber.Started.WaitAsync();

            await _coordinator.SubmitAsync(Provisional(Guid.NewGuid()));
            _transcriber.Release();
            var events = await FinishAsync();

            _coordinator.SkippedProvisionalCount.ShouldBe(1);
            _transcriber.Calls.Count.ShouldBe(1);
            events.ShouldHaveSingleItem().ShouldBeOfType<SegmentCommitted>();
        }

        [Test]
        public async Task FinalSnapshotsAreNeverDroppedEvenWhenTheQueueIsFull()
        {
            _transcriber.Block();
            var finals = Enumerable.Range(0, 20).Select(i => Final(start: i)).ToList();

            // More than the queue holds: later submissions wait instead of being dropped.
            var submitting = Task.Run(async () =>
            {
                foreach (var update in finals)
                    await _coordinator.SubmitAsync(update);
            });
            await _transcriber.Started.WaitAsync();
            submitting.IsCompleted.ShouldBeFalse();

            _transcriber.Release();
            await submitting;
            var events = await FinishAsync();

            events.Cast<SegmentCommitted>().Select(e => e.Segment.Id).ShouldBe(finals.Select(f => f.UtteranceId));
        }

        [Test]
        public async Task WaitingFinalsFromBothSourcesGoInOrderOfUtteranceEndTime()
        {
            _transcriber.Block();
            await _coordinator.SubmitAsync(Final(AudioSource.Me, start: 0));
            await _transcriber.Started.WaitAsync();

            // Crosstalk: Me's utterance arrives first but Them's ended earlier.
            var me = Final(AudioSource.Me, start: 9.5);       // ends 10.5
            var them = Final(AudioSource.Them, start: 9);     // ends 10.0
            var later = Final(AudioSource.Them, start: 11);   // ends 12.0
            await _coordinator.SubmitAsync(later);
            await _coordinator.SubmitAsync(me);
            await _coordinator.SubmitAsync(them);
            _transcriber.Release();
            var events = await FinishAsync();

            events.Cast<SegmentCommitted>().Skip(1).Select(e => e.Segment.Id).ShouldBe([them.UtteranceId, me.UtteranceId, later.UtteranceId]);
        }

        [Test]
        public async Task ProvisionalWhoseFinalIsAlreadyWaitingIsDiscarded()
        {
            // A coordinator that hasn't started yet, so both snapshots are waiting together.
            var transcriber = new FakeTranscriber();
            var events = Channel.CreateUnbounded<TranscriptEvent>();
            var coordinator = new TranscriptionCoordinator(
                transcriber, new HallucinationFilter(new HallucinationOptions()), events.Writer, 40, NullLogger<TranscriptionCoordinator>.Instance);
            var id = Guid.NewGuid();

            await coordinator.SubmitAsync(Provisional(id));
            await coordinator.SubmitAsync(Final(id: id));
            coordinator.Complete();
            await coordinator.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

            transcriber.Calls.Count.ShouldBe(1);
            coordinator.SkippedProvisionalCount.ShouldBe(1);
            events.Writer.Complete();
            (await events.Reader.ReadAllAsync().ToListAsync()).ShouldHaveSingleItem().ShouldBeOfType<SegmentCommitted>();
        }

        [Test]
        public async Task PromptIsTheTailOfTheSameSourcesCommittedText()
        {
            _transcriber.Respond = call => call switch
            {
                0 => "The first thing I want to say is that",
                1 => "Them talking",
                2 => "the second part follows on nicely.",
                _ => "last"
            };

            await _coordinator.SubmitAsync(Final(AudioSource.Me));
            await _coordinator.SubmitAsync(Final(AudioSource.Them));
            await _coordinator.SubmitAsync(Final(AudioSource.Me));
            await _coordinator.SubmitAsync(Final(AudioSource.Me));
            await FinishAsync();

            _transcriber.Calls[0].Prompt.ShouldBeNull();
            _transcriber.Calls[1].Prompt.ShouldBeNull();
            _transcriber.Calls[2].Prompt.ShouldBe("The first thing I want to say is that");

            // Limited to 40 characters, starting at a word boundary.
            var prompt = _transcriber.Calls[3].Prompt.ShouldNotBeNull();
            prompt.Length.ShouldBeLessThanOrEqualTo(40);
            prompt.ShouldEndWith("follows on nicely.");
            "The first thing I want to say is that the second part follows on nicely.".ShouldContain(prompt);
            prompt.ShouldNotStartWith(" ");
        }

        [Test]
        public async Task QuietBlocklistedFinalIsDroppedAndItsProvisionalTextRetracted()
        {
            var id = Guid.NewGuid();
            _transcriber.Respond = _ => "Thank you.";

            await _coordinator.SubmitAsync(Provisional(id, meanLevelDb: -58));
            await _transcriber.Started.WaitAsync();
            await _coordinator.SubmitAsync(Final(id: id, meanLevelDb: -58));
            var events = await FinishAsync();

            events.Count.ShouldBe(2);
            events[0].ShouldBeOfType<SegmentProvisional>();
            events[1].ShouldBe(new SegmentRetracted(id, AudioSource.Them));
            _coordinator.HallucinationCount.ShouldBe(1);
        }

        [Test]
        public async Task NonSpeechProvisionalTextIsNotShown()
        {
            _transcriber.Respond = _ => "[BLANK_AUDIO]";

            await _coordinator.SubmitAsync(Provisional(Guid.NewGuid()));
            var events = await FinishAsync();

            events.ShouldBeEmpty();
        }

        [Test]
        public async Task RealTimeFactorIsMeasured()
        {
            _coordinator.RealTimeFactor.ShouldBe(double.NaN);

            await _coordinator.SubmitAsync(Final());
            await FinishAsync();

            _coordinator.RealTimeFactor.ShouldBeInRange(0, 1);
        }

        [Test]
        public async Task TranscriptionFailureDoesNotStopTheCoordinator()
        {
            _transcriber.Respond = call => call == 0 ? "throw" : "recovered";

            await _coordinator.SubmitAsync(Final());
            await _coordinator.SubmitAsync(Final());
            var events = await FinishAsync();

            events.ShouldHaveSingleItem().ShouldBeOfType<SegmentCommitted>().Segment.Text.ShouldBe("recovered");
        }

        private async Task<List<TranscriptEvent>> FinishAsync()
        {
            _coordinator.Complete();
            await _running.WaitAsync(TimeSpan.FromSeconds(10));
            _events.Writer.Complete();
            return await _events.Reader.ReadAllAsync().ToListAsync();
        }

        private static UtteranceUpdate Final(AudioSource source = AudioSource.Them, double start = 0, Guid? id = null, double meanLevelDb = -20) =>
            new(id ?? Guid.NewGuid(), source, TimeSpan.FromSeconds(start), new float[16_000], IsFinal: true, meanLevelDb, AmbientLevelDb: -60);

        private static UtteranceUpdate Provisional(Guid id, double meanLevelDb = -20) =>
            new(id, AudioSource.Them, TimeSpan.Zero, new float[8_000], IsFinal: false, meanLevelDb, AmbientLevelDb: -60);
    }
}
