using System.Diagnostics;

namespace VoiceAssistant.Core.Tests
{
    public class SessionClockTests
    {
        [Test]
        public void QpcPositionAtSessionStartIsZero()
        {
            var start = Stopwatch.GetTimestamp();
            var clock = new SessionClock(start);

            clock.FromQpcPosition(ToHundredNanoseconds(start)).ShouldBe(TimeSpan.Zero, TimeSpan.FromTicks(1));
        }

        [Test]
        public void QpcPositionIsMeasuredFromSessionStart()
        {
            var start = Stopwatch.GetTimestamp();
            var clock = new SessionClock(start);
            var later = start + (Stopwatch.Frequency * 5 / 2);

            clock.FromQpcPosition(ToHundredNanoseconds(later)).ShouldBe(TimeSpan.FromSeconds(2.5), TimeSpan.FromTicks(1));
        }

        [Test]
        public void ElapsedAdvances()
        {
            var clock = new SessionClock(Stopwatch.GetTimestamp() - Stopwatch.Frequency);

            clock.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        }

        private static long ToHundredNanoseconds(long timestamp) =>
            (long)((Int128)timestamp * 10_000_000 / Stopwatch.Frequency);
    }
}
