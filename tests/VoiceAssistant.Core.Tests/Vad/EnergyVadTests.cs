using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.Core.Tests.Vad
{
    public class EnergyVadTests
    {
        private const int FrameSize = 480; // 30 ms at 16 kHz

        [Test]
        public void DigitalSilenceIsNeverSpeech()
        {
            Classify(new EnergyVad(new VadOptions()), TestAudio.Silence(2)).ShouldAllBe(speech => !speech);
        }

        [Test]
        public void SteadyRoomNoiseIsNotSpeech()
        {
            Classify(new EnergyVad(new VadOptions()), TestAudio.Noise(3, -55)).ShouldAllBe(speech => !speech);
        }

        [Test]
        public void LoudSoundAfterQuietNoiseIsSpeechImmediately()
        {
            var vad = new EnergyVad(new VadOptions());
            Classify(vad, TestAudio.Noise(1, -60));

            Classify(vad, TestAudio.Tone(0.3, -20)).ShouldAllBe(speech => speech);
        }

        [Test]
        public void SoundsQuieterThanTheMinimumLevelAreNotSpeech()
        {
            var vad = new EnergyVad(new VadOptions());
            Classify(vad, TestAudio.Silence(1));

            // Far above digital silence, but below MinSpeechLevelDb (-50).
            Classify(vad, TestAudio.Tone(0.5, -55)).ShouldAllBe(speech => !speech);
        }

        [Test]
        public void HysteresisNeedsMoreToStartSpeechThanToContinueIt()
        {
            // 7 dB above a -45 dB floor: between the stay (5 dB) and start (10 dB) margins.
            var inBetween = TestAudio.Tone(0.3, -38);

            var idle = new EnergyVad(new VadOptions());
            Classify(idle, TestAudio.Noise(1, -45));
            Classify(idle, inBetween).ShouldAllBe(speech => !speech);

            var speaking = new EnergyVad(new VadOptions());
            Classify(speaking, TestAudio.Noise(1, -45));
            Classify(speaking, TestAudio.Tone(0.1, -20)).Last().ShouldBeTrue();
            Classify(speaking, inBetween).ShouldAllBe(speech => speech);
        }

        [Test]
        public void NoiseFloorRisesToMeetALouderRoom()
        {
            var vad = new EnergyVad(new VadOptions());
            Classify(vad, TestAudio.Noise(1, -65));

            // An air conditioner switches on: speech at first, then learned as background.
            var louder = Classify(vad, TestAudio.Noise(20, -40, seed: 2));

            louder.Take(10).ShouldAllBe(speech => speech);
            louder.TakeLast(30).ShouldAllBe(speech => !speech);
            vad.NoiseFloorDb.ShouldBeGreaterThan(-45);
        }

        private static List<bool> Classify(EnergyVad vad, float[] samples)
        {
            var results = new List<bool>();
            for (var offset = 0; offset + FrameSize <= samples.Length; offset += FrameSize)
                results.Add(vad.IsSpeech(samples.AsSpan(offset, FrameSize)));
            return results;
        }
    }
}
