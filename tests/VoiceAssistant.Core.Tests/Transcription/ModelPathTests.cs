using VoiceAssistant.Core.Transcription;

namespace VoiceAssistant.Core.Tests.Transcription
{
    public class ModelPathTests
    {
        [Test]
        public void AbsolutePathsAreUsedAsGiven()
        {
            var path = Path.Combine(Path.GetTempPath(), "model.bin");

            ModelPath.Resolve(path).ShouldBe(path);
        }

        [Test]
        public void RelativePathIsFoundAboveTheExecutable()
        {
            // The tests run from tests\...\bin\Debug\net10.0-windows; samples\ is at the repository root.
            var resolved = ModelPath.Resolve("samples/quick-brown-fox.wav");

            File.Exists(resolved).ShouldBeTrue();
            resolved.ShouldStartWith(TestPaths.RepositoryRoot);
        }

        [Test]
        public void MissingFileResolvesAgainstTheCurrentDirectory()
        {
            ModelPath.Resolve("models/does-not-exist.bin").ShouldBe(Path.GetFullPath("models/does-not-exist.bin"));
        }
    }
}
