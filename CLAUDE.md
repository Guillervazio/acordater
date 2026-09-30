# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Acordater is a voice-driven reminder app (Android first, Windows later) that keeps nagging, hourly by default, until a reminder is marked done. **`docs/spec.md` is the source of truth** for business rules, scope and phases. Read it before implementing features, and update it when a decision changes. The spec is written in Spanish; code, identifiers and code comments are in English. User-facing strings must support Spanish and English.

Everything is local to the device: no backend, no accounts, no sync.

## Commands

```powershell
dotnet build                                   # whole solution (Android build takes minutes)
dotnet test Acordater.Tests.slnf               # Core + Data tests (~6 s); plain `dotnet test` also builds the Android app
dotnet test Acordater.Tests.slnf --filter "FullyQualifiedName~SchedulerTests"          # one class
dotnet test Acordater.Tests.slnf --filter "FullyQualifiedName~SchedulerTests.Default"  # one test
dotnet build src/Acordater.App -t:Run -f net10.0-android          # build, deploy and launch on the connected device
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" devices  # adb is not on PATH

dotnet tool restore                                               # once, installs dotnet-ef from dotnet-tools.json
dotnet ef migrations add <Name> --project src/Acordater.Data --output-dir Migrations
```

Debug deploys use fast deployment: changed assemblies are copied to `files/.__override__` without reinstalling, so `lastUpdateTime` in `dumpsys package` does not change. Check those files' timestamps to confirm a deploy.

To inspect the database on the phone, copy it out with `adb exec-out run-as com.acordater.app cat files/acordater.db` (also `-wal` and `-shm`), run through `cmd /c` with `>` because PowerShell redirection corrupts binary output. `DateTimeOffset` columns are stored as EF's binary format, not as readable dates.

Android builds need the user-level env vars `ANDROID_HOME` (Android SDK under `%LOCALAPPDATA%\Android\Sdk`) and `JAVA_HOME` (Microsoft OpenJDK 21). The `java` on PATH is an unrelated Java 8, so don't rely on it. Shells started by tools may not see these vars; then set them first from the user registry: `$env:JAVA_HOME = [Environment]::GetEnvironmentVariable('JAVA_HOME','User')` (same for `ANDROID_HOME`).

## Architecture

```
src/Acordater.Core          net10.0 class library, pure logic, no MAUI/Android references
src/Acordater.Data          EF Core + SQLite: AcordaterDbContext, ReminderStore, migrations (no MAUI references)
src/Acordater.App           .NET MAUI app (MVVM with CommunityToolkit.Mvvm), targets net10.0-android only (min API 34, Android 14)
tests/Acordater.Core.Tests  xUnit tests for Core
tests/Acordater.Data.Tests  xUnit tests against in-memory SQLite built from the real migrations
```

