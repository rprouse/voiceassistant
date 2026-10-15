using System.Runtime.InteropServices;
using NAudio.Utils;
using NAudio.Wave;
using VoiceAssistant.Core.Capture;

namespace VoiceAssistant.Core.Tests.Capture
{
    public class PaddedWaveWriterTests
    {
        // The common WASAPI shared-mode format: 48 kHz stereo 32-bit float.
        private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(50);

        [Test]
        public void ContiguousBuffersAreWrittenWithoutPadding()
        {
            var samples = Record(writer =>
            {
                writer.Write(Buffer(TimeSpan.Zero, TimeSpan.FromMilliseconds(10), 0.5f));
                writer.Write(Buffer(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), 0.5f));
                writer.SilenceInserted.ShouldBe(TimeSpan.Zero);
            });

            samples.Length.ShouldBe(960 * 2);
            samples.ShouldAllBe(s => s == 0.5f);
        }

        [Test]
        public void GapIsFilledWithSilenceSoLaterAudioLandsAtItsTimestamp()
        {
            var samples = Record(writer =>
            {
                writer.Write(Buffer(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), 0.5f));
                writer.Write(Buffer(TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(100), 0.25f));
                writer.SilenceInserted.ShouldBe(TimeSpan.FromMilliseconds(500));
            });

            var frameAt = (TimeSpan t) => (int)(t.TotalSeconds * 48_000) * 2;
            samples.Length.ShouldBe(frameAt(TimeSpan.FromMilliseconds(700)));
            samples[..frameAt(TimeSpan.FromMilliseconds(100))].ShouldAllBe(s => s == 0.5f);
            samples[frameAt(TimeSpan.FromMilliseconds(100))..frameAt(TimeSpan.FromMilliseconds(600))].ShouldAllBe(s => s == 0f);
            samples[frameAt(TimeSpan.FromMilliseconds(600))..].ShouldAllBe(s => s == 0.25f);
        }

        [Test]
        public void SilentSourceStillProducesAFileOfTheSessionLength()
        {
            TimeSpan duration = default;
            var samples = Record(writer =>
            {
                writer.PadTo(TimeSpan.FromSeconds(3));
                duration = writer.Duration;
            });

            duration.ShouldBe(TimeSpan.FromSeconds(3));
            samples.Length.ShouldBe(3 * 48_000 * 2);
            samples.ShouldAllBe(s => s == 0f);
        }

        [Test]
        public void PadToExtendsTrailingSilenceToTheStopTime()
        {
            TimeSpan duration = default;
            Record(writer =>
            {
                writer.Write(Buffer(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0.5f));
                writer.PadTo(TimeSpan.FromSeconds(30));
                duration = writer.Duration;
            });

            duration.ShouldBe(TimeSpan.FromSeconds(30));
        }

        [Test]
        public void OutputIsAValidWaveFileInTheNativeFormat()
        {
            using var stream = new MemoryStream();
            using (var writer = new PaddedWaveWriter(new IgnoreDisposeStream(stream), Format, Tolerance))
                writer.Write(Buffer(TimeSpan.Zero, TimeSpan.FromMilliseconds(250), 0.5f));

            stream.Position = 0;
            using var reader = new WaveFileReader(stream);
            reader.WaveFormat.ShouldBe(Format);
            reader.TotalTime.ShouldBe(TimeSpan.FromMilliseconds(250));
        }

        private static float[] Record(Action<PaddedWaveWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new PaddedWaveWriter(new IgnoreDisposeStream(stream), Format, Tolerance))
                write(writer);

            stream.Position = 0;
            using var reader = new WaveFileReader(stream);
            var bytes = new byte[reader.Length];
            reader.ReadExactly(bytes);
            return MemoryMarshal.Cast<byte, float>(bytes).ToArray();
        }

        private static CapturedBuffer Buffer(TimeSpan start, TimeSpan length, float value)
        {
            var samples = new float[(int)(length.TotalSeconds * Format.SampleRate) * Format.Channels];
            Array.Fill(samples, value);
            var bytes = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
            return new CapturedBuffer(AudioSource.Them, bytes, start);
        }
    }
}
