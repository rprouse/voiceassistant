using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceAssistant.Core.Capture
{
    /// <summary>
    /// Shared-mode WASAPI capture of the current default device for one source.
    /// The device callback only copies the buffer into a bounded channel; when the
    /// reader falls behind, the oldest buffers are dropped and counted.
    /// </summary>
    /// <remarks>
    /// When the default device changes, or the device disappears (a headset unplugged),
    /// the capture moves to the new default device without ending its buffer stream.
    /// The new device may use a different format, so every buffer carries its own.
    /// Only <see cref="StopAsync"/> ends the stream.
    /// </remarks>
    public abstract class WasapiSourceCapture : IAudioSourceCapture
    {
        // If a QPC timestamp disagrees with the wall clock by more than this, it is not trusted.
        private static readonly TimeSpan MaxQpcSkew = TimeSpan.FromSeconds(1);

        private static readonly TimeSpan WarningInterval = TimeSpan.FromSeconds(5);

        private readonly SessionClock _clock;
        private readonly CaptureOptions _options;
        private readonly ILogger _logger;
        private readonly Channel<CapturedBuffer> _channel;
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _switchGate = new(1, 1);
        private readonly MMDeviceEnumerator _enumerator = new();
        private MMDeviceNotificationClient? _notifications;
        private volatile RecorderSession? _current;
        private WaveFormat? _lastFormat;
        private volatile bool _stopRequested;
        private int _reconnecting;
        private long _droppedBuffers;
        private long _discontinuities;
        private long _reconnects;

        protected WasapiSourceCapture(AudioSource source, SessionClock clock, CaptureOptions options, ILogger logger)
        {
            Source = source;
            _clock = clock;
            _options = options;
            _logger = logger;
            _channel = Channel.CreateBounded<CapturedBuffer>(
                new BoundedChannelOptions(options.QueueCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true
                },
                _ => Interlocked.Increment(ref _droppedBuffers));
        }

        public AudioSource Source { get; }

        public string DeviceName { get; private set; } = string.Empty;

        public WaveFormat WaveFormat => _current?.Format ?? _lastFormat ?? throw new InvalidOperationException("Start the capture first.");

        public long DroppedBufferCount => Interlocked.Read(ref _droppedBuffers);

        public long DiscontinuityCount => Interlocked.Read(ref _discontinuities);

        public long ReconnectCount => Interlocked.Read(ref _reconnects);

        /// <summary>The endpoint type to capture: render for loopback, capture for microphones.</summary>
        protected abstract DataFlow DeviceFlow { get; }

        protected abstract WasapiRecorderBuilder Configure(WasapiRecorderBuilder builder);

        public void Start()
        {
            if (_current is not null || _lastFormat is not null)
                throw new InvalidOperationException("Capture has already been started.");

            StartRecorder();

            if (_options.RecoverFromDeviceChanges)
            {
                _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
                _notifications.DefaultDeviceChanged += OnDefaultDeviceChanged;
                _notifications.DeviceStateChanged += OnDeviceStateChanged;
            }
        }

        public async Task StopAsync()
        {
            if (_stopRequested)
            {
                await _stopped.Task.ConfigureAwait(false);
                return;
            }

            _stopRequested = true;
            DetachNotifications();

            // Let any device switch in progress finish, so there is one recorder to stop.
            await _switchGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_current is { } session)
                    session.Recorder.StopRecording();
                else
                    Finish(error: null); // between devices: nothing is recording
            }
            finally
            {
                _switchGate.Release();
            }

            await _stopped.Task.ConfigureAwait(false);
        }

        public async IAsyncEnumerable<CapturedBuffer> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Logging happens here on the reader, never in the device callback.
            var drops = new ThrottledCountReporter(WarningInterval);
            var glitches = new ThrottledCountReporter(WarningInterval);

            await foreach (var buffer in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var now = _clock.Elapsed;
                LogDrops(drops.Check(DroppedBufferCount, now));
                LogGlitches(glitches.Check(DiscontinuityCount, now));

                yield return buffer;
            }

            LogDrops(drops.Flush(DroppedBufferCount));
            LogGlitches(glitches.Flush(DiscontinuityCount));
        }

        public async ValueTask DisposeAsync()
        {
            if (_current is not null || _lastFormat is not null)
                await StopAsync().ConfigureAwait(false);

            DetachNotifications();
            if (_current is { } session)
                await session.Recorder.DisposeAsync().ConfigureAwait(false);
            _enumerator.Dispose();
            _switchGate.Dispose();
            GC.SuppressFinalize(this);
        }

        private void StartRecorder()
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DeviceFlow, _options.DeviceRole);
            var recorder = Configure(new WasapiRecorderBuilder().WithDevice(device).WithSharedMode()).Build();
            var session = new RecorderSession(recorder, device.ID, recorder.WaveFormat);

            // Each callback knows which recorder it came from, so a replaced recorder's
            // late callbacks are ignored without any locking on the audio thread.
            recorder.DataAvailable += (buffer, flags, _, qpcPosition) => OnDataAvailable(session, buffer, flags, qpcPosition);
            recorder.RecordingStopped += (_, e) => OnRecordingStopped(session, e);

            DeviceName = device.FriendlyName;
            _lastFormat = session.Format;
            _current = session;
            recorder.StartRecording();

            _logger.LogInformation("{Source} capture started on {Device} ({Format})", Source, DeviceName, session.Format);
        }

        private void OnDataAvailable(RecorderSession session, ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long qpcPosition)
        {
            if (!ReferenceEquals(session, _current))
                return;

            // The span is only valid during this callback, so copy it. A Silent flag
            // means the contents must be treated as silence whatever they contain.
            var data = flags.HasFlag(AudioClientBufferFlags.Silent) ? new byte[buffer.Length] : buffer.ToArray();

            // WASAPI routinely flags the first packet of a stream as a discontinuity; only later ones are glitches.
            if (flags.HasFlag(AudioClientBufferFlags.DataDiscontinuity) && session.ReceivedFirstBuffer)
                Interlocked.Increment(ref _discontinuities);
            session.ReceivedFirstBuffer = true;

            _channel.Writer.TryWrite(new CapturedBuffer(Source, data, GetTimestamp(session, buffer.Length, flags, qpcPosition), session.Format));
        }

        private TimeSpan GetTimestamp(RecorderSession session, int byteCount, AudioClientBufferFlags flags, long qpcPosition)
        {
            var now = _clock.Elapsed;
            var estimate = now - TimeSpan.FromSeconds((double)byteCount / session.Format.AverageBytesPerSecond);

            if (qpcPosition > 0 && !flags.HasFlag(AudioClientBufferFlags.TimestampError))
            {
                var fromDevice = _clock.FromQpcPosition(qpcPosition);
                if ((fromDevice - estimate).Duration() < MaxQpcSkew)
                    return fromDevice < TimeSpan.Zero ? TimeSpan.Zero : fromDevice;
            }

            return estimate < TimeSpan.Zero ? TimeSpan.Zero : estimate;
        }

        private void OnRecordingStopped(RecorderSession session, StoppedEventArgs e)
        {
            if (_stopRequested)
            {
                if (e.Exception is not null)
                    _logger.LogError(e.Exception, "{Source} capture stopped with an error", Source);
                else
                    _logger.LogInformation("{Source} capture stopped", Source);
                Finish(e.Exception);
                return;
            }

            // A recorder we replaced on purpose; nothing to do.
            if (!ReferenceEquals(session, _current))
                return;

            if (_options.RecoverFromDeviceChanges)
            {
                _logger.LogWarning(e.Exception, "{Source} capture lost {Device}; reconnecting to the default device", Source, DeviceName);
                Reconnect();
            }
            else
            {
                _logger.LogError(e.Exception, "{Source} capture stopped unexpectedly", Source);
                Finish(e.Exception);
            }
        }

        // Notification callbacks arrive on a COM thread that must not call back into the
        // device API, so reconnection always runs on the thread pool.
        private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
        {
            if (e.Flow != DeviceFlow || e.Role != _options.DeviceRole || e.DeviceId is null || e.DeviceId == _current?.DeviceId)
                return;

            _logger.LogInformation("{Source}: default device changed; switching", Source);
            Reconnect();
        }

        private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e)
        {
            if (e.DeviceId == _current?.DeviceId && e.NewState != DeviceState.Active)
            {
                _logger.LogWarning("{Source}: {Device} is now {State}; reconnecting", Source, DeviceName, e.NewState);
                Reconnect();
            }
        }

        /// <summary>Moves to the current default device, retrying until one is available.</summary>
        private void Reconnect()
        {
            if (Interlocked.Exchange(ref _reconnecting, 1) == 1)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var retry = TimeSpan.FromMilliseconds(_options.ReconnectIntervalMs);
                    while (!_stopRequested && !await TrySwitchDeviceAsync().ConfigureAwait(false))
                        await Task.Delay(retry).ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref _reconnecting, 0);
                }
            });
        }

        private async Task<bool> TrySwitchDeviceAsync()
        {
            await _switchGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_stopRequested)
                    return true;

                var old = _current;
                _current = null;
                if (old is not null)
                    await ReleaseAsync(old).ConfigureAwait(false);

                StartRecorder();
                Interlocked.Increment(ref _reconnects);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("{Source}: no device to capture yet ({Message}); retrying", Source, ex.Message);
                return false;
            }
            finally
            {
                _switchGate.Release();
            }
        }

        private async Task ReleaseAsync(RecorderSession session)
        {
            try
            {
                if (session.Recorder.CaptureState is CaptureState.Capturing or CaptureState.Starting)
                    session.Recorder.StopRecording();
                await session.Recorder.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The device may already be gone; that's why we're switching.
                _logger.LogDebug(ex, "{Source}: error releasing the previous recorder", Source);
            }
        }

        private void Finish(Exception? error)
        {
            _channel.Writer.TryComplete(error);
            _stopped.TrySetResult();
        }

        private void DetachNotifications()
        {
            if (Interlocked.Exchange(ref _notifications, null) is { } notifications)
            {
                notifications.DefaultDeviceChanged -= OnDefaultDeviceChanged;
                notifications.DeviceStateChanged -= OnDeviceStateChanged;
                notifications.Dispose();
            }
        }

        private void LogDrops(long? increase)
        {
            if (increase is { } count)
                _logger.LogWarning("{Source} capture queue full: {Count} more buffer(s) dropped, {Total} total", Source, count, DroppedBufferCount);
        }

        private void LogGlitches(long? increase)
        {
            if (increase is { } count)
                _logger.LogWarning("{Source} device skipped audio: {Count} more data discontinuity(ies), {Total} total", Source, count, DiscontinuityCount);
        }

        private sealed class RecorderSession(WasapiRecorder recorder, string deviceId, WaveFormat format)
        {
            public WasapiRecorder Recorder { get; } = recorder;

            public string DeviceId { get; } = deviceId;

            public WaveFormat Format { get; } = format;

            public bool ReceivedFirstBuffer { get; set; }
        }
    }
}
