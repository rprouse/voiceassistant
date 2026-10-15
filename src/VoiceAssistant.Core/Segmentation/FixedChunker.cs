using VoiceAssistant.Core.Conversion;

namespace VoiceAssistant.Core.Segmentation
{
    /// <summary>
    /// Cuts one source's converted audio into fixed, non-overlapping windows: the
    /// naive baseline that stage 4's utterance segmentation replaces. Words that
    /// straddle a window boundary get chopped; that is expected.
    /// </summary>
    public sealed class FixedChunker
    {
        private readonly float[] _window;
        private AudioSource _source;
        private int _filled;
        private TimeSpan _windowStart;

        public FixedChunker(TimeSpan windowLength)
        {
            var samples = (int)(windowLength.TotalSeconds * AudioConverter.OutputSampleRate);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples, nameof(windowLength));
            _window = new float[samples];
        }

        /// <returns>Every window this chunk completed, in order.</returns>
        public IReadOnlyList<AudioChunk> Add(AudioChunk chunk)
        {
            var completed = new List<AudioChunk>();
            var samples = chunk.Samples.AsSpan();
            var offset = 0;

            while (offset < samples.Length)
            {
                if (_filled == 0)
                {
                    _source = chunk.Source;
                    _windowStart = chunk.Timestamp + TimeSpan.FromSeconds((double)offset / AudioConverter.OutputSampleRate);
                }

                var count = Math.Min(_window.Length - _filled, samples.Length - offset);
                samples.Slice(offset, count).CopyTo(_window.AsSpan(_filled));
                _filled += count;
                offset += count;

                if (_filled == _window.Length)
                    completed.Add(TakeWindow());
            }

            return completed;
        }

        /// <returns>The partly filled window, or null if there is none.</returns>
        public AudioChunk? Flush() => _filled == 0 ? null : TakeWindow();

        private AudioChunk TakeWindow()
        {
            var window = new AudioChunk(_source, _window.AsSpan(0, _filled).ToArray(), _windowStart);
            _filled = 0;
            return window;
        }
    }
}
