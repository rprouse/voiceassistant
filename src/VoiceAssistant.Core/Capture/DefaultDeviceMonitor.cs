using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace VoiceAssistant.Core.Capture
{
    /// <summary>
    /// Logs changes to the default audio devices. Each capture follows its own role's
    /// default device (see <see cref="CaptureOptions.RecoverFromDeviceChanges"/>).
    /// </summary>
    public sealed class DefaultDeviceMonitor : IDisposable
    {
        private readonly ILogger<DefaultDeviceMonitor> _logger;
        private readonly MMDeviceEnumerator _enumerator = new();
        private readonly MMDeviceNotificationClient _client;

        public DefaultDeviceMonitor(ILogger<DefaultDeviceMonitor> logger)
        {
            _logger = logger;

            // Callbacks arrive on a COM thread; logging there is fine, but don't touch devices from it.
            _client = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
            _client.DefaultDeviceChanged += OnDefaultDeviceChanged;
            _client.DeviceStateChanged += OnDeviceStateChanged;
        }

        public event EventHandler<DefaultDeviceChangedEventArgs>? DefaultDeviceChanged;

        public void Dispose()
        {
            _client.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            _client.DeviceStateChanged -= OnDeviceStateChanged;
            _client.Dispose();
            _enumerator.Dispose();
        }

        private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
        {
            _logger.LogWarning(
                "Default {Flow} device for role {Role} changed to {DeviceId}",
                e.Flow, e.Role, e.DeviceId);
            DefaultDeviceChanged?.Invoke(this, e);
        }

        private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e) =>
            _logger.LogInformation("Audio device {DeviceId} is now {State}", e.DeviceId, e.NewState);
    }
}
