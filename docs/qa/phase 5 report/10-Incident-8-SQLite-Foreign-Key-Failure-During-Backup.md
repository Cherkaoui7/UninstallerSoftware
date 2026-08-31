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
