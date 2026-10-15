using System.Runtime.CompilerServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceAssistant.Core.Capture;

namespace VoiceAssistant.Core.Conversion
{
    /// <summary>
    /// Streams one source's native device audio to 16 kHz mono float, the format
    /// Whisper consumes. Resampler state carries across calls, so buffers of any
    /// size, including partial frames, convert as one continuous signal.
    /// </summary>
    public sealed class AudioConverter
    {
        public const int OutputSampleRate = 16_000;

        public static readonly WaveFormat OutputFormat = WaveFormat.CreateIeeeFloatWaveFormat(OutputSampleRate, 1);

        private const int ReadBlockSamples = 4_096;

        private readonly int _blockAlign;
        private readonly BufferedWaveProvider _input;
        private readonly ISampleProvider _output;
        private readonly byte[] _partialFrame;
        private int _partialFrameLength;
        private float[] _readBuffer = new float[ReadBlockSamples];

        public AudioConverter(WaveFormat inputFormat)
        {
            _blockAlign = inputFormat.BlockAlign;
            _partialFrame = new byte[_blockAlign];

            // Each call's input is drained before returning, so the buffer only has to hold one call.
            _input = new BufferedWaveProvider(inputFormat, TimeSpan.FromSeconds(10))
            {
                ReadFully = false
            };

            // ToSampleProvider decodes float and 8/16/24/32-bit PCM, including WAVE_FORMAT_EXTENSIBLE.
            ISampleProvider samples = _input.ToSampleProvider();
            if (inputFormat.Channels > 1)
                samples = new DownmixSampleProvider(samples);
            if (inputFormat.SampleRate != OutputSampleRate)
                samples = new WdlResamplingSampleProvider(samples, OutputSampleRate);

            _output = samples;
        }

        public float[] Convert(ReadOnlySpan<byte> input)
        {
            AddWholeFrames(input);

            var produced = 0;
            while (true)
            {
                if (_readBuffer.Length - produced < ReadBlockSamples)
                    Array.Resize(ref _readBuffer, _readBuffer.Length * 2);

                var read = _output.Read(_readBuffer.AsSpan(produced, ReadBlockSamples));
                if (read == 0)
                    break;
                produced += read;
            }

            return _readBuffer.AsSpan(0, produced).ToArray();
        }

        /// <remarks>
        /// The chunk keeps the buffer's timestamp. The resampler holds back a few
        /// milliseconds of filter history, so samples can lag their stamp by that much.
        /// </remarks>
        public AudioChunk Convert(CapturedBuffer buffer) => new(buffer.Source, Convert(buffer.Data), buffer.Timestamp);

        public async IAsyncEnumerable<AudioChunk> ConvertAllAsync(
            IAsyncEnumerable<CapturedBuffer> buffers,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var buffer in buffers.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                var chunk = Convert(buffer);
                if (chunk.Samples.Length > 0)
                    yield return chunk;
            }
        }

        // Decoders read whole samples, so a frame split across two device buffers is
        // held back until the rest of it arrives.
        private void AddWholeFrames(ReadOnlySpan<byte> input)
        {
            if (_partialFrameLength > 0)
            {
                var needed = Math.Min(_blockAlign - _partialFrameLength, input.Length);
                input[..needed].CopyTo(_partialFrame.AsSpan(_partialFrameLength));
                _partialFrameLength += needed;
                input = input[needed..];

                if (_partialFrameLength < _blockAlign)
                    return;

                _input.AddSamples(_partialFrame);
                _partialFrameLength = 0;
            }

            var whole = input.Length - (input.Length % _blockAlign);
            _input.AddSamples(input[..whole]);

            input[whole..].CopyTo(_partialFrame);
            _partialFrameLength = input.Length - whole;
        }
    }
}
