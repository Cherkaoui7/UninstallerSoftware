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
