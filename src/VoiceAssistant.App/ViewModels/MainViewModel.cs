using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VoiceAssistant.Core;
using VoiceAssistant.Core.Capture;
using VoiceAssistant.Core.Pipeline;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.App.ViewModels
{
    /// <summary>
    /// Runs caption sessions and shows their transcript. As an <see cref="ITranscriptSink"/>
    /// it receives events on a background thread and marshals them to the UI thread.
    /// </summary>
    public sealed partial class MainViewModel : ObservableObject, ITranscriptSink, IAsyncDisposable
    {
        private readonly CaptureOptions _captureOptions;
        private readonly WhisperOptions _whisperOptions;
        private readonly VadOptions _vadOptions;
        private readonly HallucinationOptions _hallucinationOptions;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<MainViewModel> _logger;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer;
        private readonly CaptionTimeline _timeline = new();

        private WhisperTranscriber? _transcriber;
        private CaptionSession? _session;
        private SessionClock? _clock;
        private DefaultDeviceMonitor? _deviceMonitor;
        private List<IAudioSourceCapture> _captures = [];

        public MainViewModel(
            IOptions<CaptureOptions> captureOptions,
            IOptions<WhisperOptions> whisperOptions,
            IOptions<VadOptions> vadOptions,
            IOptions<HallucinationOptions> hallucinationOptions,
            ILoggerFactory loggerFactory)
        {
            _captureOptions = captureOptions.Value;
            _whisperOptions = whisperOptions.Value;
            _vadOptions = vadOptions.Value;
            _hallucinationOptions = hallucinationOptions.Value;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<MainViewModel>();
            _dispatcher = Dispatcher.CurrentDispatcher;
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Refresh(), _dispatcher);
            _timer.Stop();
        }

        public ObservableCollection<CaptionViewModel> Captions { get; } = [];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToggleText))]
        public partial bool IsRunning { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        public partial string Elapsed { get; set; } = "00:00:00";

        [ObservableProperty]
        public partial string Status { get; set; } = "Loading model…";

        [ObservableProperty]
        public partial bool IsAlwaysOnTop { get; set; }

        [ObservableProperty]
        public partial double WindowOpacity { get; set; } = 1.0;

        public string ToggleText => IsRunning ? "Stop" : "Start";

        /// <summary>Loads the model in the background, so the window appears straight away.</summary>
        public async Task InitializeAsync()
        {
            IsBusy = true;
            try
            {
                _transcriber = await Task.Run(() => new WhisperTranscriber(_whisperOptions, _loggerFactory.CreateLogger<WhisperTranscriber>()));
                Status = $"Ready · {_transcriber.Description}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not load the Whisper model");
                Status = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public ValueTask OnEventAsync(TranscriptEvent transcriptEvent, CancellationToken cancellationToken) =>
            new(_dispatcher.InvokeAsync(() => Apply(transcriptEvent), DispatcherPriority.Normal, cancellationToken).Task);

        /// <summary>Stops a running session, committing what was being said.</summary>
        public async Task StopAsync()
        {
            if (_session is null)
                return;

            IsBusy = true;
            _timer.Stop();
            Status = "Stopping…";
            try
            {
                await Task.Run(_session.StopAsync);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping the session");
            }
            finally
            {
                await ReleaseCapturesAsync();
                _session = null;
                IsRunning = false;
                IsBusy = false;
                Refresh();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _transcriber?.Dispose();
            _transcriber = null;
        }

        [RelayCommand(CanExecute = nameof(CanToggle))]
        private Task ToggleAsync() => IsRunning ? StopAsync() : StartAsync();

        private bool CanToggle() => !IsBusy && _transcriber is not null;

        private async Task StartAsync()
        {
            if (_transcriber is null)
                return;

            if (!_captureOptions.EnableLoopback && !_captureOptions.EnableMic)
            {
                Status = "Both captures are disabled in appsettings.json (Capture:EnableLoopback, Capture:EnableMic).";
                return;
            }

            IsBusy = true;
            Status = "Starting…";
            try
            {
                Captions.Clear();
                _timeline.Clear();

                _clock = new SessionClock();
                _deviceMonitor = new DefaultDeviceMonitor(_loggerFactory.CreateLogger<DefaultDeviceMonitor>());
                _captures = [];
                if (_captureOptions.EnableLoopback)
                    _captures.Add(new LoopbackCapture(_clock, _captureOptions, _loggerFactory.CreateLogger<LoopbackCapture>()));
                if (_captureOptions.EnableMic)
                    _captures.Add(new MicCapture(_clock, _captureOptions, _loggerFactory.CreateLogger<MicCapture>()));

                var session = new CaptionSession(
                    _captures, _transcriber, _vadOptions, _hallucinationOptions, _whisperOptions.PromptCharacters, [this], _loggerFactory);

                // Opening audio devices can block; keep it off the UI thread.
                await Task.Run(session.Start);
                _session = session;
                IsRunning = true;
                _timer.Start();
                Refresh();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not start the session");
                Status = $"Could not start: {ex.Message}";
                await ReleaseCapturesAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        // The timeline keeps both sources' captions in start-time order and says where
        // each change goes; the collection mirrors it.
        private void Apply(TranscriptEvent transcriptEvent)
        {
            switch (_timeline.Apply(transcriptEvent))
            {
                case CaptionInserted inserted:
                    Captions.Insert(inserted.Index, new CaptionViewModel(inserted.Segment));
                    break;
                case CaptionUpdated updated:
                    Captions[updated.Index].Update(updated.Segment);
                    break;
                case CaptionRemoved removed:
                    Captions.RemoveAt(removed.Index);
                    break;
            }
        }

        private void Refresh()
        {
            if (_clock is not null && IsRunning)
                Elapsed = _clock.Elapsed.ToString(@"hh\:mm\:ss");

            if (_transcriber is null || IsBusy && !IsRunning)
                return;

            var parts = new List<string> { _transcriber.Description };
            if (_session is { Coordinator.RealTimeFactor: var rtf } && !double.IsNaN(rtf))
                parts.Add($"RTF {rtf:F2}");
            if (_captures.Count > 0)
            {
                parts.Add($"dropped {_captures.Sum(c => c.DroppedBufferCount)}");
                parts.Add($"device glitches {_captures.Sum(c => c.DiscontinuityCount)}");
            }

            Status = string.Join(" · ", parts);
        }

        private async Task ReleaseCapturesAsync()
        {
            foreach (var capture in _captures)
            {
                try
                {
                    await capture.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error releasing the {Source} capture", capture.Source);
                }
            }

            _deviceMonitor?.Dispose();
            _deviceMonitor = null;
        }
    }
}
