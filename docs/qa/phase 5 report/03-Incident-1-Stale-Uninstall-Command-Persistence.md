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
