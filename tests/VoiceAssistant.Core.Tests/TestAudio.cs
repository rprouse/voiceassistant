using NAudio.Wave;
using VoiceAssistant.Core.Conversion;

namespace VoiceAssistant.Core.Tests
{
    /// <summary>Builds 16 kHz mono test signals.</summary>
    internal static class TestAudio
    {
        public const int SampleRate = AudioConverter.OutputSampleRate;

        public static float[] LoadSample(string name)
        {
            using var reader = new WaveFileReader(Path.Combine(TestPaths.RepositoryRoot, "samples", name));
            var bytes = new byte[reader.Length];
            reader.ReadExactly(bytes);
            return new AudioConverter(reader.WaveFormat).Convert(bytes);
        }

        public static float[] Silence(double seconds) => new float[Samples(seconds)];

        /// <summary>White noise at an RMS level in dBFS; a seeded generator keeps tests repeatable.</summary>
        public static float[] Noise(double seconds, double levelDb, int seed = 1)
        {
            var random = new Random(seed);
            var amplitude = Math.Pow(10, levelDb / 20) * Math.Sqrt(3); // uniform noise RMS = amplitude / √3
            return Enumerable.Range(0, Samples(seconds)).Select(_ => (float)((random.NextDouble() * 2 - 1) * amplitude)).ToArray();
        }

        /// <summary>A 220 Hz tone at an RMS level in dBFS.</summary>
        public static float[] Tone(double seconds, double levelDb)
        {
            var amplitude = Math.Pow(10, levelDb / 20) * Math.Sqrt(2); // sine RMS = amplitude / √2
            return Enumerable.Range(0, Samples(seconds)).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * 220 * i / SampleRate))).ToArray();
        }

        public static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

        /// <summary>Adds quiet background noise, like a real microphone's, so signals are never digitally silent.</summary>
        public static float[] WithRoomNoise(float[] signal, double levelDb = -65)
        {
            var noise = Noise((double)signal.Length / SampleRate, levelDb, seed: 7);
            return signal.Zip(noise, (s, n) => s + n).ToArray();
        }

        public static IEnumerable<AudioChunk> Chunks(float[] samples, int chunkMs = 20, TimeSpan start = default, AudioSource source = AudioSource.Them)
        {
            var size = SampleRate * chunkMs / 1000;
            for (var offset = 0; offset < samples.Length; offset += size)
            {
                var length = Math.Min(size, samples.Length - offset);
                yield return new AudioChunk(source, samples.AsSpan(offset, length).ToArray(), start + TimeSpan.FromSeconds((double)offset / SampleRate));
            }
        }

        public static int Samples(double seconds) => (int)Math.Round(seconds * SampleRate);
    }
}
