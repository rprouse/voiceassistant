using VoiceAssistant.Core.Transcription;

namespace VoiceAssistant.Core.Tests.Transcription
{
    public class ModelCatalogTests
    {
        [TestCase("base.en", "ggml-base.en.bin")]
        [TestCase("small.en", "ggml-small.en.bin")]
        [TestCase("Large-V3-Turbo", "ggml-large-v3-turbo.bin")]
        public void FileNameFollowsWhisperCppConvention(string name, string expected)
        {
            ModelCatalog.FileName(name).ShouldBe(expected);
        }

        [TestCase("base.en", true)]
        [TestCase("SMALL.EN", true)]
        [TestCase("huge", false)]
        public void KnowsTheDownloadableModels(string name, bool known)
        {
            ModelCatalog.IsKnown(name).ShouldBe(known);
        }

        [Test]
        public void EveryDownloadableModelIsDescribed()
        {
            ModelCatalog.Models.Select(m => m.Name).ShouldBe(ModelCatalog.Names, ignoreOrder: true);
            ModelCatalog.Models.ShouldAllBe(m => m.ApproximateSizeMb > 0 && m.Note.Length > 0);
        }

        [Test]
        public void DefaultConfiguredModelIsDownloadable()
        {
            var fileName = Path.GetFileName(new WhisperOptions().ModelPath);

            ModelCatalog.Names.Select(ModelCatalog.FileName).ShouldContain(fileName);
        }
    }
}
