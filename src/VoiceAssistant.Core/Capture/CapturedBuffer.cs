using NAudio.Wave;

namespace VoiceAssistant.Core.Capture
{
    /// <summary>A block of audio exactly as the device delivered it.</summary>
    /// <param name="Timestamp">Session time of the first frame in <paramref name="Data"/>.</param>
    /// <param name="Format">
    /// The device's native format. It changes if the capture moves to another device,
    /// so consumers must check it rather than assume the format they started with.
    /// </param>
    public sealed record CapturedBuffer(AudioSource Source, byte[] Data, TimeSpan Timestamp, WaveFormat Format);
}
