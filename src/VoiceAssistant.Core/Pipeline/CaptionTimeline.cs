namespace VoiceAssistant.Core.Pipeline
{
    /// <summary>A change to apply to a displayed caption list.</summary>
    public abstract record CaptionChange(int Index);

    public sealed record CaptionInserted(int Index, TranscriptSegment Segment) : CaptionChange(Index);

    public sealed record CaptionUpdated(int Index, TranscriptSegment Segment) : CaptionChange(Index);

    public sealed record CaptionRemoved(int Index) : CaptionChange(Index);

    /// <summary>
    /// Keeps captions from every source in start-time order, one per utterance. Sources
    /// finish transcribing out of order (a long "Them" utterance that started first can
    /// commit after a short "Me" reply), so new captions are inserted by start time rather
    /// than appended, and later snapshots of an utterance update its row in place.
    /// </summary>
    public sealed class CaptionTimeline
    {
        private readonly List<TranscriptSegment> _captions = [];

        public IReadOnlyList<TranscriptSegment> Captions => _captions;

        /// <returns>How the displayed list should change, or null if it shouldn't.</returns>
        public CaptionChange? Apply(TranscriptEvent transcriptEvent) => transcriptEvent switch
        {
            SegmentProvisional { Segment: var segment } => AddOrUpdate(segment),
            SegmentCommitted { Segment: var segment } => AddOrUpdate(segment),
            SegmentRetracted { Id: var id } => Remove(id),
            _ => null
        };

        public void Clear() => _captions.Clear();

        private CaptionChange AddOrUpdate(TranscriptSegment segment)
        {
            var index = IndexOf(segment.Id);
            if (index >= 0)
            {
                _captions[index] = segment;
                return new CaptionUpdated(index, segment);
            }

            // Nearly always the newest, so search from the end.
            index = _captions.Count;
            while (index > 0 && _captions[index - 1].Start > segment.Start)
                index--;

            _captions.Insert(index, segment);
            return new CaptionInserted(index, segment);
        }

        private CaptionRemoved? Remove(Guid id)
        {
            var index = IndexOf(id);
            if (index < 0)
                return null;

            _captions.RemoveAt(index);
            return new CaptionRemoved(index);
        }

        private int IndexOf(Guid id)
        {
            // Recent captions are the ones that change, so search from the end.
            for (var i = _captions.Count - 1; i >= 0; i--)
            {
                if (_captions[i].Id == id)
                    return i;
            }

            return -1;
        }
    }
}
