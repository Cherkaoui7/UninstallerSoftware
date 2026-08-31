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
