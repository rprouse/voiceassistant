using System.Text.Json.Nodes;
using VoiceAssistant.Core.Transcription;
using VoiceAssistant.Core.Vad;

namespace VoiceAssistant.Core.Tests
{
    public class UserSettingsFileTests
    {
        private string _path = null!;

        [SetUp]
        public void ChoosePath() => _path = Path.Combine(Directory.CreateTempSubdirectory("va-settings-").FullName, "settings.json");

        [TearDown]
        public void Clean() => Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);

        [Test]
        public void SavesTheEditedWhisperAndVadSettings()
        {
            var whisper = new WhisperOptions { ModelPath = "models/ggml-small.en.bin", Language = "en", UseGpu = true, GpuDevice = 1 };
            var vad = new VadOptions { EndSilenceMs = 800, SpeechThresholdDb = 8.5 };

            UserSettingsFile.Save(_path, whisper, vad);

            var json = JsonNode.Parse(File.ReadAllText(_path))!;
            json["Whisper"]!["ModelPath"]!.GetValue<string>().ShouldBe("models/ggml-small.en.bin");
            json["Whisper"]!["UseGpu"]!.GetValue<bool>().ShouldBeTrue();
            json["Whisper"]!["GpuDevice"]!.GetValue<int>().ShouldBe(1);
            json["Vad"]!["EndSilenceMs"]!.GetValue<int>().ShouldBe(800);
            json["Vad"]!["SpeechThresholdDb"]!.GetValue<double>().ShouldBe(8.5);
            File.Exists(_path + ".tmp").ShouldBeFalse();
        }

        [Test]
        public void KeepsOtherSettingsAlreadyInTheFile()
        {
            File.WriteAllText(_path, """
                {
                  "Capture": { "DeviceRole": "Console" },
                  "Whisper": { "PromptCharacters": 300, "Language": "fr" }
                }
                """);

            UserSettingsFile.Save(_path, new WhisperOptions { Language = "en" }, new VadOptions());

            var json = JsonNode.Parse(File.ReadAllText(_path))!;
            json["Capture"]!["DeviceRole"]!.GetValue<string>().ShouldBe("Console");
            json["Whisper"]!["PromptCharacters"]!.GetValue<int>().ShouldBe(300);
            json["Whisper"]!["Language"]!.GetValue<string>().ShouldBe("en");
        }
    }
}
