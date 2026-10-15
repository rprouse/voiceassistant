using Whisper.net.Ggml;

namespace VoiceAssistant.Core.Transcription
{
    /// <summary>The ggml Whisper models that can be downloaded, by their whisper.cpp names.</summary>
    public static class ModelCatalog
    {
        private static readonly Dictionary<string, GgmlType> Models = new(StringComparer.OrdinalIgnoreCase)
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

        public static IReadOnlyCollection<string> Names => Models.Keys;

        public static bool IsKnown(string name) => Models.ContainsKey(name);

        /// <summary>The conventional whisper.cpp file name, e.g. <c>ggml-base.en.bin</c>.</summary>
        public static string FileName(string name) => $"ggml-{name.ToLowerInvariant()}.bin";

        /// <summary>
        /// Downloads a model to <paramref name="path"/>. Writes to a temporary file first,
        /// so an interrupted download never leaves a truncated model behind.
        /// </summary>
        public static async Task DownloadAsync(string name, string path, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            if (!Models.TryGetValue(name, out var type))
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
