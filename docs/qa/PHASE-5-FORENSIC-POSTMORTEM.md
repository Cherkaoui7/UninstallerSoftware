# Phase 5 Forensic Postmortem

- **Phase**: 5 (Production UI, Real-Machine Validation, Release Freeze)
- **Product**: Uninstaller for Windows
- **Final Version**: 0.23.0
- **Final Git SHA**: `1366476d37c34c92f94a609d82f59a50ef094074`
- **Date Range**: 2026-08-27 through 2026-08-31
- **Target OS**: Windows 10 / 11
- **Architecture**: win-x64
- **Final Test Count**: 343 (passed), 0 failed, 0 skipped
- **Final Release Artifact**: `publish-vm/Uninstaller.App.exe`
- **Final SHA-256**: `1E26B5B3D3CC08FA610030337F4F6C9C253664128D74567807725CE97F17D5FC`
- **Final Decision**: **RELEASE READY**

---

## Executive Summary

Phase 5 was the first time the Uninstaller application was run against real installed Windows software on a live Hyper-V virtual machine. Every prior phase (0–4) had been validated exclusively through automated tests, synthetic fixtures, and code review.

Phase 5 revealed **14 distinct production defects** that were invisible to the automated test suite. These defects spanned DI container lifecycle management, foreign key identity propagation, safety classification logic, filesystem cleanup recursion, WPF DataTemplate resolution, and artifact deployment integrity. Each defect was diagnosed through real-time VM observation, fixed in source, covered by regression tests, and re-validated on the VM.

The phase began on 2026-08-27 with the construction of the production WPF shell and concluded on 2026-08-31 with a frozen release candidate that passed the complete Telegram Desktop end-to-end cleanup workflow on a real Windows 10 VM.

---

## Phase 5 Objective

Phase 5 was intended to validate the **complete production workflow** on a real Windows machine:

```
Application Discovery
  → Application Details
    → Official Uninstall (real subprocess)
      → Uninstall Verification
        → Post-Uninstall Persistence
          → Residual Analysis (filesystem + registry + shortcuts)
            → Cleanup Plan (risk-scored, user-selectable)
              → Safety Preflight (canonical paths, protection rules)
                → Backup (ZIP archive, metadata persistence)
                  → Transaction Journal (state machine)
                    → Cleanup Execution (recursive deletion)
                      → Post-Cleanup Verification
                        → Finish & View History
                          → History Details
                            → Back Navigation
```

This workflow was considered the critical production path because it represents the **only irreversible destructive operation** in the application — permanently deleting files and registry keys from the user's machine. Every stage in the pipeline exists to ensure that deletion only occurs after explicit user authorization, safety validation, and backup creation.

Prior phases had validated each stage in isolation. Phase 5 was the first time all stages executed in sequence in a single application lifetime, using real Windows registry entries, real filesystems, and real installed applications.

---

## Complete Incident Timeline

### Incident 1 — Stale Uninstall Command Persistence

**Date**: ~2026-08-30 (commit `a876735`)

#### Symptom
After re-running application discovery, the `UninstallCommand` displayed in the Application Details view did not reflect the current registry value. The UI showed the original command string even after the registry entry was manually updated.

#### Trigger
Updating the `UninstallString` registry value for a test application and then triggering a discovery refresh in the Uninstaller.

#### Exact Evidence
The `ApplicationDeduplicator.Merge` method used the null-coalescing assignment operator:
```csharp
target.UninstallCommand ??= source.UninstallCommand;
```
This operator only assigns if the target is `null`. If the database already contained a stale value, the fresh registry truth was silently discarded.

#### Root Cause
The `??=` operator preserves existing non-null values. When an application was already persisted with a command string, subsequent discovery refreshes that brought a new command string from the registry were ignored because the existing value was non-null.

#### Files / Components Involved
- `src/Uninstaller.Infrastructure/Services/ApplicationDeduplicator.cs`

#### Resolution
Changed merge logic to prefer the source (registry truth) over the target (database):
```csharp
target.UninstallCommand = source.UninstallCommand ?? target.UninstallCommand;
```
This overwrites the stored command with the registry value when available, retaining the old value only when the registry returns null.

#### Regression Tests
- `ApplicationSynchronizationTests` in `tests/Uninstaller.Infrastructure.Tests/`
  - Tests covering changed uninstall command, removed uninstall command, and blocked executable validation.

#### VM Verification
After deploying the fix, discovery refresh correctly updated the displayed command string.

#### Status
**FIXED**

---

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

### Incident 3 — Version / Artifact Synchronization Problems

**Date**: Throughout Phase 5 (~2026-08-29 through 2026-08-31)

#### Symptom
Multiple instances where the VM was running an older executable despite newer source changes being committed and published. Symptoms included:
- UI not reflecting code changes.
- Old version numbers displayed (0.19.0, 0.21.x when 0.22.0 was expected).
- Wrong file sizes.
- Mismatched `ProductVersion` strings.
- `publish-vm/` containing multiple artifacts from different builds.

#### Trigger
Deploying new builds to the VM without rigorous version and hash verification.

#### Exact Evidence
From the Phase 5G QA Reset Report: "All previous Uninstaller binaries in the brain folder have been destroyed to guarantee ONE single authoritative binary." Multiple stale artifacts (`Uninstaller-0.21.0-phase5k-win-x64.exe`, `Uninstaller-0.21.1-phase5k-fix2-win-x64.exe`, `Uninstaller-0.21.2-phase5k-fix3-win-x64.exe`) existed alongside newer versions. The VM was inadvertently running older binaries.

#### Root Cause
1. No mandatory hash verification step after deployment.
2. Multiple named artifacts accumulated in output directories.
3. The `\\tsclient` transfer mechanism required manual file selection, creating opportunity for error.
4. Windows cached the old version in Program Files paths when using installed-mode deployment.

#### Files / Components Involved
- `Directory.Build.props` (version bumps: 0.19.0 → 0.21.x → 0.22.0 → 0.23.0)
- `publish-vm/` output directory
- VM deployment scripts

#### Resolution
1. Established a mandatory **SHA-256 verification invariant**: `git rev-parse HEAD` must equal the Git SHA embedded in `ProductVersion`.
2. Published to a clean `publish-vm/` directory with a single authoritative executable.
3. Verified on the VM using: `(Get-Item Uninstaller.App.exe).VersionInfo.ProductVersion` and `Get-FileHash`.
4. Documented that old binaries must be explicitly destroyed before deploying new ones.

#### Regression Tests
No automated test — this is a deployment process discipline.

#### VM Verification
Final verified deployment:
- Host SHA-256: `1E26B5B3D3CC08FA610030337F4F6C9C253664128D74567807725CE97F17D5FC`
- VM SHA-256: matched.
- ProductVersion: `0.23.0+1366476d37c34c92f94a609d82f59a50ef094074`

