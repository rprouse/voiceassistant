namespace VoiceAssistant.Core.Capture
{
    /// <summary>
    /// Keeps a stream's sample position aligned with session time. Loopback capture
    /// goes quiet (no buffers at all) while nothing plays, so before each buffer the
    /// tracker reports how many silent frames are needed to reach the buffer's timestamp.
    /// </summary>
    public sealed class SilenceGapTracker
    {
        private readonly int _sampleRate;
        private readonly long _toleranceFrames;

        public SilenceGapTracker(int sampleRate, TimeSpan tolerance)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            ArgumentOutOfRangeException.ThrowIfLessThan(tolerance, TimeSpan.Zero);

            _sampleRate = sampleRate;
            _toleranceFrames = ToFrames(tolerance);
        }

        public long FramesWritten { get; private set; }

        public TimeSpan Position => TimeSpan.FromSeconds((double)FramesWritten / _sampleRate);

        /// <summary>
        /// Silent frames to insert before a buffer that starts at <paramref name="bufferStart"/>.
        /// Small gaps within the tolerance are timing jitter and are ignored.
        /// </summary>
        public long SilenceBefore(TimeSpan bufferStart)
        {
            var gap = ToFrames(bufferStart) - FramesWritten;
            return gap > _toleranceFrames ? gap : 0;
        }

        /// <summary>Silent frames needed to extend the stream to exactly <paramref name="end"/>.</summary>
        public long SilenceUntil(TimeSpan end) => Math.Max(0, ToFrames(end) - FramesWritten);

        public void Advance(long frames)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(frames);
            FramesWritten += frames;
        }

        private long ToFrames(TimeSpan time) => (long)Math.Round(time.TotalSeconds * _sampleRate);
    }
}
