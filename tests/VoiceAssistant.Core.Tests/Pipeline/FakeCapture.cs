using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NAudio.Wave;
using VoiceAssistant.Core.Capture;
using VoiceAssistant.Core.Conversion;

namespace VoiceAssistant.Core.Tests.Pipeline
{
    /// <summary>
    /// Replays prepared audio as if it came from a device, as fast as it is read. Each
    /// segment can have its own format, to imitate the capture moving to another device.
    /// </summary>
    internal sealed class FakeCapture : IAudioSourceCapture
    {
        private readonly IReadOnlyList<Segment> _segments;

        public FakeCapture(AudioSource source, float[] samples)
            : this(source, [new Segment(TimeSpan.Zero, AudioConverter.OutputFormat, samples)])
        {
        }

        public FakeCapture(AudioSource source, IReadOnlyList<Segment> segments)
        {
            Source = source;
            _segments = segments;
        }

        public AudioSource Source { get; }

        public string DeviceName => "Fake";

        public WaveFormat WaveFormat => _segments[0].Format;

        public long DroppedBufferCount => 0;

        public long DiscontinuityCount => 0;

        public long ReconnectCount => _segments.Count - 1;

        public void Start()
        {
        }

        public Task StopAsync() => Task.CompletedTask;

        public async IAsyncEnumerable<CapturedBuffer> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var segment in _segments)
            {
                // 10 ms buffers of interleaved float samples.
                var frameFloats = segment.Format.Channels;
                var bufferFloats = segment.Format.SampleRate / 100 * frameFloats;
                for (var offset = 0; offset < segment.Samples.Length; offset += bufferFloats)
                {
                    var length = Math.Min(bufferFloats, segment.Samples.Length - offset);
                    var timestamp = segment.Start + TimeSpan.FromSeconds((double)offset / frameFloats / segment.Format.SampleRate);
                    var bytes = MemoryMarshal.AsBytes(segment.Samples.AsSpan(offset, length)).ToArray();
                    yield return new CapturedBuffer(Source, bytes, timestamp, segment.Format);
                    await Task.Yield();
                }
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <param name="Samples">Interleaved float samples in <paramref name="Format"/>.</param>
        public sealed record Segment(TimeSpan Start, WaveFormat Format, float[] Samples);
    }
}
