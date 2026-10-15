using NAudio.Wave;

namespace VoiceAssistant.Core.Capture
{
    public interface IAudioSourceCapture : IAsyncDisposable
    {
        AudioSource Source { get; }

        /// <summary>Friendly name of the device being captured. Available after <see cref="Start"/>.</summary>
        string DeviceName { get; }

        /// <summary>Native device format of every <see cref="CapturedBuffer"/>. Available after <see cref="Start"/>.</summary>
        WaveFormat WaveFormat { get; }

        /// <summary>Buffers discarded because the reader fell behind.</summary>
        long DroppedBufferCount { get; }

        /// <summary>
        /// Times the device itself reported skipped audio (WASAPI data discontinuity),
        /// e.g. Bluetooth packet loss. Counted separately from our own drops.
        /// </summary>
        long DiscontinuityCount { get; }

        void Start();

        /// <summary>Stops capturing. Buffers already queued can still be read, then the stream ends.</summary>
        Task StopAsync();

        IAsyncEnumerable<CapturedBuffer> ReadAllAsync(CancellationToken cancellationToken = default);
    }
}
