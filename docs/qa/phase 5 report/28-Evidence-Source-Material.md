## Evidence / Source Material

### Repository Source Files
- `src/Uninstaller.App/App.xaml` — DataTemplate declarations
- `src/Uninstaller.App/MainWindow.xaml` — DataTemplate declarations
- `src/Uninstaller.App/Services/NavigationService.cs` — scoped navigation
- `src/Uninstaller.App/Services/CleanupViewModelFactory.cs` — cleanup scope factory
- `src/Uninstaller.App/Services/HistoryViewModelFactory.cs` — history scope factory
- `src/Uninstaller.App/ViewModels/CleanupExecutionViewModel.cs` — FinishCommand
- `src/Uninstaller.App/ViewModels/ApplicationHistoryViewModel.cs` — auto-initialization
- `src/Uninstaller.App/Views/CleanupExecutionView.xaml` — button binding
- `src/Uninstaller.Core/Services/CommandParser.cs` — diagnostic logging
- `src/Uninstaller.Core/Services/CleanupPreflightValidator.cs` — unified safety model
- `src/Uninstaller.Windows/Services/WindowsFileCleanupExecutor.cs` — recursive deletion
- `src/Uninstaller.Infrastructure/Services/ApplicationDeduplicator.cs` — merge fix
- `src/Uninstaller.Infrastructure/Persistence/Repositories/HistoryRepository.cs` — query fix
- `src/Uninstaller.Infrastructure/Persistence/AppDbContext.cs` — FK constraints

### Test Files
- `tests/Uninstaller.App.Tests/Navigation/HistoryDetailsNavigationTests.cs` — 6 regression tests
- `tests/Uninstaller.App.Tests/Navigation/NavigationServiceTests.cs` — scoped resolution tests
- `tests/Uninstaller.App.Tests/ProductionCleanupSafetyPipelineTests.cs` — 22 production pipeline tests
- `tests/Uninstaller.App.Tests/DependencyInjectionValidationTests.cs` — DI container validation
- `tests/Uninstaller.Core.Tests/Services/UninstallServiceProductionPathTests.cs` — parser pipeline tests

### Git Commits (chronological, Phase 5 relevant)
- `c24a490` 2026-08-27 — Production application shell
- `16d9253` 2026-08-27 — Cleanup plan review UI
- `94b13c2` 2026-08-27 — Cleanup execution UI
- `398982a` 2026-08-28 — History and audit timeline
- `12e6be2` 2026-08-28 — Recovery experience
- `6a16fc4` 2026-08-29 — Crash and interruption recovery
- `5529c76` 2026-08-29 — Packaging and installer
- `99f0964` 2026-08-29 — Release hardening
- `da5e7be` 2026-08-29 — Security and readiness audit
- `a876735` 2026-08-30 — Sync command deduplicator fix (Incidents 1–2)
- `bee3faf` 2026-08-31 — 19 deep debug findings resolved
- `c4612ad` 2026-08-31 — Post-uninstall residual analysis workflow
- `da4deac` 2026-08-31 — Scoped navigation context (Incident 5)
- `6857602` 2026-08-31 — Dedicated execution scope lifecycle (Incident 6)
- `a2aeb2f` 2026-08-31 — False protected decision fix (Incident 7)
- `1cf45c2` 2026-08-31 — SQLite FK constraint fix (Incident 8)
- `574c7c5` 2026-08-31 — Safe recursive directory cleanup (Incident 9)
- `81c7d42` 2026-08-31 — Finish button, History navigation, EF query fix (Incidents 10–11)
- `1366476` 2026-08-31 — DataTemplate mappings, auto-initialization, diagnostic logging (Incidents 12–14)

### QA Artifacts (conversation brain)
- `phase_5k_report.md` — Phase 5K blocker report
- `phase_5k_fix2_report.md` — Parser logging injection report
- `phase_5g_tracing_report.md` — Official uninstall validation trace
- `phase_5g_sync_fix_report.md` — Stale uninstall command fix
- `phase_5g_qa_reset_report.md` — Complete E2E QA reset
- `e2e_002_fixture_report.md` — E2E-App-002 fixture setup
- `deep_debug_audit.md` — Exhaustive deep-dive debug audit
- `full_software_forensic_report.md` — Full software forensic report
- `phase_5g_final_release_report.md` — Final release QA report
- `qa/forensic-audit.json` — Machine-readable release metadata
