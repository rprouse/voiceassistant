using System.Runtime.InteropServices;
using NAudio.Wave;
using VoiceAssistant.Core.Conversion;

namespace VoiceAssistant.Core.Tests.Conversion
{
    public class AudioConverterTests
    {
        private const double Frequency = 440;

        // What WASAPI shared mode typically reports.
        private static readonly WaveFormat Device48K = new WaveFormatExtensible(48_000, 32, 2, true, 32, 3);
        private static readonly WaveFormat Device44K = new WaveFormatExtensible(44_100, 32, 2, true, 32, 3);

        [TestCase(48_000)]
        [TestCase(44_100)]
        public void StereoSineConvertsToTheExpectedLength(int sampleRate)
        {
            var format = sampleRate == 48_000 ? Device48K : Device44K;
            var output = new AudioConverter(format).Convert(StereoSine(format, TimeSpan.FromSeconds(2)));

            output.Length.ShouldBeInRange(31_680, 32_320); // ±1%
        }

        [TestCase(48_000)]
        [TestCase(44_100)]
        public void StereoSineKeepsItsFrequency(int sampleRate)
        {
            var format = sampleRate == 48_000 ? Device48K : Device44K;
            var output = new AudioConverter(format).Convert(StereoSine(format, TimeSpan.FromSeconds(2)));

            MeasureFrequency(output).ShouldBe(Frequency, tolerance: 1);
        }

        [Test]
        public void SilenceStaysSilence()
        {
            var output = new AudioConverter(Device48K).Convert(new byte[Device48K.AverageBytesPerSecond]);

            output.ShouldNotBeEmpty();
            output.ShouldAllBe(s => s == 0f);
        }

        [Test]
        public void ChannelsAreAveraged()
        {
            var frames = new float[48_000 * 2];
            for (var i = 0; i < frames.Length; i += 2)
            {
                frames[i] = 0.6f;
                frames[i + 1] = 0.2f;
            }

            var output = new AudioConverter(Device48K).Convert(MemoryMarshal.AsBytes(frames.AsSpan()));

            // Skip the resampler's settling at the edges.
            output[2_000..^2_000].ShouldAllBe(s => Math.Abs(s - 0.4f) < 0.001f);
        }

        [Test]
        public void OddBufferSizesAcrossChunkBoundariesMatchOneShotConversion()
        {
            var input = StereoSine(Device48K, TimeSpan.FromSeconds(2));
            var oneShot = new AudioConverter(Device48K).Convert(input);

            // Sizes that split samples and frames mid-way, like real device buffers can.
            int[] sizes = [1, 7, 333, 1_001, 4_097, 13, 2_050];
            var converter = new AudioConverter(Device48K);
            var chunked = new List<float>();
            var offset = 0;
            for (var i = 0; offset < input.Length; i++)
            {
                var size = Math.Min(sizes[i % sizes.Length], input.Length - offset);
                chunked.AddRange(converter.Convert(input.AsSpan(offset, size)));
                offset += size;
            }

            chunked.Count.ShouldBeInRange(oneShot.Length - 2, oneShot.Length + 2);
            var compared = Math.Min(chunked.Count, oneShot.Length);
            for (var i = 0; i < compared; i++)
                chunked[i].ShouldBe(oneShot[i], tolerance: 1e-4f);

            // A dropped or duplicated sample in a 440 Hz sine shows up as a jump larger
            // than the steepest slope between two 16 kHz samples (2π·440/16000 ≈ 0.173).
            var largestStep = chunked.Zip(chunked.Skip(1), (a, b) => Math.Abs(b - a)).Max();
            largestStep.ShouldBeLessThan(0.2f);
        }

        [Test]
        public void LongInputConvertsInOneCall()
        {
            // Longer than the converter's internal staging buffer.
            var output = new AudioConverter(Device48K).Convert(StereoSine(Device48K, TimeSpan.FromSeconds(30)));

            output.Length.ShouldBeInRange(475_200, 484_800); // 30 s ±1%
            MeasureFrequency(output).ShouldBe(Frequency, tolerance: 1);
        }

        [Test]
        public void SixteenKilohertzMonoPassesThroughUnchanged()
        {
            var format = WaveFormat.CreateIeeeFloatWaveFormat(16_000, 1);
            var samples = Enumerable.Range(0, 1_600).Select(i => (float)Math.Sin(i * 0.1)).ToArray();

            var output = new AudioConverter(format).Convert(MemoryMarshal.AsBytes(samples.AsSpan()));

            output.ShouldBe(samples);
        }

        [Test]
        public void Pcm16InputIsDecodedToFloat()
        {
            var format = new WaveFormat(16_000, 16, 1);
            short[] samples = [16_384, -16_384, 0];

            var output = new AudioConverter(format).Convert(MemoryMarshal.AsBytes(samples.AsSpan()));

            output.ShouldBe([0.5f, -0.5f, 0f]);
        }

        [Test]
        public void OutputFormatIsSixteenKilohertzMonoFloat()
        {
            AudioConverter.OutputFormat.SampleRate.ShouldBe(16_000);
            AudioConverter.OutputFormat.Channels.ShouldBe(1);
            AudioConverter.OutputFormat.Encoding.ShouldBe(WaveFormatEncoding.IeeeFloat);
        }

        private static byte[] StereoSine(WaveFormat format, TimeSpan duration)
        {
            var frames = (int)(duration.TotalSeconds * format.SampleRate);
            var samples = new float[frames * 2];
            for (var i = 0; i < frames; i++)
            {
                var value = (float)(0.8 * Math.Sin(2 * Math.PI * Frequency * i / format.SampleRate));
                samples[2 * i] = value;
                samples[2 * i + 1] = value;
            }

            return MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        }

        // Rising zero crossings, interpolated between samples, over the steady middle of the signal.
        private static double MeasureFrequency(float[] samples)
        {
            var crossings = new List<double>();
            for (var i = 1_000; i < samples.Length - 1_000; i++)
            {
                if (samples[i - 1] < 0 && samples[i] >= 0)
                    crossings.Add(i - 1 + (samples[i - 1] / (samples[i - 1] - samples[i])));
            }

            return (crossings.Count - 1) * AudioConverter.OutputSampleRate / (crossings[^1] - crossings[0]);
        }
    }
}
