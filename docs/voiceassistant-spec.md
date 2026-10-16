# Voice Assistant - Live Meeting Captioning (Proof of Concept)

## 1. Purpose

A personal Windows desktop app that captures meeting audio (system output and microphone), transcribes it locally with whisper.cpp, and displays captions sentence by sentence as they are produced.

This is phase one of a larger tool. A later phase will feed the transcript to an LLM that suggests questions and critiques the meeting in real time. Nothing in this spec implements that, but the design must leave a clean seam for it (see §8).

### Non-goals for this PoC

- No LLM integration.
- No cloud services. All audio stays on the machine.
- No installer, auto-update, multi-user support, or polish beyond what is needed to judge caption quality.
- No speaker diarization beyond "Me" (mic) vs "Them" (system audio).

## 2. Instructions for Claude Code

- Build **one stage at a time**, in order. After each stage, stop, summarise what was built, and list how to verify the acceptance criteria. Do not start the next stage until told to.
- Each stage must leave the solution building and all tests passing.
- Prefer small, testable classes in `VoiceAssistant.Core`. Keep the WPF project thin.
- Use the latest stable NuGet versions at time of build. Where an API in this spec doesn't match the installed package version, follow the package's current API and note the difference in the stage summary rather than guessing.
- Do not add libraries beyond those listed in §4 without asking.

## 3. Platform

- Windows 10/11, x64.
- .NET 10 (LTS), target `net10.0-windows`. If .NET 10 is unavailable on the machine, do not fall back, say so and wait for the user to install it.
- WPF for the UI.

## 4. Libraries

| Purpose | Package | Notes |
|---|---|---|
| Audio capture | `NAudio` | `WasapiLoopbackCapture` for system output, `WasapiCapture` for the mic. Shared mode only, never exclusive. |
| Resampling / format conversion | `NAudio` | `BufferedWaveProvider` → `WdlResamplingSampleProvider` (fully managed, no Media Foundation dependency). |
| Speech-to-text | `Whisper.net` + `Whisper.net.Runtime` | .NET bindings over whisper.cpp. CPU runtime first. |
| GPU acceleration (stage 7, optional) | `Whisper.net.Runtime.Cuda` or `Whisper.net.Runtime.Vulkan` | Whisper.net picks the best available runtime at load. |
| Voice activity detection | Built-in energy VAD first (stage 4). Optional upgrade: Silero VAD via `Microsoft.ML.OnnxRuntime`. | Keep VAD behind an `IVoiceActivityDetector` interface so it can be swapped. |
| MVVM | `CommunityToolkit.Mvvm` | `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`. |
| Hosting / DI / config | `Microsoft.Extensions.Hosting` | Generic host in the WPF app; `appsettings.json` bound to options classes. |
| Logging | `Microsoft.Extensions.Logging` + `Serilog.Extensions.Hosting` + `Serilog.Sinks.File` | Rolling file log; pipeline timings logged at Debug. |
| Pipelines between stages | `System.Threading.Channels` (BCL) | Bounded channels with explicit back-pressure policy. |
| CLI | `Spectre.Console` | Used for console input and output. |
| Tests | `NUnit`, `NUnit.Analyzers`, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk`, `Shouldly` | Use Shouldly assertions. |

## 5. Solution layout

```txt
VoiceAssistant.slnx
src/
  VoiceAssistant.Core/        class library: capture, conversion, VAD, transcription, segment model
  VoiceAssistant.Cli/         console harness used in stages 1–4 for fast iteration
  VoiceAssistant.App/         WPF app (stage 5+)
tests/
  VoiceAssistant.Core.Tests/  NUnit tests
models/                  ggml model files (git-ignored)
samples/                 short WAV fixtures for tests (keep under ~1 MB each)
```

## 6. Architecture

```txt
[Loopback capture] ─┐
                    ├─► Channel<AudioChunk> ─► [Converter: 16 kHz mono float]
[Mic capture] ──────┘        (per source)              │
                                                       ▼
                                             [VAD + utterance builder]
                                                       │
                                                       ▼
                                             [Transcriber (Whisper.net)]
                                                       │
                                                       ▼
                                    Channel<TranscriptEvent> ─► UI / file / (future) LLM
```

Each source (Me, Them) runs its own capture → convert → VAD pipeline. Both share a single transcriber instance (whisper models are memory-heavy), fed through one queue.

### Key types (Core)

```csharp
enum AudioSource { Me, Them }

record AudioChunk(AudioSource Source, float[] Samples, TimeSpan Timestamp); // already 16 kHz mono after conversion

