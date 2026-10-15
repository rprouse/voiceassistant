namespace VoiceAssistant.Core.Capture
{
    /// <summary>
    /// A block of audio exactly as the device delivered it, in the capture's native
    /// <see cref="IAudioSourceCapture.WaveFormat"/>.
    /// </summary>
    /// <param name="Timestamp">Session time of the first frame in <paramref name="Data"/>.</param>
    public sealed record CapturedBuffer(AudioSource Source, byte[] Data, TimeSpan Timestamp);
}
