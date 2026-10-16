using VoiceAssistant.Core.Pipeline;

namespace VoiceAssistant.Core.Tests.Pipeline
{
    public class CaptionTimelineTests
    {
        // NUnit shares one fixture instance across its tests, so start each with a fresh timeline.
        private CaptionTimeline _timeline = null!;

        [SetUp]
        public void CreateTimeline() => _timeline = new CaptionTimeline();

        [Test]
        public void CaptionsInTimeOrderAreAppended()
        {
            _timeline.Apply(Committed(AudioSource.Them, 1)).ShouldBe(new CaptionInserted(0, _timeline.Captions[0]));
            _timeline.Apply(Committed(AudioSource.Me, 4)).ShouldBeOfType<CaptionInserted>().Index.ShouldBe(1);
            _timeline.Apply(Committed(AudioSource.Them, 7)).ShouldBeOfType<CaptionInserted>().Index.ShouldBe(2);
        }

        [Test]
        public void LongUtteranceThatCommitsLateIsPlacedByItsStartTime()
        {
            // Them starts talking at 10 s and goes on until 25 s; Me interjects at 15 s.
            _timeline.Apply(Committed(AudioSource.Them, 2));
            _timeline.Apply(Committed(AudioSource.Me, 15));

            var change = _timeline.Apply(Committed(AudioSource.Them, 10));

            change.ShouldBeOfType<CaptionInserted>().Index.ShouldBe(1);
            _timeline.Captions.Select(c => c.Start.TotalSeconds).ShouldBe([2, 10, 15]);
        }

        [Test]
        public void ProvisionalTextIsReplacedInPlaceWhenCommitted()
        {
            var id = Guid.NewGuid();
            _timeline.Apply(Committed(AudioSource.Me, 1));
            _timeline.Apply(Provisional(AudioSource.Them, 5, id, "the quick"));
            _timeline.Apply(Provisional(AudioSource.Them, 5, id, "the quick brown"));

            var change = _timeline.Apply(Committed(AudioSource.Them, 5, id, "The quick brown fox."));

            change.ShouldBeOfType<CaptionUpdated>().Index.ShouldBe(1);
            _timeline.Captions.Count.ShouldBe(2);
            _timeline.Captions[1].ShouldSatisfyAllConditions(
                c => c.Text.ShouldBe("The quick brown fox."),
                c => c.IsFinal.ShouldBeTrue());
        }

        [Test]
        public void ProvisionalRowsFromBothSourcesKeepTimeOrder()
        {
            var me = Guid.NewGuid();
            var them = Guid.NewGuid();

            _timeline.Apply(Provisional(AudioSource.Me, 8, me, "so I"));
            _timeline.Apply(Provisional(AudioSource.Them, 6, them, "as we"));

            _timeline.Captions.Select(c => c.Source).ShouldBe([AudioSource.Them, AudioSource.Me]);
        }

        [Test]
        public void SameStartTimeKeepsArrivalOrder()
        {
            _timeline.Apply(Committed(AudioSource.Them, 3, text: "first"));
            _timeline.Apply(Committed(AudioSource.Me, 3, text: "second"));

            _timeline.Captions.Select(c => c.Text).ShouldBe(["first", "second"]);
        }

        [Test]
        public void RetractedCaptionIsRemoved()
        {
            var id = Guid.NewGuid();
            _timeline.Apply(Committed(AudioSource.Me, 1));
            _timeline.Apply(Provisional(AudioSource.Them, 2, id, "thank you"));

            _timeline.Apply(new SegmentRetracted(id, AudioSource.Them)).ShouldBe(new CaptionRemoved(1));
            _timeline.Captions.Count.ShouldBe(1);
        }

        [Test]
        public void RetractingAnUnknownCaptionChangesNothing()
        {
            _timeline.Apply(new SegmentRetracted(Guid.NewGuid(), AudioSource.Me)).ShouldBeNull();
        }

        private static SegmentCommitted Committed(AudioSource source, double start, Guid? id = null, string text = "text") =>
            new(Segment(source, start, id ?? Guid.NewGuid(), text, isFinal: true));

        private static SegmentProvisional Provisional(AudioSource source, double start, Guid id, string text) =>
            new(Segment(source, start, id, text, isFinal: false));

        private static TranscriptSegment Segment(AudioSource source, double start, Guid id, string text, bool isFinal) =>
            new(id, source, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 2), text, isFinal);
    }
}