#### Status
**FIXED** (process established)

---

### Incident 4 — Hyper-V Copy-VMFile Failure

**Date**: ~2026-08-29 through 2026-08-30

#### Symptom
Attempts to transfer files from the host to the Hyper-V VM using `Copy-VMFile` failed with:
> 0x80070015 — device not ready

#### Trigger
Running `Copy-VMFile -Name "VM" -SourcePath "..." -DestinationPath "..." -FileSource Host` from the host.

#### Exact Evidence
- The Hyper-V Guest Service Interface was not reliably starting inside the VM.
- Host-side `vmicguestinterface` service failure prevented the file copy.
- SMB share attempts also failed due to VM firewall configuration and network isolation.

#### Root Cause
The Hyper-V Guest Service Interface (Integration Service) responsible for host-to-guest file copy was not enabled or not operational in the VM configuration. The VM's network configuration also prevented standard SMB file sharing.

#### Files / Components Involved
- Hyper-V VM configuration
- Guest Service Interface integration service
- Windows Firewall on VM

#### Resolution
Adopted **Enhanced Session (RDP) drive redirection** as the deployment mechanism. With Enhanced Session mode enabled, the host's drives are mapped inside the VM as `\\tsclient\C\...`, allowing direct file copy via Explorer or PowerShell:
```powershell
Copy-Item "\\tsclient\C\Users\USER\Documents\Uninstaller\publish-vm\Uninstaller.App.exe" -Destination "C:\Users\test\Desktop\"
```

This became the preferred and final deployment path because it:
1. Required no guest service configuration.
2. Worked immediately after enabling Enhanced Session mode.
3. Was verifiable via SHA-256 hash comparison.

#### Regression Tests
No automated test — infrastructure configuration.

#### VM Verification
All subsequent deployments used `\\tsclient` successfully.

#### Status
**FIXED** (workaround adopted, `Copy-VMFile` abandoned)

---

### Incident 5 — Applications Navigation DI Crash

**Date**: ~2026-08-31 (commit `da4deac`)

#### Symptom
Clicking "Applications" in the sidebar navigation caused an immediate crash:
> `System.InvalidOperationException: Cannot resolve scoped service '...' from root provider.`

#### Trigger
Navigating to the Applications page when `ValidateScopes = true` was enabled.

#### Exact Evidence
The `NavigationService.NavigateTo<TViewModel>()` method resolved ViewModels directly from the root `IServiceProvider`. With strict scope validation enabled (`ValidateScopes = true`), resolving any ViewModel that transitively depended on a scoped service (e.g., `IApplicationRepository` → `AppDbContext`) from the root provider threw `InvalidOperationException`.

#### Root Cause
The original `NavigationService` stored a reference to the root `IServiceProvider` and called `_serviceProvider.GetRequiredService<TViewModel>()` directly. In the DI container configuration, `AppDbContext` and repositories were registered as **Scoped** (correct for EF Core), but the navigation service resolved them from the root (incorrect).

#### Files / Components Involved
- `src/Uninstaller.App/Services/NavigationService.cs`
- `src/Uninstaller.App/App.xaml.cs` (DI registration)

#### Resolution
Modified `NavigationService.NavigateTo<TViewModel>()` to create a fresh `IServiceScope` for each navigation, resolve the ViewModel from the scoped provider, and dispose the previous scope:
```csharp
_currentScope?.Dispose();
_currentScope = _serviceProvider.CreateScope();
CurrentViewModel = _currentScope.ServiceProvider.GetRequiredService<TViewModel>();
```

#### Regression Tests
- `NavigationServiceTests.cs`: Tests verifying scoped resolution for `DashboardViewModel`, `HistoryViewModel`, `SettingsViewModel`.
- `DependencyInjectionValidationTests.cs`: Full production DI container validation with `ValidateScopes = true`.

#### VM Verification
Applications page loads correctly after deployment.

#### Status
**FIXED**

---

### Incident 6 — Disposed IServiceProvider During Cleanup Execution

**Date**: ~2026-08-31 (commit `6857602`)

#### Symptom
After completing the Cleanup Plan and clicking "Execute Cleanup," the application threw:
> `System.ObjectDisposedException: Cannot access a disposed object.`

The cleanup engine never started.

#### Trigger
Navigating from `ApplicationDetailsViewModel` → `CleanupPlanViewModel` → executing cleanup.

#### Exact Evidence
The navigation flow created this scope lifecycle problem:
1. User navigates to `ApplicationDetailsViewModel` — `NavigationService` creates **Scope A**.
2. User clicks "Analyze Residuals" and then proceeds to `CleanupPlanViewModel` — still within Scope A, or a new Scope B is created.
3. User clicks "Execute Cleanup" — `CleanupPlanViewModel.ExecuteCleanup()` calls `ActivatorUtilities.CreateInstance(...)` using the `IServiceProvider` from the scope that was created during the plan phase.
4. But `NavigationService.NavigateTo<CleanupExecutionViewModel>()` creates **Scope C**, which **disposes Scope B**.
5. The `IServiceProvider` captured in the `CleanupExecutionViewModel` constructor points to the now-dead Scope B.
6. Any subsequent service resolution throws `ObjectDisposedException`.

#### Root Cause
The navigation scope lifecycle was coupled to page transitions. When `NavigationService` navigated to a new ViewModel, it disposed the previous scope. But the cleanup execution workflow spanned multiple pages, and the execution ViewModel needed services from a scope that outlived its originating navigation.

#### Files / Components Involved
- `src/Uninstaller.App/Services/NavigationService.cs`
- `src/Uninstaller.App/Services/CleanupViewModelFactory.cs` (new)
- `src/Uninstaller.App/Services/ICleanupViewModelFactory.cs` (new)
- `src/Uninstaller.App/ViewModels/CleanupPlanViewModel.cs`
- `src/Uninstaller.App/ViewModels/CleanupExecutionViewModel.cs`

#### Resolution
Introduced a **dedicated `CleanupViewModelFactory`** that owns an isolated `IServiceScope` for the entire cleanup workflow:
1. `ICleanupViewModelFactory.CreatePlanViewModel(...)` creates a dedicated scope.
2. `ICleanupViewModelFactory.CreateExecutionViewModel(...)` resolves from the **same scope**.
3. The scope is only disposed when the entire cleanup workflow completes or is cancelled.
4. `NavigationService` uses `NavigateTo(instance)` (passing a pre-created ViewModel) instead of `NavigateTo<T>()` (which would create a new scope).

