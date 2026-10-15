using System.Diagnostics;

namespace VoiceAssistant.Core
{
    /// <summary>
    /// The single time base for a session. Every timestamp, from every source, is
    /// relative to the moment this clock was created.
    /// </summary>
    public sealed class SessionClock
    {
        private const long HundredNanosecondsPerSecond = 10_000_000;

        private readonly long _startTimestamp;

        public SessionClock() : this(Stopwatch.GetTimestamp())
        {
        }

        internal SessionClock(long startTimestamp)
        {
            _startTimestamp = startTimestamp;
        }

        public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_startTimestamp);

        /// <summary>
        /// Converts a WASAPI QPC position (performance counter in 100 ns units, as
        /// reported for the first frame of a capture buffer) to session time.
        /// Stopwatch is backed by the same counter, so the two time bases agree.
        /// </summary>
        public TimeSpan FromQpcPosition(long qpcPosition)
        {
            var timestamp = (long)((Int128)qpcPosition * Stopwatch.Frequency / HundredNanosecondsPerSecond);
            return Stopwatch.GetElapsedTime(_startTimestamp, timestamp);
        }
    }
}
