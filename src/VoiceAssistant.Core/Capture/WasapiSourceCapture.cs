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
    public abstract class WasapiSourceCapture : IAudioSourceCapture
    {
        // If a QPC timestamp disagrees with the wall clock by more than this, it is not trusted.
        private static readonly TimeSpan MaxQpcSkew = TimeSpan.FromSeconds(1);

        private static readonly TimeSpan WarningInterval = TimeSpan.FromSeconds(5);

        private readonly SessionClock _clock;
        private readonly Role _deviceRole;
        private readonly ILogger _logger;
        private readonly Channel<CapturedBuffer> _channel;
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MMDeviceEnumerator _enumerator = new();
        private WasapiRecorder? _recorder;
        private int _bytesPerSecond;
        private long _droppedBuffers;
        private long _discontinuities;
        private bool _receivedFirstBuffer;

        protected WasapiSourceCapture(AudioSource source, SessionClock clock, CaptureOptions options, ILogger logger)
        {
            Source = source;
            _clock = clock;
            _deviceRole = options.DeviceRole;
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

        public WaveFormat WaveFormat => _recorder?.WaveFormat ?? throw new InvalidOperationException("Start the capture first.");

        public long DroppedBufferCount => Interlocked.Read(ref _droppedBuffers);

        public long DiscontinuityCount => Interlocked.Read(ref _discontinuities);

        /// <summary>The endpoint type to capture: render for loopback, capture for microphones.</summary>
        protected abstract DataFlow DeviceFlow { get; }

        protected abstract WasapiRecorderBuilder Configure(WasapiRecorderBuilder builder);

        public void Start()
        {
            if (_recorder is not null)
                throw new InvalidOperationException("Capture has already been started.");

            var device = _enumerator.GetDefaultAudioEndpoint(DeviceFlow, _deviceRole);
            DeviceName = device.FriendlyName;

            _recorder = Configure(new WasapiRecorderBuilder().WithDevice(device).WithSharedMode()).Build();
            _bytesPerSecond = _recorder.WaveFormat.AverageBytesPerSecond;
            _recorder.DataAvailable += OnDataAvailable;
            _recorder.RecordingStopped += OnRecordingStopped;
            _recorder.StartRecording();

            _logger.LogInformation("{Source} capture started on {Device} ({Format})", Source, DeviceName, _recorder.WaveFormat);
        }

        public async Task StopAsync()
        {
            if (_recorder is null || _stopped.Task.IsCompleted)
                return;

            _recorder.StopRecording();
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
            if (_recorder is not null)
            {
                await StopAsync().ConfigureAwait(false);
                _recorder.DataAvailable -= OnDataAvailable;
                _recorder.RecordingStopped -= OnRecordingStopped;
                await _recorder.DisposeAsync().ConfigureAwait(false);
            }

            _enumerator.Dispose();
            GC.SuppressFinalize(this);
        }

        private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
        {
            // The span is only valid during this callback, so copy it. A Silent flag
            // means the contents must be treated as silence whatever they contain.
            var data = flags.HasFlag(AudioClientBufferFlags.Silent) ? new byte[buffer.Length] : buffer.ToArray();

            // WASAPI routinely flags the first packet of a stream as a discontinuity; only later ones are glitches.
            if (flags.HasFlag(AudioClientBufferFlags.DataDiscontinuity) && _receivedFirstBuffer)
                Interlocked.Increment(ref _discontinuities);
            _receivedFirstBuffer = true;

            _channel.Writer.TryWrite(new CapturedBuffer(Source, data, GetTimestamp(buffer.Length, flags, qpcPosition)));
        }

        private TimeSpan GetTimestamp(int byteCount, AudioClientBufferFlags flags, long qpcPosition)
        {
            var now = _clock.Elapsed;
            var estimate = now - TimeSpan.FromSeconds((double)byteCount / _bytesPerSecond);

            if (qpcPosition > 0 && !flags.HasFlag(AudioClientBufferFlags.TimestampError))
            {
                var fromDevice = _clock.FromQpcPosition(qpcPosition);
                if ((fromDevice - estimate).Duration() < MaxQpcSkew)
                    return fromDevice < TimeSpan.Zero ? TimeSpan.Zero : fromDevice;
            }

            return estimate < TimeSpan.Zero ? TimeSpan.Zero : estimate;
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

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception is not null)
                _logger.LogError(e.Exception, "{Source} capture stopped with an error", Source);
            else
                _logger.LogInformation("{Source} capture stopped", Source);

            _channel.Writer.TryComplete(e.Exception);
            _stopped.TrySetResult();
        }
    }
}