#### Regression Tests
- `DependencyInjectionValidationTests.cs`: Tests for cleanup scope lifecycle, deferred disposal, and ViewModel factory integration.
- `ProductionCleanupSafetyPipelineTests.cs` (Req19–Req22): Production composition tests.

#### VM Verification
Cleanup execution completes successfully without `ObjectDisposedException`.

#### Status
**FIXED**

---

### Incident 7 — False Protected Decision

**Date**: ~2026-08-31 (commit `a2aeb2f`)

#### Symptom
The Cleanup Plan displayed the Telegram AppData directory as:
- `Protected = false`
- `Risk = Low`
- `Recommended = true`

But when the user selected it and proceeded to cleanup, the preflight validator rejected it as **Protected**.

#### Trigger
Selecting `C:\Users\test\AppData\Roaming\Telegram Desktop` in the cleanup plan and proceeding to execution.

#### Exact Evidence
The `CleanupPreflightValidator` used a broad path containment check against a list of protected paths. The `_protectedPaths` list included `UserProfile` (e.g., `C:\Users\test`). The containment logic checked whether any protected path was a **prefix** of the artifact path. Since `C:\Users\test` is a prefix of `C:\Users\test\AppData\Roaming\Telegram Desktop`, the artifact was classified as Protected — even though it was clearly an application-owned subdirectory.

The `EvidenceEngine` (which scored the artifact for the plan UI) used a different, more permissive classification, leading to the disagreement between the plan display and the preflight decision.

#### Root Cause
Two independent safety models existed:
1. `EvidenceEngine` — scored artifacts for the plan UI (showed Low Risk).
2. `CleanupPreflightValidator` — authorized artifacts for execution (rejected as Protected).

The preflight validator's containment check was too broad: treating the entire user profile tree as protected blocked legitimate application-owned directories.

#### Files / Components Involved
- `src/Uninstaller.Core/Services/CleanupPreflightValidator.cs`
- `src/Uninstaller.Core/Services/EvidenceEngine.cs`
- `src/Uninstaller.Windows/Services/WindowsFileCleanupExecutor.cs`

#### Resolution
Implemented a unified protection hierarchy:
1. **Protected exact roots**: System directories (`C:\Windows`, `C:\Program Files`, etc.) — always blocked.
2. **Recursively protected system trees**: `C:\Windows\System32\...` — always blocked at any depth.
3. **User data trees with application exceptions**: `C:\Users\<user>\AppData\Roaming\<AppName>` — allowed when the subdirectory name matches the application being cleaned.
4. **Desktop special handling**: Direct `Desktop` children may be shortcuts (allowed), but the Desktop directory itself is protected.
5. **Canonical path normalization**: All paths normalized via `Path.GetFullPath` before comparison.
6. **Reparse point / symlink / junction defense**: Artifacts on reparse points are rejected.

The preflight and evidence engine were aligned to use the same classification rules.

#### Regression Tests
- `CleanupPreflightValidatorTests.cs`: Updated test expectations.
- `ProductionCleanupSafetyPipelineTests.cs`: End-to-end cleanup plan and preflight tests.

#### VM Verification
Telegram AppData directory correctly authorized by preflight.

#### Status
**FIXED** (security was strengthened, not weakened)

---

### Incident 8 — SQLite Foreign Key Failure During Backup

**Date**: ~2026-08-31 (commit `1cf45c2`)

#### Symptom
After preflight authorization and before cleanup execution:
> `SQLite Error 19: FOREIGN KEY constraint failed`

The error occurred during `INSERT INTO Backups`.

#### Trigger
Creating a backup for the selected cleanup plan item.

#### Exact Evidence
The `Backup` entity requires a `SessionId` that references an existing `UninstallSession`. The code was using `ResidualAnalysisSession.Id` (an in-memory identifier for the analysis run) instead of the persisted `UninstallSession.Id` (the database-tracked session record).

Since `ResidualAnalysisSession.Id` did not correspond to any row in the `UninstallSessions` table, the foreign key constraint failed.

#### Root Cause
Identity confusion across the pipeline. Three distinct identities existed:
1. `Application.Id` — the application being uninstalled.
2. `UninstallSession.Id` — the persisted session record in SQLite.
3. `ResidualAnalysisSession.Id` — an ephemeral in-memory identifier for the residual analysis run.

The code path that created the `CleanupPlan` and `Backup` entities used (3) where it should have used (2).

#### Files / Components Involved
- `src/Uninstaller.Core/Services/CleanupPlanGenerator.cs`
- `src/Uninstaller.Core/Services/BackupService.cs`
- `src/Uninstaller.Infrastructure/Persistence/AppDbContext.cs` (FK constraints)

#### Resolution
Corrected the identity chain to ensure `UninstallSession.Id` (the persisted session) is propagated through the entire pipeline:
```
Application
  → UninstallSession (persisted, has FK from Application)
    → ResidualAnalysis (uses UninstallSession.Id)
      → CleanupPlan (FK → UninstallSession.Id)
        → CleanupPlanItem (FK → CleanupPlan.Id)
          → Backup (FK → UninstallSession.Id)
            → TransactionJournal (FK → CleanupPlanItem.Id)
```

#### Regression Tests
- `ProductionCleanupSafetyPipelineTests.cs`: Tests asserting FK integrity through the entire pipeline.
- `DependencyInjectionValidationTests.cs`: Parent entity persistence tests.

#### VM Verification
Backup creation succeeds without FK constraint errors.

#### Status
**FIXED**

---

### Incident 9 — DirectoryNotEmpty During Real Cleanup

**Date**: ~2026-08-31 (commit `574c7c5`)

#### Symptom
After successful preflight, backup, and journal initialization, the cleanup executor failed:
> `DirectoryNotEmpty: 'C:\Users\test\AppData\Roaming\Telegram Desktop'`

Independent verification showed the directory still existed and contained `log_start0.txt`.

#### Trigger
Executing cleanup on the Telegram Desktop residual directory.

#### Exact Evidence
The `WindowsFileCleanupExecutor` called `Directory.Delete(path, recursive: false)`, which fails if the directory is not empty. The directory contained `log_start0.txt`, a log file created by Telegram Desktop.

Running on the VM:
```powershell
Test-Path "C:\Users\test\AppData\Roaming\Telegram Desktop"
# True

Get-ChildItem "C:\Users\test\AppData\Roaming\Telegram Desktop"
# log_start0.txt
```

#### Root Cause
`Directory.Delete(path, false)` only deletes empty directories. The cleanup executor did not implement recursive deletion because blind `Directory.Delete(path, true)` was considered unsafe — it could delete unexpected content without verification.

