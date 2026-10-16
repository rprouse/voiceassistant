namespace VoiceAssistant.Core.Transcription
{
    public static class ModelPath
    {
        /// <summary>
        /// Resolves a configured model path. Relative paths are tried against the current
        /// directory, then against the executable's folder and each folder above it, so
        /// "models/..." works from the repository root and from a double-clicked app in bin\.
        /// </summary>
        /// <returns>The first path that exists, or the current-directory path if none does.</returns>
        public static string Resolve(string path)
        {
            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            var fromCurrentDirectory = Path.GetFullPath(path);
            if (File.Exists(fromCurrentDirectory))
                return fromCurrentDirectory;

            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, path);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            return fromCurrentDirectory;
        }
    }
}
