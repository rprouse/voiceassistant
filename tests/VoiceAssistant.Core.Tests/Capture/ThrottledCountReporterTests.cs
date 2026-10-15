using VoiceAssistant.Core.Capture;

namespace VoiceAssistant.Core.Tests.Capture
{
    public class ThrottledCountReporterTests
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        [Test]
        public void NothingToReportWhileCountIsUnchanged()
        {
            var reporter = new ThrottledCountReporter(Interval);

            reporter.Check(0, TimeSpan.FromSeconds(1)).ShouldBeNull();
            reporter.Flush(0).ShouldBeNull();
        }

        [Test]
        public void FirstIncreaseIsReportedImmediately()
        {
            var reporter = new ThrottledCountReporter(Interval);

            reporter.Check(1, TimeSpan.FromSeconds(1)).ShouldBe(1);
        }

        [Test]
        public void IncreasesWithinTheIntervalAreHeldBackThenReportedTogether()
        {
            var reporter = new ThrottledCountReporter(Interval);
            reporter.Check(1, TimeSpan.FromSeconds(1));

            reporter.Check(9, TimeSpan.FromSeconds(2)).ShouldBeNull();
            reporter.Check(40, TimeSpan.FromSeconds(5.9)).ShouldBeNull();
            reporter.Check(41, TimeSpan.FromSeconds(6)).ShouldBe(40);
        }

        [Test]
        public void FlushReportsWhatIsLeftRegardlessOfInterval()
        {
            var reporter = new ThrottledCountReporter(Interval);
            reporter.Check(1, TimeSpan.FromSeconds(1));
            reporter.Check(30, TimeSpan.FromSeconds(2));

            reporter.Flush(30).ShouldBe(29);
            reporter.Flush(30).ShouldBeNull();
        }
    }
}