#### Technical Analysis
Simply switching to `Directory.Delete(path, true)` was rejected as unsafe because:
1. It provides no opportunity to validate each child before deletion.
2. It cannot detect reparse points (symlinks/junctions) that could redirect deletion to unintended locations.
3. It does not verify containment — a symlink inside the directory could point outside the authorized tree.

#### Files / Components Involved
- `src/Uninstaller.Windows/Services/WindowsFileCleanupExecutor.cs`

#### Resolution
Implemented a **safe recursive cleanup** algorithm:
1. **Enumerate children** — list all files and subdirectories.
2. **Canonical containment check** — verify each child's canonical path is strictly contained within the authorized parent path.
3. **Reparse point defense** — reject any child that is a reparse point (symlink, junction, or mount point).
4. **Read-only attribute reset** — clear read-only flags on files before deletion.
5. **Bottom-up deletion** — delete files first, then empty subdirectories, then the root directory.
6. **Post-delete verification** — assert `!Directory.Exists(path)` after deletion.

#### Regression Tests
- `WindowsFileCleanupExecutorTests.cs`: Tests for recursive deletion, read-only files, reparse point rejection, containment validation, and post-delete verification.

#### VM Verification
```powershell
Test-Path "C:\Users\test\AppData\Roaming\Telegram Desktop"
# False
```
The directory was successfully and safely deleted.

#### Status
**FIXED**

---

### Incident 10 — Finish & View History Button No-Op

**Date**: ~2026-08-31 (commit `81c7d42`)

#### Symptom
After cleanup completed successfully, clicking the "Finish & View History" button had no visible effect. The application remained on the cleanup execution screen.

#### Trigger
Clicking the "Finish & View History" button on `CleanupExecutionView`.

#### Exact Evidence
Inspecting `CleanupExecutionView.xaml` revealed the button existed visually but had no `Command` binding:
```xml
<Button Content="Finish &amp; View History" />
```
No `Command="{Binding FinishCommand}"` was present. The `CleanupExecutionViewModel` also lacked a `FinishCommand` property.

#### Root Cause
The button was created as part of the UI layout but the command implementation was never wired.

#### Files / Components Involved
- `src/Uninstaller.App/Views/CleanupExecutionView.xaml`
- `src/Uninstaller.App/ViewModels/CleanupExecutionViewModel.cs`

#### Resolution
1. Added `Command="{Binding FinishCommand}"` to the XAML button.
2. Implemented `FinishCommand` as a `RelayCommand` in `CleanupExecutionViewModel`.
3. The command invokes `NavigationService.NavigateTo<HistoryViewModel>()`, which creates a fresh scope for the History page.
4. `CanFinish` returns `true` only when cleanup execution is complete.

#### Regression Tests
- `ProductionCleanupSafetyPipelineTests.cs` (Req19–Req22): Tests verifying the finish navigation flow.

#### VM Verification
Button successfully navigates to the History screen after cleanup.

#### Status
**FIXED**

---

### Incident 11 — HistoryRepository EF Query Failure

**Date**: ~2026-08-31 (commit `81c7d42`)

#### Symptom
After navigating to the History page, the ViewModel failed to load recent activities:
> EF Core translation error on sub-query with `.Include(...)`

#### Trigger
`HistoryViewModel` calling `_historyRepository.GetRecentActivitiesAsync()` during initialization.

#### Root Cause
The `HistoryRepository.GetRecentActivitiesAsync()` method contained an EF Core query with an invalid `.Include(s => _context.Applications.FirstOrDefault(...))` pattern that EF Core could not translate to SQL.

#### Files / Components Involved
- `src/Uninstaller.Infrastructure/Persistence/Repositories/HistoryRepository.cs`

#### Resolution
Refactored the activity aggregation query to use standard LINQ projection (`.Select(...)`) instead of the invalid `.Include(...)` sub-query pattern.

#### Regression Tests
- Regression coverage through `HistoryDetailsNavigationTests.cs` which exercises the full History loading pipeline.

#### VM Verification
History page loads and displays 21 recent activities.

#### Status
**FIXED**

---

### Incident 12 — History Details Displayed CLR Type Name

**Date**: ~2026-08-31 (commit `1366476`)

#### Symptom
Clicking "Details" on a History activity item rendered:
> `Uninstaller.App.ViewModels.ApplicationHistoryViewModel`

as plain text instead of the actual `ApplicationHistoryView` UI.

#### Trigger
Clicking "Details" on a history item after navigating to the History page.

#### Exact Evidence
VM log trace:
```
19:39:05.617 [Navigation] NavigateTo(instance ApplicationHistoryViewModel)
19:39:05.619 [Navigation] HistoryView DataContext assigned to null
```
The screen displayed the ViewModel's `.ToString()` output — the CLR fully qualified type name.

#### Root Cause
WPF `ContentControl` behavior: when `Content` is set to an object, WPF looks up the resource dictionaries for a `DataTemplate` with `DataType="{x:Type TViewModel}"`. If no matching `DataTemplate` is found, WPF creates a `TextBlock` displaying `object.ToString()`, which for a ViewModel is the type name string.

`MainWindow.xaml.Resources` and `App.xaml.Resources` only contained DataTemplates for 9 top-level ViewModels. The following were missing:
- `ApplicationHistoryViewModel` → `ApplicationHistoryView`
- `CleanupSessionHistoryViewModel` → `CleanupSessionHistoryView`
- `RecoverySessionHistoryViewModel` → `RecoverySessionHistoryView`

#### Files / Components Involved
- `src/Uninstaller.App/MainWindow.xaml`
- `src/Uninstaller.App/App.xaml`

#### Resolution
Declared implicit `DataTemplate` definitions for all 12 ViewModels in both `App.xaml` and `MainWindow.xaml`:

| ViewModel | View |
|:---|:---|
| `DashboardViewModel` | `DashboardView` |
| `ApplicationsViewModel` | `ApplicationsView` |
| `ApplicationDetailsViewModel` | `ApplicationDetailsView` |
| `CleanupPlanViewModel` | `CleanupPlanView` |
| `CleanupExecutionViewModel` | `CleanupExecutionView` |
| `HistoryViewModel` | `HistoryView` |
| `ApplicationHistoryViewModel` | `ApplicationHistoryView` |
| `CleanupSessionHistoryViewModel` | `CleanupSessionHistoryView` |
| `RecoverySessionHistoryViewModel` | `RecoverySessionHistoryView` |
| `RecoveryViewModel` | `RecoveryView` |
| `RecoverySessionViewModel` | `RecoverySessionView` |
| `SettingsViewModel` | `SettingsView` |

#### Regression Tests
- `HistoryDetailsNavigationTests.cs` (Req02): Verifies DataTemplate resolution for all 12 ViewModels.
- `HistoryDetailsNavigationTests.cs` (Req03): Asserts resolved view is `ApplicationHistoryView`, not `TextBlock`.

