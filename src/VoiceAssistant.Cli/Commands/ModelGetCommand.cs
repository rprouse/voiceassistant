using System.ComponentModel;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using VoiceAssistant.Core.Transcription;

namespace VoiceAssistant.Cli.Commands
{
    internal sealed class ModelGetCommand : AsyncCommand<ModelGetCommand.Settings>
    {
        private readonly WhisperOptions _options;

        public ModelGetCommand(IOptions<WhisperOptions> options)
        {
            _options = options.Value;
        }

        internal sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<NAME>")]
            [Description("Model to download, e.g. base.en or small.en.")]
            public string Name { get; set; } = string.Empty;

            [CommandOption("-f|--force")]
            [Description("Download again even if the file exists.")]
            public bool Force { get; set; }

            public override ValidationResult Validate() =>
                ModelCatalog.IsKnown(Name)
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"Unknown model '{Name}'. Known models: {string.Join(", ", ModelCatalog.Names)}.");
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            // Models go next to the configured one, so the default config finds base.en.
            var directory = Path.GetDirectoryName(Path.GetFullPath(_options.ModelPath))!;
            var path = Path.Combine(directory, ModelCatalog.FileName(settings.Name));

            if (File.Exists(path) && !settings.Force)
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]{path} already exists.[/] Use --force to download it again.");
                return 0;
            }

            await AnsiConsole.Status().StartAsync($"Downloading {settings.Name}...", async status =>
            {
                var progress = new Progress<long>(bytes => status.Status($"Downloading {settings.Name}: {bytes / 1_048_576.0:F0} MB"));
                await ModelCatalog.DownloadAsync(settings.Name, path, progress, cancellationToken);
            });

            AnsiConsole.MarkupLineInterpolated($"Saved [bold]{path}[/] ({new FileInfo(path).Length / 1_048_576.0:F0} MB)");
            if (!string.Equals(Path.GetFullPath(_options.ModelPath), path, StringComparison.OrdinalIgnoreCase))
                AnsiConsole.MarkupLineInterpolated($"To use it, set [bold]Whisper:ModelPath[/] to [bold]{Path.GetRelativePath(Environment.CurrentDirectory, path).Replace('\\', '/')}[/] in appsettings.json.");

            return 0;
        }
    }
}
