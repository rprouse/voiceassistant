using VoiceAssistant.Core.Segmentation;

namespace VoiceAssistant.Core.Tests.Segmentation
{
    public class FixedChunkerTests
    {
        private const int SampleRate = 16_000;

        [Test]
        public void NothingIsEmittedUntilAWindowFills()
        {
            var chunker = new FixedChunker(TimeSpan.FromSeconds(5));

            chunker.Add(Chunk(TimeSpan.Zero, SampleRate * 4)).ShouldBeEmpty();
        }

        [Test]
        public void TwelveSecondsMakeTwoFullWindowsAndAPartialOneOnFlush()
        {
            var chunker = new FixedChunker(TimeSpan.FromSeconds(5));
            var windows = new List<AudioChunk>();

            for (var second = 0; second < 12; second++)
                windows.AddRange(chunker.Add(Chunk(TimeSpan.FromSeconds(second), SampleRate, firstValue: second * SampleRate)));

            windows.Count.ShouldBe(2);
            windows.ShouldAllBe(w => w.Samples.Length == SampleRate * 5);
            windows.Select(w => w.Timestamp).ShouldBe([TimeSpan.Zero, TimeSpan.FromSeconds(5)]);

            var last = chunker.Flush();
            last.ShouldNotBeNull();
            last.Samples.Length.ShouldBe(SampleRate * 2);
            last.Timestamp.ShouldBe(TimeSpan.FromSeconds(10));
            chunker.Flush().ShouldBeNull();
        }

        [Test]
        public void ChunkStraddlingABoundaryIsSplitWithoutLosingSamples()
        {
            var chunker = new FixedChunker(TimeSpan.FromSeconds(1));

            // 2.5 s in one chunk, starting at 10 s, with samples numbered 0, 1, 2...
            var windows = chunker.Add(Chunk(TimeSpan.FromSeconds(10), SampleRate * 5 / 2));
            var rest = chunker.Flush()!;

            windows.Count.ShouldBe(2);
            windows[1].Timestamp.ShouldBe(TimeSpan.FromSeconds(11));
            rest.Timestamp.ShouldBe(TimeSpan.FromSeconds(12));
            windows.SelectMany(w => w.Samples).Concat(rest.Samples)
                .ShouldBe(Enumerable.Range(0, SampleRate * 5 / 2).Select(i => (float)i));
        }

        [Test]
        public void WindowsKeepTheirSource()
        {
            var chunker = new FixedChunker(TimeSpan.FromSeconds(1));

            var window = chunker.Add(Chunk(TimeSpan.Zero, SampleRate, source: AudioSource.Me)).Single();

            window.Source.ShouldBe(AudioSource.Me);
        }

        private static AudioChunk Chunk(TimeSpan timestamp, int length, int firstValue = 0, AudioSource source = AudioSource.Them) =>
            new(source, Enumerable.Range(firstValue, length).Select(i => (float)i).ToArray(), timestamp);
    }
}
