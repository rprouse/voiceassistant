# AGENTS.md

Instructions for AI coding agents (Claude Code, Codex, Copilot, etc.) working in this repository.

## Source of truth

- **Spec:** [docs/voiceassistant-spec.md](docs/voiceassistant-spec.md) — requirements, architecture, stages, acceptance criteria. Read it before any work. If this file and the spec disagree, the spec wins; point out the conflict.
- **Progress:** [docs/voiceassistant-tasks.md](docs/voiceassistant-tasks.md) — the stage checklist. Tick a stage off **only** once its acceptance criteria are met, and do it in the same change that completes the stage.

## What this project is

A personal Windows desktop proof of concept that captures meeting audio, both system output ("Them") and microphone ("Me"), transcribes it **locally** with whisper.cpp via Whisper.net, and shows live captions sentence by sentence. A later phase will add an LLM consumer of the transcript. Do **not** build that phase, but keep the seam described in spec §8 clean.

## Working rules

1. **One stage at a time, in order** (spec §7). When a stage is done, **stop** and post a stage summary (template below). Do not start the next stage until the user says so.
2. **Every stage must leave the repo green:** `dotnet build` and `dotnet test` both succeed from the repo root, with zero warnings (warnings are errors).
3. **No new dependencies without asking.** The allowed packages are listed in spec §4. Anything else, including test helpers, needs the user's approval first.
4. **Use the latest stable NuGet versions.** If a package's current API differs from the spec's sketches, follow the package and record the difference in the stage summary. Don't invent APIs. Check the real ones (context7 docs, package source, or IntelliSense/metadata).
5. **.NET 10 is required.** Target `net10.0-windows`. If the .NET 10 SDK is missing, stop and tell the user. Never fall back to an older TFM.
6. **Keep the WPF project thin.** Put logic in small, testable classes in `VoiceAssistant.Core`.
7. **Don't build what the spec defers:** no LLM integration, no cloud calls, no LocalAgreement sliding window (stage 4 alternative), no Silero VAD, no installer. Each needs an explicit request.

### Stage summary template

```md
## Stage N — <name>: complete

**Built:** <bullets: projects/classes/commands added>
**Deviations from spec:** <API differences, decisions made, or "none">
**Verify acceptance criteria:**
1. <exact command or manual step> → <expected result>
**Known issues / notes for next stage:** <bullets>
```

## Commands

```powershell
dotnet build                                   # whole solution
dotnet test                                    # all tests
dotnet test --filter "Category!=Integration"   # skip tests needing devices/models
dotnet run --project src/VoiceAssistant.Cli -- <command> [args]
dotnet run --project src/VoiceAssistant.App    # WPF app (stage 5+)
```

The CLI commands the spec defines, each available from the stage shown:

| Command | Stage |
|---|---|
| `record --seconds 30` → `them.wav`, `me.wav` (native format) | 1 |
| `record --converted` → also `them-16k.wav`, `me-16k.wav` | 2 |
| `model get base.en` | 3 |
| `transcribe file.wav` | 3 |
| `live --naive [--seconds N]` (fixed 5 s chunks) | 3 |
| `live` (VAD + utterances, provisional/committed) | 4 |

## Repository layout

```txt
VoiceAssistant.slnx             # XML solution format (.NET 10 default)
Directory.Build.props           # Nullable=enable, TreatWarningsAsErrors=true, LangVersion=latest
src/
  VoiceAssistant.Core/          # capture, conversion, VAD, transcription, segment model. NO WPF refs.
  VoiceAssistant.Cli/           # Spectre.Console harness for stages 1–4
  VoiceAssistant.App/           # WPF + generic host (stage 5+)
tests/
  VoiceAssistant.Core.Tests/    # NUnit + Shouldly
models/                         # ggml model files — git-ignored, never commit
samples/                        # small WAV fixtures for tests (< ~1 MB each)
docs/                           # spec and task checklist
```

**Solution file:** `VoiceAssistant.slnx`, the XML solution format that `dotnet new sln` produces by default on the .NET 10 SDK. Don't create a legacy `.sln`.

## Architecture invariants

These come from spec §6 and §8. Breaking one is a bug even if the tests pass.

- **Pipeline per source:** capture → `Channel<AudioChunk>` → converter (16 kHz mono float) → VAD and utterance builder → shared transcriber → `Channel<TranscriptEvent>` → sinks.
- **One `ITranscriber` instance**, shared by both sources through a single queue, because Whisper models are memory-heavy. When both sources have utterances ready, process them in order of utterance end time.
- **One shared `Stopwatch`** per session. Every timestamp is relative to session start.
- **NAudio `DataAvailable` handlers** only copy the buffer and `TryWrite` it to a channel. Never block, convert, log heavily, or allocate large buffers in them.
- **Channel policies:**
  - capture → converter: bounded, `BoundedChannelFullMode.DropOldest`, and **log a warning and count every drop** (the UI shows the dropped-audio count).
  - utterance → transcriber: bounded, `Wait`. Never drop committed passes. Skip a provisional pass if the transcriber is busy.
