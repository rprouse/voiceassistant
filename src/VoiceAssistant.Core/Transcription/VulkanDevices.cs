using System.Globalization;
using System.Text.RegularExpressions;

namespace VoiceAssistant.Core.Transcription
{
    /// <param name="Index">whisper.cpp's number for the device, as used by <see cref="WhisperOptions.GpuDevice"/>.</param>
    /// <param name="IsIntegrated">Shares system memory (e.g. Intel graphics) rather than having its own.</param>
    public sealed record VulkanDevice(int Index, string Name, bool IsIntegrated)
    {
        public string DisplayName => $"{Index} · {Name} ({(IsIntegrated ? "integrated" : "dedicated")})";
    }

    /// <summary>
    /// The GPUs whisper.cpp's Vulkan backend found, read from its own log messages so the
    /// numbering matches exactly. Whisper.net has no API to list them, and Windows' adapter
    /// order isn't guaranteed to match. Empty until a model has been loaded with the GPU on.
    /// </summary>
    public static partial class VulkanDevices
    {
        private static readonly Lock Gate = new();
        private static readonly SortedDictionary<int, VulkanDevice> Found = [];

        public static IReadOnlyList<VulkanDevice> All
        {
            get
            {
                lock (Gate)
                    return [.. Found.Values];
            }
        }

        /// <summary>The device Auto chooses: the first dedicated GPU, else the first one.</summary>
        public static VulkanDevice? Preferred
        {
            get
            {
                var all = All;
                return all.FirstOrDefault(d => !d.IsIntegrated) ?? all.FirstOrDefault();
            }
        }

        public static VulkanDevice? Get(int index)
        {
            lock (Gate)
                return Found.GetValueOrDefault(index);
        }

        /// <summary>Records the device if <paramref name="logMessage"/> is a whisper.cpp device line.</summary>
        public static void Observe(string? logMessage)
        {
            if (logMessage is not null && TryParse(logMessage) is { } device)
            {
                lock (Gate)
                    Found[device.Index] = device;
            }
        }

        /// <summary>
        /// Parses e.g. "ggml_vulkan: 1 = NVIDIA GeForce RTX 3050 Ti Laptop GPU (NVIDIA) | uma: 0 | fp16: 1 | ...".
        /// </summary>
        public static VulkanDevice? TryParse(string logMessage)
        {
            var match = DeviceLine().Match(logMessage);
            if (!match.Success)
                return null;

            return new VulkanDevice(
                int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture),
                match.Groups["name"].Value.Trim(),
                match.Groups["uma"].Value == "1");
        }

        // The name can itself contain parentheses ("Intel(R) UHD Graphics"), so the vendor is
        // the last parenthesised group before the first '|'.
        [GeneratedRegex(@"ggml_vulkan:\s*(?<index>\d+)\s*=\s*(?<name>.+?)\s*\([^()]*\)\s*\|\s*uma:\s*(?<uma>\d)")]
        private static partial Regex DeviceLine();
    }
}
