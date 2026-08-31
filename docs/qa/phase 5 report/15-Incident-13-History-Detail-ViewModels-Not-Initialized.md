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
