using VoiceAssistant.Core.Conversion;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.Core.Segmentation
{
    /// <summary>
    /// Turns one source's converted audio into utterances. Idle until speech lasts
    /// <see cref="VadOptions.MinSpeechMs"/> (keeping <see cref="VadOptions.PreRollMs"/> of
    /// audio from before it), then Speaking until <see cref="VadOptions.EndSilenceMs"/> of
    /// silence or <see cref="VadOptions.MaxUtteranceSeconds"/>, when the utterance is cut at
    /// the quietest recent frame instead of mid-word. While Speaking, a provisional
    /// snapshot is emitted every <see cref="VadOptions.ProvisionalIntervalMs"/>.
    /// </summary>
    public sealed class UtteranceBuilder
    {
        // When an utterance hits the maximum length, the cut point is searched for in this much recent audio.
        private const int CutSearchMs = 3_000;

        // Smoothing for the ambient (non-speech) level estimate, per frame.
        private const double AmbientSmoothing = 0.05;

        // Gaps in the incoming timeline longer than this are treated as silence.
        private static readonly TimeSpan GapTolerance = TimeSpan.FromMilliseconds(100);

        private static readonly long TicksPerSample = TimeSpan.TicksPerSecond / AudioConverter.OutputSampleRate;

        private readonly AudioSource _source;
        private readonly IVoiceActivityDetector _vad;
        private readonly int _frameSize;
        private readonly int _minSpeechFrames;
        private readonly int _preRollFrames;
        private readonly int _endSilenceFrames;
        private readonly int _maxFrames;
        private readonly int _provisionalFrames;
        private readonly int _cutSearchFrames;

        private readonly float[] _pending;
        private int _pendingCount;
        private TimeSpan _pendingStart;
        private TimeSpan? _position;

        private readonly List<Frame> _history = [];
        private int _speechRun;

        private List<Frame>? _utterance;
        private Guid _utteranceId;
        private int _silenceRun;
        private int _sinceProvisional;

        private double _ambientDb = double.NaN;

        public UtteranceBuilder(AudioSource source, VadOptions options, IVoiceActivityDetector vad)
        {
            _source = source;
            _vad = vad;
            _frameSize = AudioConverter.OutputSampleRate * options.FrameMs / 1000;
            _minSpeechFrames = Math.Max(1, FramesFor(options.MinSpeechMs, options));
            _preRollFrames = FramesFor(options.PreRollMs, options);
            _endSilenceFrames = Math.Max(1, FramesFor(options.EndSilenceMs, options));
            _maxFrames = Math.Max(2, FramesFor(options.MaxUtteranceSeconds * 1000, options));
            _provisionalFrames = Math.Max(1, FramesFor(options.ProvisionalIntervalMs, options));
            _cutSearchFrames = Math.Clamp(FramesFor(CutSearchMs, options), 1, _maxFrames - 1);
            _pending = new float[_frameSize];
        }

        public bool IsSpeaking => _utterance is not null;

        public IReadOnlyList<UtteranceUpdate> Add(AudioChunk chunk)
        {
            var updates = new List<UtteranceUpdate>();

            if (_position is { } position && chunk.Timestamp - position > GapTolerance)
                HandleGap(chunk.Timestamp - position, updates);
            if (_position is null || _pendingCount == 0 && chunk.Timestamp > _position)
                _position = chunk.Timestamp;

            var samples = chunk.Samples.AsSpan();
            while (!samples.IsEmpty)
            {
                if (_pendingCount == 0)
                    _pendingStart = _position.Value;

                var count = Math.Min(_frameSize - _pendingCount, samples.Length);
                samples[..count].CopyTo(_pending.AsSpan(_pendingCount));
                samples = samples[count..];
                _pendingCount += count;
                _position = _position.Value + TimeSpan.FromTicks(count * TicksPerSample);

                if (_pendingCount == _frameSize)
                {
                    ProcessFrame(_pending.ToArray(), _pendingStart, forcedSilence: false, updates);
                    _pendingCount = 0;
                }
            }

            return updates;
        }

        /// <summary>Ends any utterance in progress, e.g. when capture stops.</summary>
        /// <returns>The final snapshot of the in-progress utterance, or null if there wasn't one.</returns>
        public UtteranceUpdate? Flush()
        {
            if (_utterance is null)
                return null;

            var frames = _utterance;
            EndUtterance();
            return HasSpeech(frames) ? CreateUpdate(frames, isFinal: true) : null;
        }

        private void HandleGap(TimeSpan gap, List<UtteranceUpdate> updates)
        {
            if (_pendingCount > 0)
            {
                Array.Clear(_pending, _pendingCount, _frameSize - _pendingCount);
                ProcessFrame(_pending.ToArray(), _pendingStart, forcedSilence: false, updates);
                _pendingCount = 0;
            }

            if (_utterance is not null)
            {
                // The source went quiet (loopback raises nothing while nothing plays):
                // feed enough silence to end the utterance, but no more.
                var frames = (int)Math.Min(gap.Ticks / (_frameSize * TicksPerSample), _endSilenceFrames);
                var start = _position!.Value;
                for (var i = 0; i < frames && _utterance is not null; i++)
                {
                    ProcessFrame(new float[_frameSize], start, forcedSilence: true, updates);
                    start += TimeSpan.FromTicks(_frameSize * TicksPerSample);
                }
            }

            // Pre-roll from before the gap belongs to a different moment.
            _history.Clear();
            _speechRun = 0;
            _position = null;
        }

        private void ProcessFrame(float[] samples, TimeSpan start, bool forcedSilence, List<UtteranceUpdate> updates)
        {
            var meanSquare = AudioLevel.MeanSquare(samples);
            var isSpeech = !forcedSilence && _vad.IsSpeech(samples);

            if (!isSpeech && !forcedSilence)
            {
                var level = AudioLevel.ToDb(meanSquare);
                _ambientDb = double.IsNaN(_ambientDb) ? level : _ambientDb + (level - _ambientDb) * AmbientSmoothing;
            }

            var frame = new Frame(samples, start, meanSquare, isSpeech);
            if (_utterance is null)
                WhileIdle(frame);
            else
                WhileSpeaking(frame, updates);
        }

        private void WhileIdle(Frame frame)
        {
            _history.Add(frame);
            if (_history.Count > _preRollFrames + _minSpeechFrames)
                _history.RemoveAt(0);

            _speechRun = frame.IsSpeech ? _speechRun + 1 : 0;
            if (_speechRun < _minSpeechFrames)
                return;

            BeginUtterance([.. _history]);
            _history.Clear();
            _speechRun = 0;
        }

        private void WhileSpeaking(Frame frame, List<UtteranceUpdate> updates)
        {
            var utterance = _utterance!;
            utterance.Add(frame);
            _silenceRun = frame.IsSpeech ? 0 : _silenceRun + 1;
            _sinceProvisional++;

            if (_silenceRun >= _endSilenceFrames)
            {
                // Keep a little trailing silence (as much as the pre-roll), and let the rest
                // seed the next utterance's pre-roll.
                var keep = utterance.Count - _silenceRun + Math.Min(_silenceRun, _preRollFrames);
                var kept = utterance.GetRange(0, keep);
                if (HasSpeech(kept))
                    updates.Add(CreateUpdate(kept, isFinal: true));
                var trailing = utterance.GetRange(keep, utterance.Count - keep);
                EndUtterance();
                _history.AddRange(trailing.TakeLast(_preRollFrames + _minSpeechFrames));
            }
            else if (utterance.Count >= _maxFrames)
            {
                var cut = QuietestRecentFrame(utterance);
                updates.Add(CreateUpdate(utterance.GetRange(0, cut + 1), isFinal: true));
                BeginUtterance(utterance.GetRange(cut + 1, utterance.Count - cut - 1));
            }
            else if (_sinceProvisional >= _provisionalFrames)
            {
                updates.Add(CreateUpdate(utterance, isFinal: false));
                _sinceProvisional = 0;
            }
        }

        private int QuietestRecentFrame(List<Frame> frames)
        {
            var quietest = frames.Count - 1;
            for (var i = frames.Count - 1; i >= frames.Count - _cutSearchFrames; i--)
            {
                if (frames[i].MeanSquare < frames[quietest].MeanSquare)
                    quietest = i;
            }

            return quietest;
        }

        private void BeginUtterance(List<Frame> frames)
        {
            _utterance = frames;
            _utteranceId = Guid.NewGuid();
            _silenceRun = 0;
            for (var i = frames.Count - 1; i >= 0 && !frames[i].IsSpeech; i--)
                _silenceRun++;
            _sinceProvisional = 0;
        }

        private void EndUtterance()
        {
            _utterance = null;
            _silenceRun = 0;
            _sinceProvisional = 0;
        }

        private UtteranceUpdate CreateUpdate(List<Frame> frames, bool isFinal)
        {
            var samples = new float[frames.Count * _frameSize];
            double meanSquare = 0;
            for (var i = 0; i < frames.Count; i++)
            {
                frames[i].Samples.CopyTo(samples, i * _frameSize);
                meanSquare += frames[i].MeanSquare;
            }

            return new UtteranceUpdate(
                _utteranceId,
                _source,
                frames[0].Start,
                samples,
                isFinal,
                AudioLevel.ToDb(meanSquare / frames.Count),
                double.IsNaN(_ambientDb) ? AudioLevel.SilenceDb : _ambientDb);
        }

        private static bool HasSpeech(List<Frame> frames) => frames.Exists(f => f.IsSpeech);

        private static int FramesFor(int milliseconds, VadOptions options) => milliseconds / options.FrameMs;

        private readonly record struct Frame(float[] Samples, TimeSpan Start, double MeanSquare, bool IsSpeech);
    }
}