- **Loopback is silent when nothing plays:** `WasapiLoopbackCapture` raises no events during silence. Pad with silence based on elapsed time so the Me and Them timelines stay aligned.
- **WASAPI shared mode only.** Never use exclusive mode: other capture apps (e.g. Granola) must keep working.
- **Core has no WPF/UI references.** Dispatcher marshalling happens only inside the WPF sink (the view model's `ITranscriptSink` implementation).
- **`TranscriptEvent` and its subtypes stay plain, serialisable records**, so they could later go over a local pipe to another process. Don't put UI types, delegates, or `Span`s in them.
- **Several consumers:** UI, session file, and the future LLM are all `ITranscriptSink`s. Never assume the UI is the only consumer.
- **VAD sits behind `IVoiceActivityDetector`** so it can be swapped later. `EnergyVad` is the first implementation.
- **All tunables live in `appsettings.json`**, bound to options classes (`IOptions<T>`) through `Microsoft.Extensions.Hosting`. No magic numbers for thresholds, intervals, or paths.

## Coding conventions

- Follow `.editorconfig` (NUnit coding standards). Naming rules are enforced only in the IDE; `dotnet build` does **not** catch them, so follow them yourself:
  - PascalCase for types, methods, properties, events, and public/protected fields.
  - camelCase for parameters and locals.
  - `_camelCase` for private fields.
- `System` usings first. `var` when the type is apparent. CRLF line endings for `.cs` files.
- Nullable reference types are on. Don't silence warnings with `!` or `#pragma` unless there's a comment explaining why.
- Prefer `record`s for data, `sealed` classes by default, `IAsyncDisposable` for anything that owns devices or native resources.
- Pass `CancellationToken` through every async path. Async-only APIs: no `.Result` or `.Wait()`.
- Logging: inject `ILogger<T>`. Log pipeline timings (e.g. real-time factor, queue depth) at `Debug`. Log dropped audio and device changes at `Warning`. Serilog writes a rolling file.
- Keep comments sparse. Explain *why* (audio quirks, threading choices), not *what*.

## Testing

- **NUnit** + **Shouldly** assertions (`result.ShouldBe(...)`), not `Assert.That`. Keep `NUnit.Analyzers` warnings clean.
- Unit tests must **not** need audio hardware, network, or model files. Generate synthetic audio (sine waves, silence, noise bursts) in code wherever possible.
- Tests that need a real device or a downloaded model go in `[Category("Integration")]`. When the prerequisite is missing, skip with `Assert.Ignore` and a clear message rather than failing.
- WAV fixtures go in `samples/`, each under ~1 MB. `*.wav` is git-ignored everywhere else.
- Stage 2 and stage 4 list specific required tests in the spec. Implement those exactly.
- Whisper integration tests find the repo root via `TestPaths.RepositoryRoot` and need `models/ggml-base.en.bin`. Speech fixtures in `samples/` are generated with Windows text-to-speech (`System.Speech`), not real recordings.
- `Whisper.net.Runtime` ships its native DLLs through `build/` targets, which don't flow through project references. Reference it from every *executable* project (CLI, App, tests), not from Core.

## Configuration

`appsettings.json` sections: `Whisper` (ModelPath, Language, UseGpu, GpuDevice), `Vad` (FrameMs, MinSpeechMs, PreRollMs, EndSilenceMs, MaxUtteranceSeconds, ProvisionalIntervalMs), `Capture` (EnableMic, EnableLoopback, DeviceRole, QueueCapacity, GapToleranceMs), `Hallucinations` (Blocklist). See spec §9 for defaults. Logs go to `%LOCALAPPDATA%\VoiceAssistant\logs\`. Switching `base.en` → `small.en` must stay a one-line config change.

## Git

- Never commit model files (`models/`), recorded WAVs outside `samples/`, or `bin/` / `obj/`.
- Commits are signed. Never bypass signing (`--no-gpg-sign`, `commit.gpgsign=false`). If signing fails, stop and ask the user.
- Commit only when the user asks. One logical change per commit. A stage-completion commit includes the checklist update.

## Known risks to keep in mind

- **CPU headroom:** provisional passes multiply transcription work. If the real-time factor on `base.en` is above ~0.5 on CPU, tune `ProvisionalIntervalMs` or enable GPU before redesigning anything.
- **Whisper hallucinates on silence or noise:** VAD and the blocklist reduce this but don't remove it. Don't claim it's solved.
- **Echo** (your own voice coming back through the other side's audio) is out of scope. Note it if you see it.
