using Microsoft.Extensions.Logging.Abstractions;
using VoiceAssistant.Core.Transcription;

namespace VoiceAssistant.Core.Tests.Transcription
{
    /// <summary>Needs models/ggml-base.en.bin: run <c>VoiceAssistant model get base.en</c> first.</summary>
    [Category("Integration")]
    public class WhisperTranscriberTests
    {
        private WhisperTranscriber _transcriber = null!;

        [OneTimeSetUp]
        public void LoadModel()
        {
            var modelPath = Path.Combine(TestPaths.RepositoryRoot, "models", "ggml-base.en.bin");
            if (!File.Exists(modelPath))
                Assert.Ignore($"Model not found at {modelPath}. Run: VoiceAssistant model get base.en");

            _transcriber = new WhisperTranscriber(new WhisperOptions { ModelPath = modelPath }, NullLogger<WhisperTranscriber>.Instance);
        }

        [OneTimeTearDown]
        public void Unload() => _transcriber?.Dispose();

        [Test]
        public async Task TranscribesASpokenSentence()
        {
            var segments = await TranscribeAsync(TestAudio.LoadSample("quick-brown-fox.wav"), TimeSpan.Zero);

            string.Join(" ", segments.Select(s => s.Text)).ShouldContain("quick brown fox", Case.Insensitive);
        }

        [Test]
        public async Task SegmentTimesAreOffsetToSessionTime()
        {
            var start = TimeSpan.FromMinutes(3);

            var segments = await TranscribeAsync(TestAudio.LoadSample("quick-brown-fox.wav"), start);

            segments.ShouldNotBeEmpty();
            segments.ShouldAllBe(s => s.Start >= start && s.End > s.Start && s.Source == AudioSource.Them);
        }

        [Test]
        public async Task PromptedCallsWork()
        {
            var segments = await _transcriber.TranscribeAsync(
                AudioSource.Me, TimeSpan.Zero, TestAudio.LoadSample("quick-brown-fox.wav"), "Animals and pangrams.", CancellationToken.None).ToListAsync();

            string.Join(" ", segments.Select(s => s.Text)).ShouldContain("lazy dog", Case.Insensitive);
        }

        private Task<List<TranscriptSegment>> TranscribeAsync(float[] samples, TimeSpan start) =>
            _transcriber.TranscribeAsync(AudioSource.Them, start, samples, prompt: null, CancellationToken.None).ToListAsync().AsTask();
    }
}