#### VM Verification
History Details renders `ApplicationHistoryView` with populated timeline data.

#### Status
**FIXED**

---

### Incident 13 — History Detail ViewModels Not Initialized

**Date**: ~2026-08-31 (commit `1366476`)

#### Symptom
After navigating to History Details, the view rendered correctly (Incident 12 fixed) but displayed empty lists — no timeline events, no session details.

#### Trigger
Clicking Details on a History activity item.

#### Root Cause
`ApplicationHistoryViewModel`, `CleanupSessionHistoryViewModel`, and `RecoverySessionHistoryViewModel` each defined `public async Task InitializeAsync()` that loaded data from the database, but none of them invoked this method during construction. The WPF DataTemplate instantiated the view and set the DataContext, but without initialization, all observable collections remained empty.

#### Files / Components Involved
- `src/Uninstaller.App/ViewModels/ApplicationHistoryViewModel.cs`
- `src/Uninstaller.App/ViewModels/CleanupSessionHistoryViewModel.cs`
- `src/Uninstaller.App/ViewModels/RecoverySessionHistoryViewModel.cs`

#### Resolution
Added `_ = InitializeAsync();` in each detail ViewModel constructor to trigger fire-and-forget initialization upon creation. Error handling within `InitializeAsync()` ensures exceptions are caught and logged without crashing the UI thread.

#### Regression Tests
- `HistoryDetailsNavigationTests.cs` (Req01, Req03–Req06): All tests exercise automatic initialization.

#### VM Verification
Timeline events populated automatically upon navigating to History Details.

#### Status
**FIXED**

---

### Incident 14 — Instance Navigation / DataContext Lifecycle

**Date**: ~2026-08-31 (commits `81c7d42`, `1366476`)

#### Symptom
When using `NavigateTo(instance)` (passing a pre-created ViewModel) instead of `NavigateTo<T>()` (generic type resolution), the `ContentControl.DataContext` was temporarily set to `null` before being assigned to the new ViewModel, causing visual flicker and a brief display of the CLR type name.

#### Trigger
`HistoryViewModel.ViewSessionDetailsCommand` creating a detail ViewModel via `IHistoryViewModelFactory` and passing it to `NavigationService.NavigateTo(instance)`.

#### Root Cause
The `NavigationService.NavigateTo(instance)` path needed to set `CurrentViewModel` directly without creating a new scope (because the instance already owned its scope via the factory). The initial implementation disposed the current scope before setting the new ViewModel, causing a brief `null` DataContext.

#### Files / Components Involved
- `src/Uninstaller.App/Services/NavigationService.cs`
- `src/Uninstaller.App/Services/HistoryViewModelFactory.cs`
- `src/Uninstaller.App/ViewModels/HistoryViewModel.cs`
- `src/Uninstaller.App/MainWindow.xaml.cs` (DataContext logging)
- `src/Uninstaller.App/Views/ApplicationHistoryView.xaml.cs` (Loaded/DataContext logging)

#### Resolution
1. `NavigationService.NavigateTo(instance)` now sets `CurrentViewModel` directly without scope manipulation.
2. `IHistoryViewModelFactory` allocates and manages the scope for each detail ViewModel.
3. Added diagnostic logging at every navigation checkpoint: command invocation, instance creation, `NavigateTo` entry, View Loaded events, DataContext assignments, and `MainContentControl` DataContext changes.
4. `GoBackCommand` in detail ViewModels navigates via `NavigateTo<HistoryViewModel>()` (fresh scope) without disposed-scope exceptions.

#### Regression Tests
- `HistoryDetailsNavigationTests.cs` (Req06): Switching between detail views A → B preserves scope isolation.

#### VM Verification
Smooth navigation: History → Details → Back → Details with no flicker, no type-name rendering, no `ObjectDisposedException`.

#### Status
**FIXED**

---

## E2E Test Evolution

### Synthetic E2E Fixtures (E2E-App-001 / E2E-App-002)

Synthetic test applications were created as controlled fixtures for VM testing.

**E2E-App-001** was the first fixture — a minimal .NET console application with registry entries. It was used to validate:
- Application discovery from registry.
- Application Details display.
- Official uninstall subprocess execution.

What it **failed to prove**: It did not produce residual artifacts after uninstallation, so the entire residual analysis → cleanup pipeline could not be tested.

**E2E-App-002** was a refined fixture with a proper PE uninstaller executable that deleted its own install directory. It successfully validated the uninstall subprocess flow but still produced only minimal residuals.

### Real Application Testing

The limitations of synthetic fixtures led to testing with real installed applications:

- **Freebuff**: Not established from available evidence whether this was tested.
- **Git for Windows**: Used for initial discovery and details validation.
- **Telegram Desktop**: Became the **principal end-to-end cleanup test** because after official uninstallation, it left behind:
  1. `C:\Users\test\AppData\Roaming\Telegram Desktop` (directory with `log_start0.txt`)
  2. A registry residual entry.

  Telegram was the only application that exercised the **full production path**: discovery → details → official uninstall → residual analysis → cleanup plan → preflight → backup → journal → recursive directory deletion → verification → history → history details.

---

## Real VM Evidence — Final Telegram Validation Sequence

1. **Application Discovery**: Telegram Desktop appeared in the Applications list after registry scan.
2. **View Details**: Application metadata (name, publisher, version, install location) displayed correctly.
3. **Official Uninstall**: Telegram's official uninstaller subprocess executed and completed.
4. **Post-Uninstall State**: `IsPresent` updated to `false`; uninstall button disabled; record retained.
5. **Residual Analysis**: Three engines (filesystem, registry, shortcut) executed concurrently.
6. **Two Residual Artifacts Detected**: AppData directory (`C:\Users\test\AppData\Roaming\Telegram Desktop`) and registry entry.
7. **Cleanup Plan**: Plan rendered with risk-scored artifacts.
8. **Low-Risk Selection**: AppData directory auto-selected (Low Risk, Application Owned). Registry key NOT auto-selected (High Risk).
9. **Preflight Authorization**: Selected directory passed canonical path, containment, reparse-point, and protection checks.
10. **Backup**: ZIP archive created; `Backup.SessionId` correctly referenced `UninstallSession.Id`; zero FK errors.
11. **Journal**: `TransactionJournalEntry` persisted with state `Pending` → `Executing` → `Committed`.
12. **Recursive Directory Deletion**: Safe recursive cleanup: read-only reset → file deletion → subdirectory deletion → root deletion.
13. **Verification**: `Directory.Exists` returned `false`. Independent verification:
    ```powershell
    Test-Path "C:\Users\test\AppData\Roaming\Telegram Desktop"
    # False
    ```
