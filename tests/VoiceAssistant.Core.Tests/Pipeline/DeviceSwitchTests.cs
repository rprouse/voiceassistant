using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using VoiceAssistant.Core.Capture;
using VoiceAssistant.Core.Conversion;
using VoiceAssistant.Core.Pipeline;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.Core.Tests.Pipeline
{
    /// <summary>
    /// A headset unplugged mid-session: the capture moves to the laptop's device, whose
    /// format differs, and captioning carries on.
    /// </summary>
    public class DeviceSwitchTests
    {
        private static readonly WaveFormat Headset = WaveFormat.CreateIeeeFloatWaveFormat(16_000, 1);
        private static readonly WaveFormat Speakers = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

        [Test]
        public async Task ConversionContinuesWhenTheFormatChanges()
        {
            var capture = new FakeCapture(AudioSource.Me,
            [
                new FakeCapture.Segment(TimeSpan.Zero, Headset, TestAudio.Tone(1, -20)),
                new FakeCapture.Segment(TimeSpan.FromSeconds(1.5), Speakers, Stereo48K(TestAudio.Tone(1, -20)))
            ]);

            var chunks = await AudioConverter.ConvertAllAsync(capture.ReadAllAsync()).ToListAsync();

            chunks.Sum(c => c.Samples.Length).ShouldBeInRange(31_600, 32_400); // 2 s at 16 kHz, ±1%
            chunks.Last().Timestamp.ShouldBeGreaterThan(TimeSpan.FromSeconds(2));
        }

        [Test]
        public async Task CaptioningCarriesOnAcrossADeviceSwitch()
        {
            var before = TestAudio.WithRoomNoise(TestAudio.Concat(TestAudio.Silence(0.5), TestAudio.Tone(1.5, -20), TestAudio.Silence(1)));
            var after = TestAudio.WithRoomNoise(TestAudio.Concat(TestAudio.Silence(0.5), TestAudio.Tone(1.5, -20), TestAudio.Silence(1)));
            var capture = new FakeCapture(AudioSource.Me,
            [
                new FakeCapture.Segment(TimeSpan.Zero, Headset, before),
                new FakeCapture.Segment(TimeSpan.FromSeconds(5), Speakers, Stereo48K(after))
            ]);
            var sink = new CollectingSink();
            var session = new CaptionSession(
                [capture], new FakeTranscriber(), new VadOptions(), new HallucinationOptions(), 200, [sink], NullLoggerFactory.Instance);

            session.Start();
            await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));

            var committed = sink.Events.OfType<SegmentCommitted>().Select(e => e.Segment).ToList();
            committed.Count.ShouldBe(2);
            committed[0].Start.ShouldBeLessThan(TimeSpan.FromSeconds(1));
            committed[1].Start.ShouldBeGreaterThan(TimeSpan.FromSeconds(5));
        }

        // Resample a 16 kHz mono signal to 48 kHz stereo by repeating samples: crude, but enough for level-based VAD.
        private static float[] Stereo48K(float[] mono) => mono.SelectMany(s => new[] { s, s, s, s, s, s }).ToArray();

        private sealed class CollectingSink : ITranscriptSink
        {
            public List<TranscriptEvent> Events { get; } = [];

            public ValueTask OnEventAsync(TranscriptEvent transcriptEvent, CancellationToken cancellationToken)
            {
                Events.Add(transcriptEvent);
                return ValueTask.CompletedTask;
            }
        }
    }
}