record TranscriptSegment(
    Guid Id,
    AudioSource Source,
    TimeSpan Start,
    TimeSpan End,
    string Text,
    bool IsFinal);

abstract record TranscriptEvent;
record SegmentProvisional(TranscriptSegment Segment) : TranscriptEvent;  // may be revised
record SegmentCommitted(TranscriptSegment Segment) : TranscriptEvent;    // never changes again

interface IAudioSourceCapture : IAsyncDisposable { /* Start, Stop, IAsyncEnumerable<AudioChunk> */ }
interface IVoiceActivityDetector { bool IsSpeech(ReadOnlySpan<float> frame); }
interface ITranscriber { IAsyncEnumerable<TranscriptSegment> TranscribeAsync(float[] samples, string? prompt, CancellationToken ct); }
interface ITranscriptSink { ValueTask OnEventAsync(TranscriptEvent e, CancellationToken ct); }
```

Timestamps are relative to session start, from a single `Stopwatch` shared by both sources.

### Threading rules

- NAudio `DataAvailable` handlers do nothing but copy the buffer and `TryWrite` to a channel. Never block, never convert there.
- Capture → converter channels: bounded, `DropOldest`, with a logged warning when dropping. Losing audio is better than unbounded lag.
- Utterance → transcriber channel: bounded, `Wait`. Committed passes must not be dropped. Provisional passes may be skipped if the transcriber is behind (see §7, stage 4).
- UI updates marshal through the dispatcher only at the sink. Core has no WPF references.

## 7. Stages

The progress on the following tasks are tracked in the checklist in [voiceassistant-tasks.md](voiceassistant-tasks.md). Update the checklist as each task is completed so we always know where we are in the project.

### Stage 0 — Scaffold

Create the solution, projects, references, add to `.gitignore` if needed (include `models/`, `bin/`, `obj/`, `*.wav` outside `samples/`), `Directory.Build.props` (nullable enabled, warnings as errors, latest C# version), and an empty passing test.

**Accept when:** `dotnet build` and `dotnet test` succeed from the repo root.

### Stage 1 — Capture to WAV

In Core, implement `LoopbackCapture` and `MicCapture` over NAudio. In the CLI, `VoiceAssistant record --seconds 30` writes `them.wav` and `me.wav` in the native device format.

Handle: device format varies (commonly 32-bit IEEE float, 48 kHz, stereo); loopback raises **no** `DataAvailable` events while nothing is playing, so the pipeline must tolerate gaps and pad with silence based on elapsed time; the default device can change mid-session (log it; recovery can wait until stage 7).

**Accept when:** playing a YouTube video while talking produces two WAVs that play back correctly, with matching durations within ~100 ms, while another capture app (e.g. Granola) runs simultaneously.

### Stage 2 — Format conversion

Implement `AudioConverter`: native format → 16 kHz, mono, 32-bit float (the format Whisper.net consumes). Downmix by averaging channels. Resample with `WdlResamplingSampleProvider`.

CLI: `VoiceAssistant record --converted` additionally writes `them-16k.wav` / `me-16k.wav`.

Tests: converting a generated 48 kHz stereo sine produces correct length (±1%) and frequency; silence stays silence; odd buffer sizes across chunk boundaries don't click or drop samples.

**Accept when:** tests pass and the 16 kHz files sound clean.

### Stage 3 — Naive transcription

Implement `WhisperTranscriber` over Whisper.net. Model path and language come from config. Add a CLI command to download a model (`VoiceAssistant model get base.en`) using Whisper.net's ggml downloader if available in the installed version; otherwise document the manual download.

CLI: `VoiceAssistant transcribe file.wav` prints segments with timestamps. `VoiceAssistant live --naive` transcribes fixed, non-overlapping 5-second chunks of live audio and prints them.

Default model: `base.en`. Make `small.en` a one-line config change.

**Accept when:** a file transcribes sensibly, and `live --naive` produces readable (if word-chopped) live output. Log per-chunk transcription time vs audio duration (real-time factor).

### Stage 4 — VAD and utterance segmentation

This is the stage that determines caption quality. Replace fixed chunks with utterance-based segmentation:

1. Run VAD on 30 ms frames. Implement `EnergyVad` (RMS threshold with hysteresis and a short adaptive noise floor).
2. **Utterance builder** state machine per source:
   - Idle → Speaking when speech is detected for ≥ `MinSpeechMs` (default 200). Include `PreRollMs` (default 300) of audio before the trigger so first syllables aren't clipped.
   - Speaking → end of utterance when silence ≥ `EndSilenceMs` (default 600), **or** utterance length reaches `MaxUtteranceSeconds` (default 15), cutting at the quietest recent frame rather than mid-word.
3. **Provisional passes:** while Speaking, every `ProvisionalIntervalMs` (default 1000) transcribe the utterance so far and emit `SegmentProvisional`. If the transcriber is still busy with a previous pass, skip this one.
4. **Commit pass:** at end of utterance, transcribe the full utterance and emit `SegmentCommitted`, replacing any provisional text for that utterance.
5. Pass the last ~200 characters of committed text (same source) as the Whisper prompt for continuity.
6. Filter known silence hallucinations: drop committed segments that are empty, punctuation-only, or match a configurable blocklist (e.g. "Thank you.", "Thanks for watching!") when the utterance's mean energy is near the noise floor.

All thresholds live in `appsettings.json`.

Tests (using `samples/` fixtures and synthetic audio): silence produces zero segments; a single spoken sentence produces exactly one committed segment; two sentences separated by a 1 s pause produce two; a 20 s monologue is split without exceeding the max length.

**Accept when:** `VoiceAssistant live` prints provisional lines that update in place and committed lines that stick, and captions feel sentence-shaped rather than chopped.

*Alternative if utterance segmentation proves inadequate for long unbroken speech:* a LocalAgreement-style sliding window (re-transcribe a rolling window, commit only the prefix that two consecutive passes agree on). Don't build this unless asked.

### Stage 5 — WPF captions UI

A single window:

- Start / Stop button, elapsed time, and a small status line (model, real-time factor, dropped-audio count).
- Scrolling caption list. Each row: timestamp, source badge ("Me" / "Them"), text.
- Provisional text shown in italic/grey on the last row per source; replaced in place when committed.
- Auto-scroll to bottom unless the user has scrolled up; resume when they scroll back down.
- Optional "always on top" toggle and adjustable opacity so it can sit over a meeting window.

Wire the generic host in `App.xaml.cs`; the view model implements `ITranscriptSink`.

**Accept when:** a live meeting (or video) captions smoothly for 30 minutes with no UI freezes and memory stays flat.

### Stage 6 — Both sources together

Run Me and Them pipelines concurrently through the shared transcriber. Committed events from both sources interleave in timestamp order in the UI.

Handle crosstalk sensibly: if both sources have utterances ready, process in order of utterance end time. Echo (your voice picked up via the other side's audio) is out of scope; note it if observed.

**Accept when:** in a real call, your lines are labelled "Me" and others' "Them", in the right order.

### Stage 7 — Hardening

- GPU runtime opt-in via config; log which runtime Whisper.net actually loaded.
- Save each session's committed transcript to `Documents\VoiceAssistant\yyyy-MM-dd_HHmm.md` (the user's Documents folder, which is easier to find than AppData) (implemented as another `ITranscriptSink`).
- Recover from default-device changes by restarting the affected capture.
- Settings dialog for model, language, and the VAD thresholds from stage 4.
- Clean shutdown: Stop flushes the in-progress utterance as a final commit.

**Accept when:** a 60-minute session survives a headset unplug/replug and produces a complete saved transcript.

## 8. Seam for the LLM phase (do not implement)

The future assistant will be another `ITranscriptSink` subscribed to `SegmentCommitted` events only. Nothing in Core should assume the UI is the only consumer. Keep `TranscriptEvent` serialisable (plain records, no WPF types) so it could later be sent over a local pipe to a separate process if that proves cleaner.

## 9. Configuration (`appsettings.json` sketch)

```json
{
  "Whisper": {
    "ModelPath": "models/ggml-base.en.bin",
    "Language": "en",
    "UseGpu": false
  },
  "Vad": {
    "FrameMs": 30,
    "MinSpeechMs": 200,
    "PreRollMs": 300,
    "EndSilenceMs": 600,
    "MaxUtteranceSeconds": 15,
    "ProvisionalIntervalMs": 1000
  },
  "Capture": {
    "EnableMic": true,
    "EnableLoopback": true
  },
  "Hallucinations": {
    "Blocklist": [ "Thank you.", "Thanks for watching!", "Thank you for watching." ]
  }
}
```

## 10. Known risks

- **CPU headroom:** provisional passes multiply transcription work. If the real-time factor on `base.en` exceeds ~0.5 on CPU, raise `ProvisionalIntervalMs` or enable GPU before blaming the design.
- **Whisper hallucination on silence/noise:** mitigated by VAD and the blocklist, not eliminated.
