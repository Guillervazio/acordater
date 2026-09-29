# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Acordater is a voice-driven reminder app (Android first, Windows later) that keeps nagging, hourly by default, until a reminder is marked done. **`docs/spec.md` is the source of truth** for business rules, scope and phases. Read it before implementing features, and update it when a decision changes. The spec is written in Spanish; code, identifiers and code comments are in English. User-facing strings must support Spanish and English.

Everything is local to the device: no backend, no accounts, no sync.

## Commands

```powershell
dotnet build                                   # whole solution (Android build takes minutes)
dotnet test                                    # Core tests only; fast, no device needed
dotnet test --filter "FullyQualifiedName~SchedulerTests"          # one class
dotnet test --filter "FullyQualifiedName~SchedulerTests.Default"  # one test
dotnet build src/Acordater.App -t:Run -f net10.0-android          # build, deploy and launch on the connected device
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" devices  # adb is not on PATH
```

Android builds need the user-level env vars `ANDROID_HOME` (Android SDK under `%LOCALAPPDATA%\Android\Sdk`) and `JAVA_HOME` (Microsoft OpenJDK 21). The `java` on PATH is an unrelated Java 8, so don't rely on it.

## Architecture

```
src/Acordater.Core          net10.0 class library, pure logic, no MAUI/Android references
tests/Acordater.Core.Tests  xUnit tests for Core
src/Acordater.App           .NET MAUI app, targets net10.0-android only (min API 33)
```

- **All business rules live in Core and are unit-tested there**: scheduling (first reminder, 1-hour repeats, snooze, quiet hours) and natural-language interpretation. The App project must not reimplement them. Core is the fast feedback loop; the App needs a device to verify.
- **Time is injected** (`TimeProvider`, with `FakeTimeProvider` in tests). Never call `DateTime.Now` in Core. `FakeTimeProvider` ignores the offset of the `DateTimeOffset` it receives, so use the `TestClock` helpers in tests.
- **24-hour clock everywhere**: display times as `HH:mm` in both languages, never AM/PM. A spoken hour is literal ("a las 3" = 03:00) unless a period word or am/pm changes it.
- **Interpretation is pluggable**: every interpreter implements `IReminderInterpreter` (text → task + optional first reminder time). The offline rule-based interpreter is the baseline and the fallback. LLM providers (Claude, OpenAI, Gemini, Gemini Nano) are added as more implementations, each using the user's own API key. See spec section 5.
- **Platform capabilities sit behind interfaces** in the App (alarm scheduling, notifications, speech-to-text, text-to-speech), with Android implementations under `Platforms/Android`. Alarms must use exact `AlarmManager` alarms, play on the alarm audio stream (so they sound in silent mode), and be rescheduled on boot.

Windows is deliberately not a target yet (spec phase 7), so don't add `net10.0-windows` TFMs.
