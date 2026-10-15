using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceAssistant.Core.Capture
{
    /// <summary>
    /// Captures everything playing on the default output device ("Them").
    /// WASAPI raises no data while nothing is playing; consumers fill those gaps
    /// from buffer timestamps.
    /// </summary>
    public sealed class LoopbackCapture : WasapiSourceCapture
    {
        public LoopbackCapture(SessionClock clock, CaptureOptions options, ILogger<LoopbackCapture> logger)
            : base(AudioSource.Them, clock, options, logger)
        {
        }

        protected override DataFlow DeviceFlow => DataFlow.Render;

        protected override WasapiRecorderBuilder Configure(WasapiRecorderBuilder builder) => builder.WithLoopbackCapture();
    }
}
