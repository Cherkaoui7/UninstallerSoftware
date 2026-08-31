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
