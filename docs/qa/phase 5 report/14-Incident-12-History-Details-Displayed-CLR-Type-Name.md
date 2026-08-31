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
