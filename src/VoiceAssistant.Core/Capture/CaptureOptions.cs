using NAudio.CoreAudioApi;

namespace VoiceAssistant.Core.Capture
{
    public sealed class CaptureOptions
    {
        public const string SectionName = "Capture";

        public bool EnableMic { get; set; } = true;

        public bool EnableLoopback { get; set; } = true;

        /// <summary>
        /// Which default device to capture. Meeting apps play and record through the
        /// Communications device, which can differ from the Console/Multimedia default.
        /// </summary>
        public Role DeviceRole { get; set; } = Role.Communications;

        /// <summary>Buffers queued between capture and the next stage before the oldest are dropped.</summary>
        public int QueueCapacity { get; set; } = 500;

        /// <summary>Follow the default device when it changes or the current one disappears.</summary>
        public bool RecoverFromDeviceChanges { get; set; } = true;

        /// <summary>How often to retry while no device is available.</summary>
        public int ReconnectIntervalMs { get; set; } = 2000;

        /// <summary>Timeline gaps larger than this are filled with silence.</summary>
        public int GapToleranceMs { get; set; } = 50;
    }
}
