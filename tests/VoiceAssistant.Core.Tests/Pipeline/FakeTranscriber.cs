using System.Runtime.CompilerServices;
using VoiceAssistant.Core.Transcription;

namespace VoiceAssistant.Core.Tests.Pipeline
{
    /// <summary>A transcriber whose answers and timing the test controls.</summary>
    internal sealed class FakeTranscriber : ITranscriber
    {
        private TaskCompletionSource? _gate;

        public string Description => "Fake";

        public List<(AudioSource Source, TimeSpan Start, int Samples, string? Prompt)> Calls { get; } = [];

        /// <summary>What to "hear" for a call, by call number (0-based); defaults to "utterance N".</summary>
        public Func<int, string> Respond { get; set; } = call => $"utterance {call}";

        /// <summary>Signalled when a call starts, so tests know the transcriber is busy.</summary>
        public SemaphoreSlim Started { get; } = new(0);

        /// <summary>Makes calls wait until <see cref="Release"/>.</summary>
        public void Block() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate?.TrySetResult();

        public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
            AudioSource source,
            TimeSpan start,
            float[] samples,
            string? prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var call = Calls.Count;
            Calls.Add((source, start, samples.Length, prompt));
            Started.Release();

            if (_gate is not null)
                await _gate.Task.WaitAsync(cancellationToken);

            var text = Respond(call);
            if (text == "throw")
                throw new InvalidOperationException("Transcriber failed.");

            yield return new TranscriptSegment(Guid.NewGuid(), source, start, start + TimeSpan.FromSeconds(1), text, IsFinal: true);
        }
    }
}
