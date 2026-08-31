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
