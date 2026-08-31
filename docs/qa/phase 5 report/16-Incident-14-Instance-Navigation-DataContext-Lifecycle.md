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