14. **Cleanup Result**: Success: 1, Failed: 0, Skipped: 0, Cancelled: 0.
15. **Finish & View History**: Button navigated to `HistoryViewModel` under a fresh isolated scope.
16. **History Rendering**: `HistoryViewModel` loaded 21 recent activities.
17. **History Details**: Details button resolved `ApplicationHistoryView` with populated timeline.
18. **Back Navigation**: Returned cleanly to `HistoryViewModel` without `ObjectDisposedException`.
19. **Process Stability**: Application remained alive with zero unhandled exceptions.
20. **No Error Logs**: No `[ERR]`, `[FTL]`, or fatal events in the final validated workflow.

---

## Security Lessons

### Principles Strengthened During Phase 5

1. **Fail-Closed Execution**: Every safety check defaults to **reject**. An artifact is only authorized if it explicitly passes all validation rules. This was intentionally NOT weakened when it caused false-positive rejections (Incident 7) — instead, the classification logic was refined to correctly identify application-owned paths.

2. **Canonical Paths**: All paths are normalized via `Path.GetFullPath()` before any comparison. This prevents `..` traversal attacks and case-sensitivity inconsistencies.

3. **Protected Roots**: System directories, user profile roots, and program file directories are protected at exact match. Their children are recursively protected unless they are explicitly application-owned subdirectories.

4. **Recursive Deletion Safety**: Blind `Directory.Delete(path, true)` was intentionally rejected. The safe recursive implementation validates each child for containment, reparse-point status, and protection before deletion.

5. **Reparse Point Handling**: Symlinks, junctions, and mount points are detected via `FileAttributes.ReparsePoint` and rejected. This prevents an attacker from placing a junction inside a residual directory that redirects deletion to a system directory.

6. **No Shell Execution Bypass**: `cmd.exe`, `powershell.exe`, and shell script execution are strictly blocked by `CommandParser`. Only validated PE executables are allowed as uninstall commands.

7. **Mandatory Backup**: No cleanup item executes without a persisted backup. The backup is created and verified before the transaction journal transitions to `Executing`.

8. **Transaction Journaling**: Every cleanup operation records state transitions in a journal. If the application crashes mid-cleanup, the startup recovery service can reconcile based on journal state.

9. **Identity Consistency**: `UninstallSession.Id` (the persisted database identity) is the authoritative reference for all downstream entities. Ephemeral in-memory identifiers (like `ResidualAnalysisSession.Id`) must never be used for FK relationships.

10. **Verification After Mutation**: After every destructive operation, the system asserts that the target no longer exists. This is not optimistic — it is a hard assertion that fails the operation if the target persists.

11. **No Automatic Deletion of High-Risk Residuals**: Registry keys and other high-risk artifacts are displayed to the user but NOT auto-selected in the cleanup plan. The user must explicitly choose to delete them.

### Decisions That Were NOT Weakened

- When `ValidateScopes = true` caused crashes (Incidents 5–6), the solution was to fix the scope architecture, not disable validation.
- When the protected path check falsely rejected application directories (Incident 7), the solution was to refine the classification hierarchy, not remove protection.
- When `Directory.Delete(path, false)` failed on non-empty directories (Incident 9), the solution was to implement safe recursive deletion, not use blind `Directory.Delete(path, true)`.

---

## Architectural Lessons

### Root vs. Scoped DI
Resolving scoped services (EF Core DbContext, repositories) from the root `IServiceProvider` violates the scope contract and causes either `InvalidOperationException` (with `ValidateScopes`) or silent DbContext sharing (without it). Every ViewModel that touches persistence must be resolved from a dedicated scope.

### Scope Ownership
The entity that **creates** a scope must own its **disposal**. Navigation scopes are owned by `NavigationService`. Cleanup workflow scopes are owned by `CleanupViewModelFactory`. History detail scopes are owned by `HistoryViewModelFactory`. Mixing ownership causes premature disposal.

### Operation Lifetime vs. Page Lifetime
A multi-step workflow (plan → execution → verification) spans multiple pages. Its service scope must outlive individual page transitions. The factory pattern solves this by decoupling scope lifetime from navigation lifetime.

### Persistence Identity
Database entities must reference the persisted identity (`UninstallSession.Id`) across the entire pipeline. Using ephemeral in-memory identifiers for FK relationships causes constraint violations that surface only at runtime with real data.

### ViewModel/View Mapping
WPF's implicit DataTemplate mechanism requires explicit `DataTemplate` declarations for **every** ViewModel that can appear in a `ContentControl`. Missing a single mapping causes silent degradation to CLR type-name rendering.

### Architecture Diagram

```
┌─────────────────────────────────────────────────┐
│                  WPF Application                │
│  ┌─────────────┐  ┌──────────────────────────┐  │
│  │ MainWindow  │  │      NavigationService    │  │
│  │ ContentCtrl │←→│ (scoped ViewModel mgmt)  │  │
│  └──────┬──────┘  └──────────┬───────────────┘  │
│         │                    │                   │
│  ┌──────▼──────────────────────────────────────┐│
│  │              ViewModels                     ││
│  │  Dashboard │ Applications │ Details         ││
│  │  CleanupPlan │ Execution │ History │ ...    ││
│  └──────┬──────────────────────────────────────┘│
│         │                                       │
│  ┌──────▼──────────────────────────────────────┐│
│  │          ViewModel Factories                ││
│  │  CleanupViewModelFactory (owns scope)       ││
│  │  HistoryViewModelFactory (owns scope)       ││
│  └──────┬──────────────────────────────────────┘│
└─────────┼───────────────────────────────────────┘
          │
┌─────────▼───────────────────────────────────────┐
│               Uninstaller.Core                  │
│  DiscoveryService │ UninstallService            │
│  ResidualAnalysisService │ CleanupPlanGenerator │
│  CleanupPreflightValidator │ EvidenceEngine     │
│  BackupService │ CleanupTransactionEngine       │
│  CommandParser │ StartupRecoveryService         │
└─────────┬───────────────────────────────────────┘
          │
┌─────────▼───────────────────────────────────────┐
│            Uninstaller.Windows                  │
│  WindowsRegistryService │ WindowsFileSystemSvc  │
│  WindowsShortcutService │ WindowsProcessExecutor│
│  WindowsFileCleanupExecutor                     │
│  WindowsRegistryCleanupExecutor                 │
└─────────┬───────────────────────────────────────┘
          │
┌─────────▼───────────────────────────────────────┐
│          Uninstaller.Infrastructure             │
│  AppDbContext (SQLite + WAL)                    │
│  ApplicationRepository │ SessionRepository     │
│  HistoryRepository │ ReconciliationRepository  │
│  ApplicationDeduplicator                        │
└─────────┬───────────────────────────────────────┘
          │
┌─────────▼───────────────────────────────────────┐
│            Uninstaller.Domain                   │
│  Application │ UninstallSession │ CleanupPlan  │
│  CleanupPlanItem │ Backup │ TransactionJournal │
│  Artifact │ Operation │ LogEntry              │
└─────────────────────────────────────────────────┘
```

