# Voice Assistant — Live Meeting Captions

A personal Windows desktop app that captures meeting audio, from both **system output** ("Them") and your **microphone** ("Me"), transcribes it **entirely on your machine** with [whisper.cpp](https://github.com/ggml-org/whisper.cpp) via [Whisper.net](https://github.com/sandrohanea/whisper.net), and shows live captions sentence by sentence.

> **Status: proof of concept, pre-scaffold.** The project is built in stages. See [progress](docs/voiceassistant-tasks.md). Commands below show the stage that introduces them and won't work until that stage is done.

## Why

The goal is fast, private, readable captions for meetings, with no audio leaving the machine. This is phase one of a larger tool: a later phase will feed the committed transcript to an LLM that suggests questions and critiques the meeting in real time. The architecture already leaves room for that consumer.

## Features (planned)

- Captures system audio (WASAPI loopback) and the mic at the same time, in shared mode, so it works alongside other capture apps.
- Local speech-to-text with Whisper (`base.en` by default, `small.en` with a one-line config change, optional GPU).
- Utterance-based segmentation with voice activity detection, so captions read as sentences instead of chopped 5-second chunks.
- **Provisional** captions (grey, updating in place) that become **committed** captions once the utterance ends.
- Rows labelled "Me" and "Them", in timestamp order.
- WPF captions window with always-on-top and adjustable opacity, so it can sit over a meeting.
- Each session's committed transcript saved as Markdown to `%LOCALAPPDATA%\VoiceAssistant\sessions\yyyy-MM-dd_HHmm.md`, kept complete after every caption.
- Survives device changes: if the default device changes or a headset is unplugged, capture moves to the new default device and captioning carries on.

**Not in scope for the PoC:** LLM integration, cloud services, speaker diarization beyond Me/Them, an installer, or auto-update.

## Requirements

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A Whisper ggml model (downloaded with the CLI or by hand, see below)
- Optional: an NVIDIA GPU (CUDA) or a Vulkan-capable GPU for faster transcription

## Getting started

```powershell
git clone <repo-url>
cd voiceassistant
dotnet build
dotnet test
```

### Get a model *(stage 3)*

```powershell
dotnet run --project src/VoiceAssistant.Cli -- model get base.en
```

Run it from the repository root: it saves the model to `models/ggml-base.en.bin` (git-ignored), and the relative `Whisper:ModelPath` in `appsettings.json` is resolved against the current directory. You can also download a `ggml-*.bin` file from the [whisper.cpp models on Hugging Face](https://huggingface.co/ggerganov/whisper.cpp) and put it in `models/`.

### CLI harness

The console app is for quick iteration on capture and transcription before the UI exists.

| Command | What it does | Stage |
|---|---|---|
| `record --seconds 30 [-o dir]` | Writes `them.wav` and `me.wav` in the device's native format | 1 |
| `record --converted` | Also writes 16 kHz mono `them-16k.wav` / `me-16k.wav` | 2 |
| `transcribe file.wav` | Prints timestamped segments for a WAV file | 3 |
| `live --naive [--seconds N]` | Live captions from fixed 5 s chunks (baseline); runs until Ctrl+C unless `--seconds` is given | 3 |
| `live [--seconds N]` | Live captions by utterance: grey provisional lines update in place, committed lines stay | 4 |

```powershell
dotnet run --project src/VoiceAssistant.Cli -- live
```

### Desktop app *(stage 5)*

```powershell
dotnet run --project src/VoiceAssistant.App
```

The model loads in the background when the window opens; press **Start** once the status line says *Ready*. Captions appear one row per utterance: grey italic text is provisional and is replaced in place when the utterance is committed. The list follows new captions unless you scroll up, and resumes when you scroll back to the bottom. 📌 keeps the window on top of your meeting, and the slider sets its opacity. Closing the window while captioning commits what was being said before exiting.

## Configuration

Settings live in `src/appsettings.json`, shared by the CLI and the desktop app. The desktop app's ⚙ **Settings** dialog (available while stopped) edits the model, language, GPU and voice-detection settings and saves them to `%LOCALAPPDATA%\VoiceAssistant\settings.json`, which overrides `appsettings.json` for both the app and the CLI and survives rebuilds.

```json
{
  "Whisper":  { "ModelPath": "models/ggml-base.en.bin", "Language": "en", "UseGpu": true, "GpuDevice": 1,
                "PromptCharacters": 200 },
  "Vad":      { "FrameMs": 30, "MinSpeechMs": 200, "PreRollMs": 300, "EndSilenceMs": 600,
                "MaxUtteranceSeconds": 15, "ProvisionalIntervalMs": 1000,
                "SpeechThresholdDb": 10, "SilenceThresholdDb": 5, "MinSpeechLevelDb": -50,
                "MinNoiseFloorDb": -75, "NoiseFloorRiseDbPerSecond": 2 },
  "Transcript": { "Save": true, "Directory": "" },
  "Capture":  { "EnableMic": true, "EnableLoopback": true, "DeviceRole": "Communications",
                "QueueCapacity": 500, "GapToleranceMs": 50,
                "RecoverFromDeviceChanges": true, "ReconnectIntervalMs": 2000 },
  "Hallucinations": { "Blocklist": [ "Thank you.", "Thanks for watching!", "Thank you for watching." ],
                      "NearNoiseFloorDb": 6 }
}
```

Tuning tips:

- **GPU:** `UseGpu` transcribes through Whisper.net's Vulkan runtime (any recent GPU driver, no CUDA install needed), falling back to the CPU if Vulkan can't load. `GpuDevice` picks the Vulkan device: laptops often list the integrated GPU as device 0, so check the log for the `ggml_vulkan: N = ...` lines when the model loads. The first GPU run after a driver update takes several extra seconds while shaders compile.
- **Captions lag behind speech:** check the real-time factor in the log or status line. If it's above ~0.5, raise `ProvisionalIntervalMs`, set `UseGpu`, or use a smaller model.
- **Sentences split too early:** raise `EndSilenceMs`.
- **First words clipped:** raise `PreRollMs`.
- **Quiet speakers missed, or background noise captioned:** lower or raise `SpeechThresholdDb` (how far above the room's noise floor speech must be). `MinSpeechLevelDb` is an absolute minimum, whatever the room.
- **Phantom "Thank you." lines:** add the phrase to `Hallucinations.Blocklist`.
- **Wrong device captured:** captures default to the Windows *communications* device, which meeting apps use. To capture the general default device (e.g. for a video outside a call), set `Capture.DeviceRole` to `Console`.

## How it works

```txt
[Loopback capture] ─┐
                    ├─► Channel<AudioChunk> ─► [Convert to 16 kHz mono float]
[Mic capture] ──────┘        (per source)                  │
                                                           ▼
                                                 [VAD + utterance builder]
                                                           │
                                                           ▼
                                              [Shared Whisper transcriber]
                                                           │
                                                           ▼
                                  Channel<TranscriptEvent> ─► UI / session file / (future) LLM
```

- Each source runs its own capture → convert → VAD pipeline. Both share **one** transcriber, because Whisper models are memory-heavy.
- An energy-based VAD with hysteresis finds speech. An utterance builder adds a little pre-roll and ends an utterance after a pause, or at a quiet point once it reaches the maximum length.
- While someone is talking, the audio so far is re-transcribed about once a second (**provisional**). When they stop, the full utterance is transcribed once more and **committed**. The last part of the committed text is passed back as a prompt so the next utterance continues naturally.
- When both sources have finished utterances waiting, the one that ended first is transcribed first, and captions are shown in start-time order. A long "Them" sentence that began before your reply appears above it, even though it finished later.
- When the system falls behind, raw audio is dropped (and counted) rather than letting lag build up. Committed transcriptions are never dropped.

See the [full spec](docs/voiceassistant-spec.md) for details.

## Project layout

```txt
src/VoiceAssistant.Core/         Capture, conversion, VAD, transcription, transcript model
src/VoiceAssistant.Cli/          Console harness (Spectre.Console)
src/VoiceAssistant.App/          WPF captions window
tests/VoiceAssistant.Core.Tests/ NUnit + Shouldly tests
models/                          Whisper models (git-ignored)
samples/                         Small WAV test fixtures
docs/                            Spec and stage checklist
```

## Roadmap

| Stage | Goal |
|---|---|
| 0 | Scaffold solution, projects, build settings |
| 1 | Capture loopback and mic to WAV |
| 2 | Convert audio to 16 kHz mono float |
| 3 | Naive fixed-chunk transcription |
| 4 | VAD and utterance segmentation (caption quality) |
| 5 | WPF captions UI |
| 6 | Me and Them together, interleaved |
| 7 | Hardening: GPU, saved transcripts, device recovery, settings |

Progress is tracked in [docs/voiceassistant-tasks.md](docs/voiceassistant-tasks.md).

## Privacy

All audio and transcription stay on your machine. Nothing is sent to a network service. Recording other people may require their consent where you live, so check before you use this in meetings.

## Contributing with AI agents

Coding agents should read [AGENTS.md](AGENTS.md) first. It covers the stage-by-stage workflow, architecture invariants, and conventions.

## License

[MIT](LICENSE) © 2026 Rob Prouse
