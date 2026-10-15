using VoiceAssistant.Core.Capture;

namespace VoiceAssistant.Core.Tests.Capture
{
    public class SilenceGapTrackerTests
    {
        private const int SampleRate = 48_000;
        private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(50);

        [Test]
        public void NoSilenceWhenBufferStartsWhereStreamEnds()
        {
            var tracker = new SilenceGapTracker(SampleRate, Tolerance);
            tracker.Advance(4_800);

            tracker.SilenceBefore(TimeSpan.FromMilliseconds(100)).ShouldBe(0);
        }

        [Test]
        public void GapsWithinToleranceAreTreatedAsJitter()
        {
            var tracker = new SilenceGapTracker(SampleRate, Tolerance);
            tracker.Advance(4_800);

            tracker.SilenceBefore(TimeSpan.FromMilliseconds(140)).ShouldBe(0);
        }

        [Test]
        public void GapBeyondToleranceIsFilledToTheBufferStart()
        {
            var tracker = new SilenceGapTracker(SampleRate, Tolerance);
            tracker.Advance(4_800);

            // Loopback went quiet from 100 ms until 2.1 s.
            tracker.SilenceBefore(TimeSpan.FromMilliseconds(2_100)).ShouldBe(96_000);
        }

        [Test]
        public void LateFirstBufferGetsLeadingSilence()
        {
            var tracker = new SilenceGapTracker(SampleRate, Tolerance);

            tracker.SilenceBefore(TimeSpan.FromSeconds(1)).ShouldBe(SampleRate);
        }

        [Test]
        public void BufferStartingBeforeStreamEndNeedsNoSilence()
        {
            var tracker = new SilenceGapTracker(SampleRate, Tolerance);
            tracker.Advance(SampleRate);

            tracker.SilenceBefore(TimeSpan.FromMilliseconds(900)).ShouldBe(0);
        }

        [Test]
        public void SilenceUntilIgnoresToleranceAndReachesTheExactEnd()
        {
            var tracker = new SilenceGapTracker(SampleRate, Tolerance);
            tracker.Advance(SampleRate);

            tracker.SilenceUntil(TimeSpan.FromMilliseconds(1_010)).ShouldBe(480);
            tracker.SilenceUntil(TimeSpan.FromMilliseconds(500)).ShouldBe(0);
        }

        [Test]
        public void PositionReflectsFramesWritten()
        {
            var tracker = new SilenceGapTracker(SampleRate, Tolerance);
            tracker.Advance(72_000);

            tracker.Position.ShouldBe(TimeSpan.FromSeconds(1.5));
        }
    }
}
