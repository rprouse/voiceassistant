using System.Text;
using System.Text.RegularExpressions;

namespace VoiceAssistant.Core.Transcription
{
    /// <summary>
    /// Recognises text Whisper produces from silence or noise rather than speech.
    /// </summary>
    public sealed partial class HallucinationFilter
    {
        private readonly HashSet<string> _blocklist;
        private readonly double _nearNoiseFloorDb;

        public HallucinationFilter(HallucinationOptions options)
        {
            _blocklist = options.Blocklist.Select(Normalize).Where(p => p.Length > 0).ToHashSet();
            _nearNoiseFloorDb = options.NearNoiseFloorDb;
        }

        /// <summary>Removes Whisper's non-speech annotations such as [BLANK_AUDIO] or (music).</summary>
        public static string Clean(string text) => Whitespace().Replace(Annotation().Replace(text, " "), " ").Trim();

        /// <summary>True when nothing is left but punctuation once annotations are removed.</summary>
        public static bool IsNonSpeech(string text) => !Clean(text).Any(char.IsLetterOrDigit);

        public bool ShouldDrop(string text, double meanLevelDb, double ambientLevelDb) =>
            IsNonSpeech(text)
            || (meanLevelDb - ambientLevelDb < _nearNoiseFloorDb && _blocklist.Contains(Normalize(Clean(text))));

        private static string Normalize(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
                else if (char.IsWhiteSpace(c) && builder.Length > 0 && builder[^1] != ' ')
                    builder.Append(' ');
            }

            return builder.ToString().TrimEnd();
        }

        [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*")]
        private static partial Regex Annotation();

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();
    }
}
