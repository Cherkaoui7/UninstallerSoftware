# Uninstaller — Exhaustive Deep-Dive Debug Audit

**Date:** 2026-08-31  
**Scope:** All production source across Domain, Core, Infrastructure, Windows, and App layers  
**Files Audited:** 30+ source files across 5 projects  

---

## Severity Legend

| Severity | Meaning |
|---|---|
| 🔴 **CRITICAL** | Will crash at runtime or silently corrupt data under reachable conditions |
| 🟠 **HIGH** | Incorrect behavior visible to the user, data loss risk, or security gap |
| 🟡 **MEDIUM** | Latent defect that surfaces under stress, concurrency, or edge-case inputs |
| 🟢 **LOW** | Code smell, dead code, or minor inconsistency |

---

## 🔴 CRITICAL Findings

### C01 — `BackupService.GetBackupAsync` Throws `NotImplementedException`

**File:** [BackupService.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/BackupService.cs#L222-L226)  
**Severity:** 🔴 CRITICAL  

```csharp
public Task<Backup?> GetBackupAsync(Guid backupId, CancellationToken cancellationToken = default)
{
    throw new NotImplementedException();
}
```

**Impact:** The entire **Recovery subsystem** is broken. `RecoveryTransactionEngine.ExecuteAsync` calls `_backupService.GetBackupAsync(item.BackupArtifactId)` at [line 61](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/RecoveryTransactionEngine.cs#L61). Every recovery attempt will throw `NotImplementedException`, crash the engine, and leave the user with no way to undo a cleanup operation.

Additionally, **`StartupRecoveryService.ReconcileRecoveryAsync`** calls `_backupService.GetBackupAsync(entry.ItemId)` at [line 111](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/StartupRecoveryService.cs#L111). This means if a recovery transaction was interrupted, the application will crash on next startup during reconciliation.

**Fix:** Implement `GetBackupAsync` with a proper persistence lookup (database or manifest file).

---

### C02 — DI Lifetime Conflict: Scoped Services Injected into Singleton Hosts

**File:** [App.xaml.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.App/App.xaml.cs#L26-L28) + [DependencyInjection.cs (Core)](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/DependencyInjection.cs#L18) + [DependencyInjection.cs (Infra)](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Infrastructure/DependencyInjection.cs#L17-L21)

**Severity:** 🔴 CRITICAL  

The `IItemExecutionTracker` is registered as **Scoped** in `AddCore()` (line 18), but `App.xaml.cs` immediately overrides it with a **Singleton** `ObservableItemExecutionTracker` (line 28). The Singleton tracker is correct for the App, but the `ICleanupTransactionEngine` that *consumes* the tracker is registered as **Scoped** (line 19). This means:

1. The `CleanupTransactionEngine` (Scoped) captures the `IItemExecutionTracker` from the root provider (Singleton).
2. But `AppDbContext` (Scoped) and repositories (Scoped) are resolved per-scope.
3. If any ViewModels or services resolve `ICleanupTransactionEngine` without an explicit scope, **EF Core will throw** because the DbContext is disposed or shared across threads.

Additionally, the `IRecoveryItemExecutionTracker` follows the same pattern (line 32).

**Fix:** Either:
- Make `CleanupTransactionEngine` Singleton (if stateless), or
- Ensure all ViewModel commands that use it create an explicit `IServiceScope` and resolve from the scope.

---

### C03 — Double-Add of `CleanupExecutionResult` on Executor Exception

**File:** [CleanupTransactionEngine.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/CleanupTransactionEngine.cs#L179-L211)  
**Severity:** 🔴 CRITICAL  

```csharp
var executionResult = new CleanupExecutionResult { ItemId = item.Id };  // line 69

// ...inside try:
var execResult = await executor.ExecuteAsync(context, CancellationToken.None);  // line 185
result.Results.Add(execResult);  // line 186 — adds the real result

// ...inside catch:
executionResult.Outcome = CleanupOutcome.DeleteFailed;  // line 207
result.Results.Add(executionResult);  // line 209 — adds the ORIGINAL stub as well
```

When an executor **succeeds** (line 186) and then a subsequent item **throws** (line 203), the outer `executionResult` stub created at line 69 has `Outcome = default` (which is likely `CleanupOutcome.Unknown` or `0`). If the executor itself throws, **both** the stub AND the real result are added, causing duplicate entries in `Results`. The `ReconcileResult` method in `CleanupExecutionViewModel` then maps both, potentially overwriting the real outcome with the stub.

**Fix:** Restructure to use a single result object, or guard the catch block from adding the stub if the real result was already added.

---

## 🟠 HIGH Findings

### H01 — `CleanupPreflightValidator.ValidateAsync` Defaults `Outcome` to `Authorized`

**File:** [CleanupPreflightValidator.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/CleanupPreflightValidator.cs#L31-L38)  
**Severity:** 🟠 HIGH  

```csharp
var result = new PreflightValidationResult
{
    IsValid = true,
    IsAuthorized = false,         // starts false...
    Outcome = PreflightValidationOutcome.Authorized,  // ...but Outcome starts Authorized!
    ArtifactStillMatches = true,
    ApplicationStillMatches = true,
    PlanItemStillValid = true
};
```

The `Outcome` is initialized to `Authorized` and only overwritten by explicit `Reject()` calls. If a new `ArtifactType` is added to the enum without a corresponding `case` in the `switch`, or if the default case is somehow bypassed, the validator will return `Authorized` for an artifact it never actually validated.

**Fix:** Initialize `Outcome` to a safe default (e.g., `PreflightValidationOutcome.ValidationError`) and set it to `Authorized` only after all checks pass.

---

### H02 — `UninstallService` State Machine Allows Invalid Transition to `Created`

**File:** [UninstallService.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/UninstallService.cs#L194)  
**Severity:** 🟠 HIGH  

```csharp
(_, UninstallSessionStatus.Created) => true,
```

This transition rule allows **any** status to transition back to `Created`, which breaks the state machine invariant. A session that has already `Failed` or `Completed` could be illegally reset to `Created`.

**Fix:** Remove this wildcard rule or restrict it to only transition from `None`/initial states.

---

### H03 — `CleanupTransactionEngine` Missing Final Status When All Items Skip/Fail

**File:** [CleanupTransactionEngine.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/CleanupTransactionEngine.cs#L214-L228)  
**Severity:** 🟠 HIGH  

```csharp
if (result.FailureCount == 0 && result.SkippedCount == 0 && result.SuccessCount > 0)
{
    result.Status = CleanupSessionStatus.Completed;
}
else if (result.FailureCount > 0 || result.SkippedCount > 0)
{
    result.Status = CleanupSessionStatus.CompletedWithFailures;
}
else
{
    result.Status = CleanupSessionStatus.Completed; // ← WRONG
}
```

The `else` branch (line 226) is reached when `FailureCount == 0 && SkippedCount == 0 && SuccessCount == 0`. This means **zero items were processed**, yet the status is `Completed`. This can happen when `selectedItems` is empty after LINQ filtering (even though there's an early return for `Count == 0`, the same logic applies if all items get cancelled individually via the cancellation checks but the `break` doesn't fire for the first item).

**Fix:** Set the else branch to `CleanupSessionStatus.CompletedWithFailures` or add explicit handling.

---

### H04 — `RecoveryTransactionEngine` Same Empty-Session Completed Bug

**File:** [RecoveryTransactionEngine.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/RecoveryTransactionEngine.cs#L167-L181)  
**Severity:** 🟠 HIGH  

Identical pattern to H03 — zero processed items yields `RecoverySessionStatus.Completed`.

---

### H05 — `ApplicationsViewModel.InitializeAsync` Never Called Automatically

**File:** [ApplicationsViewModel.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.App/ViewModels/ApplicationsViewModel.cs#L67-L86)  
**Severity:** 🟠 HIGH  

`InitializeAsync()` is a public method that loads applications from the repository. However, it is never called from the constructor, from `NavigationService`, or from any lifecycle hook. The user navigates to the Applications view and sees an **empty list** until they manually click "Scan". This creates a confusing first-use experience. The `NavigationService.NavigateTo<T>()` does not call any initialization hook on the resolved ViewModel.

**Fix:** Either call `InitializeAsync()` in the constructor (fire-and-forget with proper error handling), or implement an `INavigationAware` interface that `NavigationService` calls.

---

### H06 — `RecoveryTransactionEngine` Uses Wrong Item ID for Journal

**File:** [RecoveryTransactionEngine.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/RecoveryTransactionEngine.cs#L189)  
**Severity:** 🟠 HIGH  

```csharp
await _journal.RecordStateAsync(sessionId, item.BackupArtifactId, TransactionType.Recovery, state.ToString(), cancellationToken);
```

The journal records `item.BackupArtifactId` as the item ID. But `StartupRecoveryService.ReconcileRecoveryAsync` then calls `_backupService.GetBackupAsync(entry.ItemId)` where `entry.ItemId` is the value recorded in the journal. This means the journal stores the Backup's ID, and the reconciliation correctly retrieves the backup by that ID — **but** the `UpdateStateAsync` on the same line also calls `_executionTracker.UpdateStateAsync(item.Id, state)` using the **RecoveryItem's** ID. These two different IDs may cause the UI tracker to fire events with `RecoveryItem.Id` while the journal tracks `BackupArtifactId`, creating a mismatch if anyone tries to correlate journal entries with UI state.

---

## 🟡 MEDIUM Findings

### M01 — `CommandParser` `.exe` Heuristic Can Select Wrong Extension

**File:** [CommandParser.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/CommandParser.cs#L134-L147)  
**Severity:** 🟡 MEDIUM  

```csharp
var exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
```

`IndexOf(".exe")` returns the **first** occurrence. For a command like `C:\Program Files\SomeApp.exe\helper.exe /uninstall`, it would extract `C:\Program Files\SomeApp.exe` and miss the actual executable. Similarly, a path like `C:\AppData\executable_data.exefiles\uninstall.exe` would be cut at the wrong `.exe`.

**Fix:** Find the **last** `.exe` occurrence, or implement smarter boundary detection.

---

### M02 — `EvidenceEngine` Path Protection Check Is Incomplete

**File:** [EvidenceEngine.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/EvidenceEngine.cs#L12-L20)  
**Severity:** 🟡 MEDIUM  

The `ProtectedPaths` array only contains relative folder names (`Documents`, `Downloads`, etc.) but does not include:
- `%APPDATA%\Microsoft` (Outlook, Edge data)
- `%LOCALAPPDATA%\Microsoft` (Windows Store apps)
- `%PROGRAMDATA%` system-wide config
- `%WINDIR%`, `%SYSTEMROOT%` (already covered by path resolver, but not by evidence engine)

The evidence engine's protection check is a secondary defense layer that could miss some important user-data paths.

---

### M03 — `DiscoveryService` Double-Completes Result

**File:** [DiscoveryService.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/DiscoveryService.cs#L84-L112)  
**Severity:** 🟡 MEDIUM  

When `result.Cancelled` is true at line 82, `CompleteResult(result)` is called at line 84 (return), **and** it's called again in the `finally` block at line 111. The `DiscoveryCompletedAt` timestamp is overwritten twice, but more importantly, the `finally` block's call to `CompleteResult` on the early-return path creates confusion about whether the result was truly completed or cancelled.

---

### M04 — `CleanupExecutionViewModel` Tracker Race Condition

**File:** [CleanupExecutionViewModel.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.App/ViewModels/CleanupExecutionViewModel.cs#L129-L141)  
**Severity:** 🟡 MEDIUM  

```csharp
System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
{
    var item = Items.FirstOrDefault(i => i.Id == e.ItemId);
```

`InvokeAsync` is non-blocking. The `ReconcileResult` method at line 158 then overwrites all counters synchronously. If a queued `InvokeAsync` fires **after** `ReconcileResult`, it will call `UpdateCounters()` and overwrite the authoritative final counts with stale intermediate state. Same issue exists in `RecoverySessionViewModel`.

---

### M05 — `NavigationService` Does Not Dispose Previous ViewModel

**File:** [NavigationService.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.App/Services/NavigationService.cs#L30-L33)  
**Severity:** 🟡 MEDIUM  

```csharp
public void NavigateTo(ObservableObject viewModel)
{
    CurrentViewModel = viewModel;  // old VM is simply abandoned
}
```

`CleanupExecutionViewModel` and `RecoverySessionViewModel` implement `IDisposable`. When the user navigates away, the old ViewModel is never disposed, leaking event subscriptions, CancellationTokenSources, and potentially holding references to large object graphs.

---

### M06 — `StartupRecoveryService.CheckArtifactExists` for `RegistryValue` Always Returns True

**File:** [StartupRecoveryService.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/StartupRecoveryService.cs#L139)  
**Severity:** 🟡 MEDIUM  

```csharp
ArtifactType.RegistryValue => true, // RegistryService doesn't have ValueExists yet.
```

This means a registry value that was successfully deleted during cleanup will be reconciled as "still exists" → `Failed`, even though the cleanup actually succeeded. This causes false reconciliation failures for registry value artifacts.

---

## 🟢 LOW Findings

### L01 — Dead `Class1.cs` Files in Domain, Core, Infrastructure, Windows

**Files:**  
- [Domain/Class1.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Domain/Class1.cs)  
- [Core/Class1.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Class1.cs)  
- [Infrastructure/Class1.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Infrastructure/Class1.cs)  
- [Windows/Class1.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Windows/Class1.cs)  

Template-generated files from `dotnet new`. Should be deleted.

---

### L02 — `CleanupPlanViewModel` Uses `ApplicationName` from Entity, Not from ViewModel

**File:** [CleanupPlanViewModel.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.App/ViewModels/CleanupPlanViewModel.cs#L111)  
**Severity:** 🟢 LOW  

`ApplicationName` is set from `_application.Name` which can be `null` (the entity's `Name` property defaults to `string.Empty` but was historically nullable in test scenarios). The `??` fallback to `string.Empty` is correct but the field `_applicationName` has a default of `string.Empty` anyway, so this is harmless but redundant.

---

### L03 — `ViewModelBase` Constructor Sets `UIState.Idle`, Then Subclass Constructors Override to `Ready`

**File:** [ViewModelBase.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.App/ViewModels/ViewModelBase.cs#L15-L16)  
**Severity:** 🟢 LOW  

The base constructor sets `State = UIState.Idle`, then every subclass immediately sets `State = UIState.Ready`. This generates a spurious `PropertyChanged` event during construction. Harmless but wasteful.

---

### L04 — `CommandParser` Logs Raw Uninstall Command at Information Level

**File:** [CommandParser.cs](file:///C:/Users/USER/Documents/Uninstaller/src/Uninstaller.Core/Services/CommandParser.cs#L34)  
**Severity:** 🟢 LOW  

```csharp
_logger.LogInformation("App {AppName}: Parsing raw command: {Command}. IsQuiet: {IsQuiet}", ...);
```

Logging raw uninstall commands at `Information` level may expose sensitive paths or GUIDs in production logs. Should be `Debug`.

---

## Summary

| Severity | Count | Key Themes |
|---|---|---|
| 🔴 **CRITICAL** | 3 | Recovery completely broken (`NotImplementedException`), DI lifetime mismatch, duplicate result entries |
| 🟠 **HIGH** | 6 | Preflight defaults to Authorized, state machine hole, empty-session false "Completed", missing auto-load, journal ID mismatch |
| 🟡 **MEDIUM** | 6 | Command parser edge cases, evidence gaps, race conditions, resource leaks, false reconciliation |
| 🟢 **LOW** | 4 | Dead code, redundant defaults, sensitive logging |
| **Total** | **19** | |

---

## Recommended Fix Priority

> [!CAUTION]
> **C01** (`GetBackupAsync` → `NotImplementedException`) is the most dangerous finding. It makes the entire Recovery subsystem and startup reconciliation for recovery transactions completely non-functional. Any user who performs a cleanup and then tries to recover will hit an unhandled exception.

1. **C01** — Implement `GetBackupAsync` with database/manifest lookup
2. **C03** — Fix double-add of `CleanupExecutionResult` on exception
3. **C02** — Audit DI lifetimes; ensure scoped services aren't captured by singletons
4. **H01** — Change preflight default to `ValidationError` instead of `Authorized`
5. **H02** — Remove wildcard `Created` transition
6. **M05** — Dispose previous ViewModel on navigation
7. **H05** — Auto-load applications on navigation to Applications view
8. All remaining items in severity order
