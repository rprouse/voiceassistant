namespace VoiceAssistant.Core.Capture
{
    /// <summary>
    /// Decides when a growing counter is worth logging: the first increase straight
    /// away, then at most once per interval, so a glitchy device can't flood the log.
    /// </summary>
    internal sealed class ThrottledCountReporter
    {
        private readonly TimeSpan _interval;
        private long _reported;
        private TimeSpan? _lastReportAt;

        public ThrottledCountReporter(TimeSpan interval)
        {
            _interval = interval;
        }

        /// <returns>The increase since the last report if one is due, otherwise null.</returns>
        public long? Check(long total, TimeSpan now)
        {
            if (total == _reported || (_lastReportAt is { } last && now - last < _interval))
                return null;

            _lastReportAt = now;
            return Take(total);
        }

        /// <returns>Any increase not yet reported, ignoring the interval.</returns>
        public long? Flush(long total) => total == _reported ? null : Take(total);

        private long Take(long total)
        {
            var increase = total - _reported;
            _reported = total;
            return increase;
        }
    }
}
