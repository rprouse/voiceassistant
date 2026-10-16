using Whisper.net.Ggml;

namespace VoiceAssistant.Core.Transcription
{
    /// <summary>A downloadable model and roughly how big it is.</summary>
    public sealed record ModelInfo(string Name, int ApproximateSizeMb, string Note);

    /// <summary>The ggml Whisper models that can be downloaded, by their whisper.cpp names.</summary>
    public static class ModelCatalog
    {
        private static readonly Dictionary<string, GgmlType> Types = new(StringComparer.OrdinalIgnoreCase)
        {
            ["tiny"] = GgmlType.Tiny,
            ["tiny.en"] = GgmlType.TinyEn,
            ["base"] = GgmlType.Base,
            ["base.en"] = GgmlType.BaseEn,
            ["small"] = GgmlType.Small,
            ["small.en"] = GgmlType.SmallEn,
            ["medium"] = GgmlType.Medium,
            ["medium.en"] = GgmlType.MediumEn,
            ["large-v1"] = GgmlType.LargeV1,
            ["large-v2"] = GgmlType.LargeV2,
            ["large-v3"] = GgmlType.LargeV3,
            ["large-v3-turbo"] = GgmlType.LargeV3Turbo
        };

        /// <summary>Smallest to largest. Sizes are the published ggml file sizes, rounded.</summary>
        public static IReadOnlyList<ModelInfo> Models { get; } =
        [
            new("tiny.en", 75, "English only; fastest, least accurate"),
            new("tiny", 75, "Multilingual"),
            new("base.en", 142, "English only; the default"),
            new("base", 142, "Multilingual"),
            new("small.en", 466, "English only; noticeably more accurate"),
            new("small", 466, "Multilingual"),
            new("medium.en", 1_500, "English only; needs a GPU for live captions"),
            new("medium", 1_500, "Multilingual; needs a GPU for live captions"),
            new("large-v3-turbo", 1_600, "Multilingual; near large accuracy at medium speed"),
            new("large-v3", 3_100, "Multilingual; most accurate, slowest"),
            new("large-v2", 3_100, "Multilingual; previous large model"),
            new("large-v1", 3_100, "Multilingual; original large model")
        ];

        public static IReadOnlyCollection<string> Names => Types.Keys;

        public static bool IsKnown(string name) => Types.ContainsKey(name);

        /// <summary>The conventional whisper.cpp file name, e.g. <c>ggml-base.en.bin</c>.</summary>
        public static string FileName(string name) => $"ggml-{name.ToLowerInvariant()}.bin";

        /// <summary>
        /// Downloads a model to <paramref name="path"/>. Writes to a temporary file first,
        /// so an interrupted download never leaves a truncated model behind.
        /// </summary>
        public static async Task DownloadAsync(string name, string path, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            if (!Types.TryGetValue(name, out var type))
                throw new ArgumentException($"Unknown model '{name}'. Known models: {string.Join(", ", Names)}.", nameof(name));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporaryPath = path + ".download";

            try
            {
                await using (var model = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(type, QuantizationType.NoQuantization, cancellationToken).ConfigureAwait(false))
                await using (var file = File.Create(temporaryPath))
                {
                    var buffer = new byte[81_920];
                    long total = 0;
                    int read;
                    while ((read = await model.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        total += read;
                        progress?.Report(total);
                    }
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
