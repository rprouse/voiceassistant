using Microsoft.Extensions.Logging.Abstractions;
using VoiceAssistant.Core.Pipeline;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.Core.Tests.Pipeline
{
    public class CaptionSessionShutdownTests
    {
        [Test]
        public async Task StoppingMidSentenceCommitsItAndSavesTheTranscript()
        {
            var directory = Directory.CreateTempSubdirectory("va-shutdown-").FullName;
            try
            {
                // The capture ends while speech is still going: no trailing silence to end the utterance.
                var capture = new FakeCapture(AudioSource.Me, TestAudio.WithRoomNoise(TestAudio.Concat(TestAudio.Silence(0.5), TestAudio.Tone(2, -20))));
                var transcript = new SessionTranscriptSink(directory, DateTimeOffset.Now);
                var transcriber = new FakeTranscriber { Respond = _ => "and that is all I wanted to" };
                var session = new CaptionSession(
                    [capture], transcriber, new VadOptions(), new HallucinationOptions(), 200, [transcript], NullLoggerFactory.Instance);

                session.Start();
                await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));

                transcript.HasContent.ShouldBeTrue();
                File.ReadAllText(transcript.FilePath).ShouldContain("**[00:00:00] Me:** and that is all I wanted to");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
