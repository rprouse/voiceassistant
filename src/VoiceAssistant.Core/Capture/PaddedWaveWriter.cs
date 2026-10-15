using NAudio.Wave;

namespace VoiceAssistant.Core.Capture
{
    /// <summary>
    /// Writes captured buffers to a WAV stream, inserting silence wherever the
    /// source produced nothing, so the file's timeline matches session time.
    /// </summary>
    public sealed class PaddedWaveWriter : IDisposable
    {
        private static readonly byte[] Zeros = new byte[64 * 1024];

        private readonly WaveFileWriter _writer;
        private readonly SilenceGapTracker _tracker;
        private readonly int _blockAlign;

        /// <remarks>
        /// Zero bytes are silence for the float and signed PCM formats WASAPI delivers.
        /// </remarks>
        public PaddedWaveWriter(Stream output, WaveFormat format, TimeSpan gapTolerance)
        {
            _writer = new WaveFileWriter(output, format);
            _tracker = new SilenceGapTracker(format.SampleRate, gapTolerance);
            _blockAlign = format.BlockAlign;
        }

        public TimeSpan Duration => _tracker.Position;

        /// <summary>Total silence inserted to fill gaps.</summary>
        public TimeSpan SilenceInserted { get; private set; }

        public void Write(CapturedBuffer buffer)
        {
            WriteSilence(_tracker.SilenceBefore(buffer.Timestamp));

            // Devices deliver whole frames; ignore any stray partial frame rather than misalign the file.
            var length = buffer.Data.Length - (buffer.Data.Length % _blockAlign);
            _writer.Write(buffer.Data.AsSpan(0, length));
            _tracker.Advance(length / _blockAlign);
        }

        /// <summary>Extends the file with silence so it ends at <paramref name="end"/>.</summary>
        public void PadTo(TimeSpan end) => WriteSilence(_tracker.SilenceUntil(end));

        public void Dispose() => _writer.Dispose();

        private void WriteSilence(long frames)
        {
            if (frames <= 0)
                return;

            var remaining = frames * _blockAlign;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(remaining, Zeros.Length - (Zeros.Length % _blockAlign));
                _writer.Write(Zeros.AsSpan(0, chunk));
                remaining -= chunk;
            }

            _tracker.Advance(frames);
            SilenceInserted += TimeSpan.FromSeconds((double)frames / _writer.WaveFormat.SampleRate);
        }
    }
}
