using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceAssistant.Core.Capture
{
    /// <summary>Captures the default microphone ("Me").</summary>
    public sealed class MicCapture : WasapiSourceCapture
    {
        public MicCapture(SessionClock clock, CaptureOptions options, ILogger<MicCapture> logger)
            : base(AudioSource.Me, clock, options, logger)
        {
        }

        protected override DataFlow DeviceFlow => DataFlow.Capture;

        protected override WasapiRecorderBuilder Configure(WasapiRecorderBuilder builder) => builder;
    }
}
