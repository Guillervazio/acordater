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

Android builds need the user-level env vars `ANDROID_HOME` (Android SDK under `%LOCALAPPDATA%\Android\Sdk`) and `JAVA_HOME` (Microsoft OpenJDK 21). The `java` on PATH is an unrelated Java 8, so don't rely on it.

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
- **Interpretation is pluggable**: every interpreter implements `IReminderInterpreter` (text → task + optional first reminder time). The offline rule-based interpreter is the baseline and the fallback. LLM providers (Claude, OpenAI, Gemini, Gemini Nano) are added as more implementations, each using the user's own API key. See spec section 5.
- **Settings**: user settings live in MAUI `Preferences`, not in the database. Quiet hours are read through `IQuietHoursProvider` on every calculation (`QuietHoursSettings` in the App; a fixed `QuietHours` is its own provider in tests). Until quiet hours are saved once, the main page redirects to the settings page (first-run setup).
- **Persistence**: schema changes always go through an EF migration. The app applies migrations at startup (`Database.Migrate()` in `MauiProgram`); never use `EnsureCreated`, or app updates would lose user data. `ReminderStore` creates a short-lived context per call through `IDbContextFactory`. `DateTimeOffset` is stored with a binary converter because SQLite cannot order or compare it as text.
- **Strings**: user-facing text lives in `Resources/Strings/AppResources.resx` (Spanish, neutral) and `AppResources.en.resx`, compiled into the strongly typed `AppResources` class through MSBuild metadata in the csproj (no designer file). Add every new key to both files.
- **Reminder lifecycle lives in `ReminderService` (Core)**: add, alert (alarm fired → notify + queue the next repeat), snooze, complete, and reschedule-all. It talks to the platform only through the ports in `Core/Alerts/Ports.cs` (`IReminderStore`, `IAlarmScheduler`, `IReminderNotifier`), so it is unit-tested with fakes. Android code under `Platforms/Android` is thin: broadcast receivers only forward events to `ReminderService` (via `ReminderIntents.RunAsync`), and the adapters wrap `AlarmManager.SetAlarmClock` and notifications. Future capabilities (speech-to-text, text-to-speech) follow the same pattern.
- **Android specifics**: each reminder's id travels in the intent data URI (`acordater://reminder/{id}`) so every reminder has its own `PendingIntent`. Receivers have explicit Java `Name`s so scheduled alarms survive rebuilds. Notification channel settings are immutable once created: bump the channel id (`reminders_vN`) to change sound or importance. Alarms are recreated on boot, on app update and on every app start (idempotent).

Windows is deliberately not a target yet (spec phase 7), so don't add `net10.0-windows` TFMs.
