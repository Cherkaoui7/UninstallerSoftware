### Incident 2 — Command Parser / Executable Validation Failure

**Date**: ~2026-08-30 (commits in phase 5K series)

#### Symptom
Clicking "Official Uninstall" on a valid E2E test application produced:
> "Command validation failed. The uninstall command is invalid or missing."

The `ProcessExecutor` was never invoked.

#### Trigger
Attempting to uninstall the E2E-App-001 fixture from the VM.

#### Exact Evidence
From the Phase 5G tracing report: The `CommandParser` correctly parsed the quoted executable path and separated arguments. However, `IFileSystemService.FileExists(parsed.ExecutablePath)` returned `false` in the production VM context, even though PowerShell's `Test-Path` on the same path returned `True`.

The exact call chain was:
1. `UninstallService` fetches `Application` from `IApplicationRepository`.
2. `UninstallService` calls `_commandParser.Parse(application)`.
3. `CommandParser` extracts `ExecutablePath` (stripping quotes).
4. `CommandParser` calls `_fileSystem.FileExists(executablePath)` → returns `false`.
5. `CommandParser` returns `StructuredCommand` with `ExecutionType = Missing`, `IsValid = false`.
6. `UninstallService` sees `IsValid = false` and fails the session.
7. `ProcessExecutor` is **never reached**.

#### Root Cause
The root cause was ultimately traced to **stale VM artifacts** — the VM was running an older binary that did not include parser fixes, or the E2E fixture executable itself did not exist at the expected path. The parser logic was correct (proven by unit tests feeding the exact string). The failure was an environment/artifact synchronization issue (see Incident 3).

#### Files / Components Involved
- `src/Uninstaller.Core/Services/CommandParser.cs` — diagnostic logging added
- `src/Uninstaller.Core/Services/UninstallService.cs` — call chain investigated
- `tests/Uninstaller.Core.Tests/Services/UninstallServiceProductionPathTests.cs` — added 7 production-path tests

#### Resolution
1. Added comprehensive Serilog diagnostic logging to `CommandParser` (raw command, extracted path, `FileExists` result, final decision).
2. Added 7 regression tests proving the full `UninstallService → CommandParser → FileSystemService` pipeline functions correctly for quoted, unquoted, missing, malformed, and forbidden executables.
3. The actual fix was deploying a correctly synchronized binary with the matching E2E fixture (see Incident 3).

#### Regression Tests
- `UninstallServiceProductionPathTests.cs`: 7 tests covering the exact production call chain.

#### VM Verification
After deploying the correct binary with the E2E-App-002 fixture, official uninstall executed successfully.

#### Status
**FIXED** (root cause was artifact synchronization, parser was correct)

---
