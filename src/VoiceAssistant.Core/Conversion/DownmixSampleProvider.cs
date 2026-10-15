using NAudio.Wave;

namespace VoiceAssistant.Core.Conversion
{
    /// <summary>
    /// Averages every channel of an interleaved source into one. Unlike NAudio's
    /// stereo-only ToMono, this handles any channel count (e.g. 5.1 or 7.1 loopback).
    /// </summary>
    internal sealed class DownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _sourceBuffer = [];

        public DownmixSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            var needed = buffer.Length * _channels;
            if (_sourceBuffer.Length < needed)
                _sourceBuffer = new float[needed];

            var frames = _source.Read(_sourceBuffer.AsSpan(0, needed)) / _channels;
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                var offset = frame * _channels;
                for (var channel = 0; channel < _channels; channel++)
                    sum += _sourceBuffer[offset + channel];
                buffer[frame] = sum / _channels;
            }

            return frames;
        }
    }
}