- **All business rules live in Core and are unit-tested there**: scheduling (first reminder, 1-hour repeats, snooze, quiet hours) and natural-language interpretation. The App project must not reimplement them. Core is the fast feedback loop; the App needs a device to verify.
- **Time is injected** (`TimeProvider`, with `FakeTimeProvider` in tests). Never call `DateTime.Now` in Core. `FakeTimeProvider` ignores the offset of the `DateTimeOffset` it receives, so use the `TestClock` helpers in tests.
- **24-hour clock everywhere**: display times as `HH:mm` in both languages, never AM/PM. A spoken hour is literal ("a las 3" = 03:00) unless a period word or am/pm changes it.
- **Interpretation is pluggable**: every interpreter implements `IReminderInterpreter` (text → task + optional first reminder time). The offline `RuleBasedInterpreter` is the baseline and the fallback. `ChatInterpreter` (Core) serves every LLM provider through `IChatClient` (`Microsoft.Extensions.AI`): shared prompt, strict JSON validated in Core, fallback to the rules on error, no connection, timeout (7 s) or invalid JSON; the result records who interpreted it (`Interpreter`, `Failure`). The App only builds the clients (`Interpretation/AiProviders`: Claude via the `Anthropic` SDK, OpenAI and Gemini via `Microsoft.Extensions.AI.OpenAI`, Gemini through its OpenAI-compatible endpoint) and registers `ConfiguredInterpreter`, which reads the chosen provider on every call. Tests use a fake `IChatClient`; no test calls a real API. See spec section 5.
- **Language**: `LanguageSettings` (App) holds the language chosen in Settings (phone, Spanish, English or bilingual). `MauiProgram` applies it at startup to the .NET cultures and Java's default locale, so `AppResources`, TTS and the AI context follow it; `AndroidSpeechRecognizer` takes its dictation language (and, when bilingual, Android 14's language switch) from it.
- **Settings**: user settings live in MAUI `Preferences`, not in the database; secrets (AI API keys) in `SecureStorage`, never logged. Quiet hours are read through `IQuietHoursProvider` on every calculation (`QuietHoursSettings` in the App; a fixed `QuietHours` is its own provider in tests). Until quiet hours are saved once, the main page redirects to the settings page (first-run setup).
- **Persistence**: schema changes always go through an EF migration. The app applies migrations at startup (`Database.Migrate()` in `MauiProgram`); never use `EnsureCreated`, or app updates would lose user data. `ReminderStore` creates a short-lived context per call through `IDbContextFactory`. `DateTimeOffset` is stored with a binary converter because SQLite cannot order or compare it as text.
- **Strings**: user-facing text lives in `Resources/Strings/AppResources.resx` (Spanish, neutral) and `AppResources.en.resx`, compiled into the strongly typed `AppResources` class through MSBuild metadata in the csproj (no designer file). Add every new key to both files. The only exception is text Android shows by itself (manifest labels, widget layout): `Platforms/Android/Resources/values/strings.xml` and `values-en/strings.xml`.
- **Reminder lifecycle lives in `ReminderService` (Core)**: add, alert (alarm fired → notify + queue the next repeat), snooze, complete, and reschedule-all. It talks to the platform only through the ports in `Core/Alerts/Ports.cs` (`IReminderStore`, `IAlarmScheduler`, `IReminderNotifier`), so it is unit-tested with fakes. Android code under `Platforms/Android` is thin: broadcast receivers only forward events to `ReminderService` (via `ReminderIntents.RunAsync`), and the adapters wrap `AlarmManager.SetAlarmClock` and notifications. Future capabilities (speech-to-text, text-to-speech) follow the same pattern.
- **Reminder page** (`ReminderPage` / `ReminderViewModel`): one page both confirms a new reminder (the interpreter's result, correctable before saving) and edits a pending one. While the date and time are untouched, Save passes the original request (null = default rule), so a default time stays subject to quiet hours; a changed time is explicit. After voice capture it speaks the confirmation and saves by itself after a countdown unless the user touches a field.
- **Voice** (`Voice/`): speech to text sits behind `ISpeechRecognizer` (App, since only the UI uses it; `AndroidSpeechRecognizer` prefers the on-device recognizer). Confirmations are spoken with MAUI's `ITextToSpeech` (`VoiceFeedback`); the ringing alarm reads the reminder with Android's `TextToSpeech` on the alarm stream inside `AlarmRingingService`. The widget and the quick settings tile (`Platforms/Android/QuickCapture.cs`) open `MainActivity` with a capture action, which raises `CaptureRequests`; `MainPage` takes the request and starts listening. `CaptureRequests.End()` marks the end of a capture.
- **Wake word** (spec 4.7): `OpenWakeWordDetector` (Core) is a C# port of openWakeWord's streaming pipeline on ONNX Runtime (Core references only `Microsoft.ML.OnnxRuntime.Managed`; the App and the tests bring the native `Microsoft.ML.OnnxRuntime`). It skips the models during silence. Its tests run the real models (`Resources/Raw/wakeword/*.onnx`, linked into the test project) on Windows text-to-speech clips in `tests/Acordater.Core.Tests/WakeWord/`. `IWakeWordDetector` (App, `Voice/`) → `WakeWordService` (Android microphone foreground service that reads `AudioRecord` on its own thread and feeds the detector). On detection it raises `CaptureRequests` when the app is on screen, otherwise it posts a full-screen notification whose intent (`CaptureIntents.ActionWakeWord`) opens `MainActivity` over the lock screen until `CaptureRequests.End()`. `WakeWordService.Pause()` returns a handle that frees the microphone (used while the app listens and while `AlarmRingingService` rings). Android 14+ forbids starting it from the background or boot: `BootReceiver` notifies instead and `MainPage` restarts it when the app opens. A custom `.onnx` keyword model is imported at runtime into the app data directory (`WakeWordSettings`).
- **Android specifics**: each reminder's id travels in the intent data URI (`acordater://reminder/{id}`) so every reminder has its own `PendingIntent`. Receivers have explicit Java `Name`s so scheduled alarms survive rebuilds. Notification channel settings are immutable once created: bump the channel id (`reminders_vN`) to change sound or importance. Alarms are recreated on boot, on app update and on every app start (idempotent).

Windows is deliberately not a target yet (spec phase 7), so don't add `net10.0-windows` TFMs.
