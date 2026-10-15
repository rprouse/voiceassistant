using VoiceAssistant.Core.Segmentation;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.Core.Tests.Segmentation
{
    public class UtteranceBuilderTests
    {
        private static readonly VadOptions Options = new();
        private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(30);

        [Test]
        public void SilenceProducesNoUtterances()
        {
            var updates = Run(TestAudio.WithRoomNoise(TestAudio.Silence(10)));

            updates.ShouldBeEmpty();
        }

        [Test]
        public void SingleSpokenSentenceProducesExactlyOneFinalUtterance()
        {
            var speech = TestAudio.LoadSample("quick-brown-fox.wav");
            var signal = TestAudio.WithRoomNoise(TestAudio.Concat(TestAudio.Silence(1), speech, TestAudio.Silence(1.5)));

            var finals = Run(signal).Where(u => u.IsFinal).ToList();

            finals.Count.ShouldBe(1);
            finals[0].Start.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(1) + SpeechStart(speech));
            finals[0].End.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1) + SpeechEnd(speech));
        }

        [Test]
        public void TwoSentencesSeparatedByOneSecondProduceTwoFinalUtterances()
        {
            var signal = TestAudio.WithRoomNoise(TestAudio.Concat(
                TestAudio.Silence(1), TestAudio.LoadSample("two-sentences.wav"), TestAudio.Silence(1.5)));

            var finals = Run(signal).Where(u => u.IsFinal).ToList();

            finals.Count.ShouldBe(2);
            finals[1].Start.ShouldBeGreaterThan(finals[0].End);
        }

        [Test]
        public void MonologueIsSplitWithoutExceedingTheMaximumLength()
        {
            var monologue = TestAudio.LoadSample("monologue.wav");
            var signal = TestAudio.WithRoomNoise(TestAudio.Concat(TestAudio.Silence(1), monologue, TestAudio.Silence(1.5)));

            var finals = Run(signal).Where(u => u.IsFinal).ToList();

            finals.Count.ShouldBeGreaterThanOrEqualTo(2);
            finals.ShouldAllBe(u => u.Duration <= TimeSpan.FromSeconds(Options.MaxUtteranceSeconds));
            finals.Sum(u => u.Duration.TotalSeconds).ShouldBeGreaterThan(monologue.Length / (double)TestAudio.SampleRate * 0.9);
        }

        [Test]
        public void ContinuousSoundIsCutAtTheQuietestRecentFrame()
        {
            // 20 s of unbroken sound with one brief, quieter moment 13 s in.
            var signal = TestAudio.WithRoomNoise(TestAudio.Concat(
                TestAudio.Silence(0.5), TestAudio.Tone(12.5, -20), TestAudio.Tone(0.06, -30), TestAudio.Tone(7, -20)));

            var first = Run(signal, flush: false).First(u => u.IsFinal);

            first.Duration.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(Options.MaxUtteranceSeconds));
            first.End.ShouldBe(TimeSpan.FromSeconds(13.06), Frame * 2);
        }

        [Test]
        public void PreRollIncludesAudioFromBeforeSpeechWasDetected()
        {
            var signal = TestAudio.WithRoomNoise(TestAudio.Concat(TestAudio.Silence(1), TestAudio.Tone(1, -20), TestAudio.Silence(1)));

            var final = Run(signal).Single(u => u.IsFinal);

            final.Start.ShouldBe(TimeSpan.FromSeconds(1) - TimeSpan.FromMilliseconds(Options.PreRollMs), Frame * 2);
        }

        [Test]
        public void ProvisionalSnapshotsArriveAboutOnceASecondAndShareTheFinalsId()
        {
            var signal = TestAudio.WithRoomNoise(TestAudio.Concat(TestAudio.Silence(1), TestAudio.Tone(4, -20), TestAudio.Silence(1)));

            var updates = Run(signal);
            var provisional = updates.Where(u => !u.IsFinal).ToList();
            var final = updates.Single(u => u.IsFinal);

            provisional.Count.ShouldBeInRange(3, 5);
            provisional.ShouldAllBe(u => u.UtteranceId == final.UtteranceId);
            provisional.Select(u => u.Samples.Length).ShouldBeInOrder(SortDirection.Ascending);
            updates.Last().ShouldBe(final);
        }

        [Test]
        public void GapInTheTimelineEndsTheUtterance()
        {
            var builder = new UtteranceBuilder(AudioSource.Them, Options, new EnergyVad(Options));
            foreach (var chunk in TestAudio.Chunks(TestAudio.WithRoomNoise(TestAudio.Concat(TestAudio.Silence(0.5), TestAudio.Tone(1.5, -20)))))
                builder.Add(chunk).Where(u => u.IsFinal).ShouldBeEmpty();

            // Loopback raised nothing for 5 s, then sound resumes.
            var resumed = new AudioChunk(AudioSource.Them, TestAudio.Tone(0.02, -20), TimeSpan.FromSeconds(7));
            var final = builder.Add(resumed).Single(u => u.IsFinal);

            final.End.ShouldBeLessThan(TimeSpan.FromSeconds(2.5));
            builder.IsSpeaking.ShouldBeFalse();
        }

        [Test]
        public void FlushCommitsTheUtteranceInProgress()
        {
            var builder = new UtteranceBuilder(AudioSource.Me, Options, new EnergyVad(Options));
            foreach (var chunk in TestAudio.Chunks(TestAudio.WithRoomNoise(TestAudio.Concat(TestAudio.Silence(0.5), TestAudio.Tone(2, -20))), source: AudioSource.Me))
                builder.Add(chunk);

            var final = builder.Flush();

            final.ShouldNotBeNull();
            final.IsFinal.ShouldBeTrue();
            final.Source.ShouldBe(AudioSource.Me);
            builder.Flush().ShouldBeNull();
        }

        [TestCase(7)]
        [TestCase(100)]
        [TestCase(1_000)]
        public void ChunkSizeDoesNotChangeTheResult(int chunkMs)
        {
            var signal = TestAudio.WithRoomNoise(TestAudio.Concat(
                TestAudio.Silence(1), TestAudio.LoadSample("two-sentences.wav"), TestAudio.Silence(1.5)));

            var finals = Run(signal, chunkMs: chunkMs).Where(u => u.IsFinal).ToList();
            var reference = Run(signal, chunkMs: 20).Where(u => u.IsFinal).ToList();

            finals.Select(u => (u.Start, u.Samples.Length)).ShouldBe(reference.Select(u => (u.Start, u.Samples.Length)));
        }

        // The TTS fixtures have silence at both ends; find where the speech itself is.
        private static TimeSpan SpeechStart(float[] speech) => At(Array.FindIndex(speech, s => Math.Abs(s) > 0.01f));

        private static TimeSpan SpeechEnd(float[] speech) => At(Array.FindLastIndex(speech, s => Math.Abs(s) > 0.01f));

        private static TimeSpan At(int sample) => TimeSpan.FromSeconds((double)sample / TestAudio.SampleRate);

        private static List<UtteranceUpdate> Run(float[] signal, int chunkMs = 20, bool flush = true)
        {
            var builder = new UtteranceBuilder(AudioSource.Them, Options, new EnergyVad(Options));
            var updates = TestAudio.Chunks(signal, chunkMs).SelectMany(builder.Add).ToList();
            if (flush && builder.Flush() is { } last)
                updates.Add(last);
            return updates;
        }
    }
}