---

## Test Evolution

Evidence-based test count milestones:

| Milestone | Tests | Context |
|:---|:---|:---|
| Phase 5K initial | 267 | After ApplicationDetails data-context fix |
| Phase 5K + parser tests | 270 | After UninstallServiceProductionPathTests |
| Phase 5G QA reset (0.22.0) | 278 | After E2E-App-002 fixture and command sync fix |
| Post deep-debug audit | 284 | After 19 findings resolved across all layers |
| Post DI scoping fix | ~287–289 | After NavigationService and factory scopes |
| Post DI validation expansion | ~294 | After DI validation and lifetime tests |
| Post cleanup safety pipeline | ~325–329 | After preflight, FK identity, and safety tests |
| Post recursive deletion + safety | ~333–337 | After WindowsFileCleanupExecutor recursive tests |
| Final (0.23.0) | **343** | After History navigation regression tests |

**Final**: 343 passed, 0 failed, 0 skipped.

---

## Final Defect Matrix

| ID | Defect | Severity | Root Cause | Fixed In | Regression Test | Real VM Verified |
|:---|:---|:---|:---|:---|:---|:---|
| 1 | Stale UninstallCommand | High | `??=` operator in deduplicator | `a876735` | ApplicationSynchronizationTests | Yes |
| 2 | Command parser File.Exists failure | High | Stale VM binary / env mismatch | `a876735` | UninstallServiceProductionPathTests (7) | Yes |
| 3 | Artifact version mismatch | Critical | No SHA verification process | Process fix | N/A (deployment) | Yes |
| 4 | Copy-VMFile 0x80070015 | Medium | Guest Service not operational | Adopted `\\tsclient` | N/A (infrastructure) | Yes |
| 5 | Scoped DI from root provider | Critical | NavigationService root resolution | `da4deac` | NavigationServiceTests, DI validation | Yes |
| 6 | ObjectDisposedException in cleanup | Critical | Navigation scope disposal | `6857602` | DI validation, cleanup pipeline | Yes |
| 7 | False Protected decision | Critical | Broad path containment | `a2aeb2f` | CleanupPreflightValidatorTests | Yes |
| 8 | SQLite FK constraint on Backup | Critical | ResidualAnalysisSession.Id used | `1cf45c2` | ProductionCleanupSafetyPipelineTests | Yes |
| 9 | DirectoryNotEmpty on cleanup | Critical | Non-recursive Directory.Delete | `574c7c5` | WindowsFileCleanupExecutorTests | Yes |
| 10 | Finish button no-op | High | Missing Command binding | `81c7d42` | ProductionCleanupSafetyPipelineTests | Yes |
| 11 | HistoryRepository EF query failure | High | Invalid Include sub-query | `81c7d42` | HistoryDetailsNavigationTests | Yes |
| 12 | CLR type name rendered | High | Missing DataTemplate mappings | `1366476` | HistoryDetailsNavigationTests (Req02-03) | Yes |
| 13 | Detail VMs not initialized | Medium | Missing auto-initialization | `1366476` | HistoryDetailsNavigationTests (Req01) | Yes |
| 14 | Instance navigation DataContext | Medium | Scope lifecycle on NavigateTo | `1366476` | HistoryDetailsNavigationTests (Req06) | Yes |

---

## Final Release History

| Version | Commit | Purpose | Status |
|:---|:---|:---|:---|
| 0.19.0 | (early Phase 5) | Initial production shell | Superseded |
| 0.21.0 | (Phase 5K) | ApplicationDetails data-context fix | Superseded |
| 0.21.1 | (Phase 5K fix2) | Parser logging injection | Superseded |
| 0.21.2 | (Phase 5K fix3) | Extended tracing for VM diagnosis | Superseded |
| 0.21.3 | (Phase 5G sync fix) | Command deduplicator fix | Superseded |
| 0.22.0 | `a876735` | Clean E2E-App-002 QA reset | Superseded |
| 0.23.0 | `1366476` | **Final validated release** | **RELEASE READY** |

Old VM binaries were rejected because SHA-256 verification showed they did not match the current source. The final release process requires:
1. Clean `git status`.
2. `git rev-parse HEAD` matches embedded `ProductVersion` SHA.
3. Host SHA-256 matches VM SHA-256.

---

## What Did Not Work

| Failed Approach | Lesson Learned |
|:---|:---|
| **Copy-VMFile** | Hyper-V Guest Service Interface is unreliable; Enhanced Session `\\tsclient` is robust. |
| **SMB share on VM** | Firewall and network isolation make SMB impractical for isolated test VMs. |
| **Relying on installed Program Files version** | Stale installed binaries mask code changes; always deploy from `publish-vm/`. |
| **Using stale binaries without hash verification** | SHA-256 comparison is mandatory; version numbers alone are insufficient. |
| **Blind recursive `Directory.Delete(path, true)`** | Unsafe — no child validation, no reparse-point defense, no containment check. |
| **Resolving scoped services from root provider** | Violates DI scope contract; must create explicit `IServiceScope` per operation. |
| **Keeping one navigation scope alive indefinitely** | Leaks DbContexts and services; scope must match ViewModel lifetime. |
| **Using `ResidualAnalysisSession.Id` as `UninstallSession.Id`** | Ephemeral IDs must never be used for FK relationships; persist first, reference second. |
| **Relying on missing WPF implicit DataTemplates** | Every ViewModel needs an explicit `DataTemplate`; WPF silently degrades to `.ToString()`. |
| **Treating passing unit tests as sufficient** | Unit tests with mocked dependencies do not catch DI lifecycle, EF translation, or filesystem interaction bugs. Real VM testing is irreplaceable. |

---

## Final Acceptance Criteria

All criteria met for Phase 5 completion:

- [x] Source: clean working tree (`git status --short` = empty)
- [x] Build: 0 errors, 0 warnings
- [x] Tests: 343 passed, 0 failed, 0 skipped
- [x] Release Identity: `0.23.0+1366476d37c34c92f94a609d82f59a50ef094074`
- [x] VM Hash Match: Host SHA-256 == VM SHA-256
- [x] Official Uninstall: Telegram subprocess completed
- [x] Residual Analysis: 2 artifacts detected
- [x] Cleanup Safety: Low-risk authorized, high-risk blocked, protected paths enforced
- [x] Backup: ZIP created, FK integrity maintained
- [x] Transaction Journal: State machine persisted
- [x] Recursive Deletion: Directory deleted, `Test-Path` = `False`
- [x] Verification: Target absent after cleanup
- [x] History: `HistoryViewModel` loaded, activities displayed
- [x] History Details: `ApplicationHistoryView` rendered with timeline
- [x] Runtime Stability: Zero unhandled exceptions

