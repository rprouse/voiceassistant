namespace VoiceAssistant.Core.Tests
{
    internal static class TestPaths
    {
        /// <summary>The folder containing VoiceAssistant.slnx, found by walking up from the test binaries.</summary>
        public static string RepositoryRoot { get; } = FindRepositoryRoot();

        private static string FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "VoiceAssistant.slnx")))
                    return directory.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root (VoiceAssistant.slnx).");
        }
    }
}
