using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NAudio.Wave;
using VoiceAssistant.Core.Capture;
using VoiceAssistant.Core.Conversion;

namespace VoiceAssistant.Core.Tests.Pipeline
{
    /// <summary>Replays prepared 16 kHz audio as if it came from a device, as fast as it is read.</summary>
    internal sealed class FakeCapture(AudioSource source, float[] samples) : IAudioSourceCapture
    {
        public AudioSource Source => source;

        public string DeviceName => "Fake";

        public WaveFormat WaveFormat => AudioConverter.OutputFormat;

        public long DroppedBufferCount => 0;

        public long DiscontinuityCount => 0;

        public void Start()
        {
        }

        public Task StopAsync() => Task.CompletedTask;

        public async IAsyncEnumerable<CapturedBuffer> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in TestAudio.Chunks(samples, chunkMs: 10, source: source))
            {
                yield return new CapturedBuffer(source, MemoryMarshal.AsBytes(chunk.Samples.AsSpan()).ToArray(), chunk.Timestamp);
                await Task.Yield();
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