---

## Final Lessons Learned

1. **Unit tests were insufficient by themselves.** All 270+ unit tests passed before Phase 5 VM testing. The VM revealed 14 defects invisible to mocked test environments.

2. **Real VM testing found defects.** DI scope violations, EF translation errors, filesystem permission issues, and WPF DataTemplate gaps only manifest in integrated execution.

3. **Artifact identity matters.** Without SHA-256 verification, it is impossible to confirm the VM is running the correct binary. Version numbers can be stale or duplicated.

4. **Lifecycle ownership matters.** When one component creates a scope and another component disposes it, the services within that scope become unusable. Ownership must be explicit and documented.

5. **Persistence IDs must be explicit.** Using an ephemeral in-memory ID where a database FK is expected causes constraint violations that only surface with real data. The identity chain must be traced end-to-end.

6. **Safety rules must be shared.** When two components (EvidenceEngine and PreflightValidator) implement independent safety classification, they will inevitably disagree. A single authoritative safety model prevents conflicting decisions.

7. **UI and backend state must agree.** Showing an artifact as "Low Risk / Recommended" in the plan but rejecting it as "Protected" during preflight destroys user trust. The same logic must drive both decisions.

8. **Every destructive action needs verification.** After deleting a directory, `Directory.Exists` must return `false`. After persisting a backup, the FK chain must be valid. Optimistic assumptions are unacceptable for irreversible operations.

---

## Phase 5 Final Status

# **RELEASE READY**

| Property | Value |
|:---|:---|
| **Version** | `0.23.0` |
| **Git SHA** | `1366476d37c34c92f94a609d82f59a50ef094074` |
| **Test Count** | 343 passed, 0 failed, 0 skipped |
| **Build Warnings** | 0 |
| **Build Errors** | 0 |
| **Artifact** | `publish-vm/Uninstaller.App.exe` |
| **SHA-256** | `1E26B5B3D3CC08FA610030337F4F6C9C253664128D74567807725CE97F17D5FC` |
| **VM Result** | Full Telegram Desktop E2E workflow passed |

---

## Evidence / Source Material

### Repository Source Files
- `src/Uninstaller.App/App.xaml` — DataTemplate declarations
- `src/Uninstaller.App/MainWindow.xaml` — DataTemplate declarations
- `src/Uninstaller.App/Services/NavigationService.cs` — scoped navigation
- `src/Uninstaller.App/Services/CleanupViewModelFactory.cs` — cleanup scope factory
- `src/Uninstaller.App/Services/HistoryViewModelFactory.cs` — history scope factory
- `src/Uninstaller.App/ViewModels/CleanupExecutionViewModel.cs` — FinishCommand
- `src/Uninstaller.App/ViewModels/ApplicationHistoryViewModel.cs` — auto-initialization
- `src/Uninstaller.App/Views/CleanupExecutionView.xaml` — button binding
- `src/Uninstaller.Core/Services/CommandParser.cs` — diagnostic logging
- `src/Uninstaller.Core/Services/CleanupPreflightValidator.cs` — unified safety model
- `src/Uninstaller.Windows/Services/WindowsFileCleanupExecutor.cs` — recursive deletion
- `src/Uninstaller.Infrastructure/Services/ApplicationDeduplicator.cs` — merge fix
- `src/Uninstaller.Infrastructure/Persistence/Repositories/HistoryRepository.cs` — query fix
- `src/Uninstaller.Infrastructure/Persistence/AppDbContext.cs` — FK constraints

### Test Files
- `tests/Uninstaller.App.Tests/Navigation/HistoryDetailsNavigationTests.cs` — 6 regression tests
- `tests/Uninstaller.App.Tests/Navigation/NavigationServiceTests.cs` — scoped resolution tests
- `tests/Uninstaller.App.Tests/ProductionCleanupSafetyPipelineTests.cs` — 22 production pipeline tests
- `tests/Uninstaller.App.Tests/DependencyInjectionValidationTests.cs` — DI container validation
- `tests/Uninstaller.Core.Tests/Services/UninstallServiceProductionPathTests.cs` — parser pipeline tests

### Git Commits (chronological, Phase 5 relevant)
- `c24a490` 2026-08-27 — Production application shell
- `16d9253` 2026-08-27 — Cleanup plan review UI
- `94b13c2` 2026-08-27 — Cleanup execution UI
- `398982a` 2026-08-28 — History and audit timeline
- `12e6be2` 2026-08-28 — Recovery experience
- `6a16fc4` 2026-08-29 — Crash and interruption recovery
- `5529c76` 2026-08-29 — Packaging and installer
- `99f0964` 2026-08-29 — Release hardening
- `da5e7be` 2026-08-29 — Security and readiness audit
- `a876735` 2026-08-30 — Sync command deduplicator fix (Incidents 1–2)
- `bee3faf` 2026-08-31 — 19 deep debug findings resolved
- `c4612ad` 2026-08-31 — Post-uninstall residual analysis workflow
- `da4deac` 2026-08-31 — Scoped navigation context (Incident 5)
- `6857602` 2026-08-31 — Dedicated execution scope lifecycle (Incident 6)
- `a2aeb2f` 2026-08-31 — False protected decision fix (Incident 7)
- `1cf45c2` 2026-08-31 — SQLite FK constraint fix (Incident 8)
- `574c7c5` 2026-08-31 — Safe recursive directory cleanup (Incident 9)
- `81c7d42` 2026-08-31 — Finish button, History navigation, EF query fix (Incidents 10–11)
- `1366476` 2026-08-31 — DataTemplate mappings, auto-initialization, diagnostic logging (Incidents 12–14)

### QA Artifacts (conversation brain)
- `phase_5k_report.md` — Phase 5K blocker report
- `phase_5k_fix2_report.md` — Parser logging injection report
- `phase_5g_tracing_report.md` — Official uninstall validation trace
- `phase_5g_sync_fix_report.md` — Stale uninstall command fix
- `phase_5g_qa_reset_report.md` — Complete E2E QA reset
- `e2e_002_fixture_report.md` — E2E-App-002 fixture setup
- `deep_debug_audit.md` — Exhaustive deep-dive debug audit
- `full_software_forensic_report.md` — Full software forensic report
- `phase_5g_final_release_report.md` — Final release QA report
- `qa/forensic-audit.json` — Machine-readable release metadata
