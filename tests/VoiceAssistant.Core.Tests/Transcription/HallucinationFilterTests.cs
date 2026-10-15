using VoiceAssistant.Core.Transcription;

namespace VoiceAssistant.Core.Tests.Transcription
{
    public class HallucinationFilterTests
    {
        private static readonly HallucinationFilter Filter = new(new HallucinationOptions
        {
            Blocklist = ["Thank you.", "Thanks for watching!"],
            NearNoiseFloorDb = 6
        });

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("...")]
        [TestCase("[BLANK_AUDIO]")]
        [TestCase("(music)")]
        [TestCase("*laughs* - !")]
        public void TextWithoutWordsIsNonSpeech(string text)
        {
            HallucinationFilter.IsNonSpeech(text).ShouldBeTrue();
            Filter.ShouldDrop(text, meanLevelDb: -20, ambientLevelDb: -60).ShouldBeTrue();
        }

        [Test]
        public void AnnotationsAreRemovedFromRealSpeech()
        {
            HallucinationFilter.Clean("Hello [BLANK_AUDIO] there  (music)").ShouldBe("Hello there");
        }

        [TestCase("Thank you.")]
        [TestCase("thank you")]
        [TestCase("  Thanks for watching ")]
        public void BlocklistedPhraseFromNearSilentAudioIsDropped(string text)
        {
            Filter.ShouldDrop(text, meanLevelDb: -58, ambientLevelDb: -60).ShouldBeTrue();
        }

        [Test]
        public void BlocklistedPhraseFromClearSpeechIsKept()
        {
            // Someone really did say "Thank you."
            Filter.ShouldDrop("Thank you.", meanLevelDb: -25, ambientLevelDb: -60).ShouldBeFalse();
        }

        [Test]
        public void OtherQuietSpeechIsKept()
        {
            Filter.ShouldDrop("Can everyone hear me?", meanLevelDb: -58, ambientLevelDb: -60).ShouldBeFalse();
        }
    }
}
