## Final Defect Matrix

| ID | Defect | Severity | Root Cause | Fixed In | Regression Test | Real VM Verified |
|:---|:---|:---|:---|:---|:---|:---|
| 1 | Stale UninstallCommand | High | `??=` operator in deduplicator | `a876735` | ApplicationSynchronizationTests | Yes |
| 2 | Command parser File.Exists failure | High | Stale VM binary / env mismatch | `a876735` | UninstallServiceProductionPathTests (7) | Yes |
| 3 | Artifact version mismatch | Critical | No SHA verification process | Process fix | N/A (deployment) | Yes |
| 4 | Copy-VMFile 0x80070015 | Medium | Guest Service not operational | Adopted `\\tsclient` | N/A (infrastructure) | Yes |
| 5 | Scoped DI from root provider | Critical | NavigationService root resolution | `da4deac` | NavigationServiceTests, DI validation | Yes |
| 6 | ObjectDisposedException in cleanup | Critical | Navigation scope disposal | `6857602` | DI validation, cleanup pipeline | Yes |
| 7 | False Protected decision | Critical | Broad path containment | `a2aeb2f` | CleanupPreflightValidatorTests | Yes |
| 8 | SQLite FK constraint on Backup | Critical | ResidualAnalysisSession.Id used | `1cf45c2` | ProductionCleanupSafetyPipelineTests | Yes |
| 9 | DirectoryNotEmpty on cleanup | Critical | Non-recursive Directory.Delete | `574c7c5` | WindowsFileCleanupExecutorTests | Yes |
| 10 | Finish button no-op | High | Missing Command binding | `81c7d42` | ProductionCleanupSafetyPipelineTests | Yes |
| 11 | HistoryRepository EF query failure | High | Invalid Include sub-query | `81c7d42` | HistoryDetailsNavigationTests | Yes |
| 12 | CLR type name rendered | High | Missing DataTemplate mappings | `1366476` | HistoryDetailsNavigationTests (Req02-03) | Yes |
| 13 | Detail VMs not initialized | Medium | Missing auto-initialization | `1366476` | HistoryDetailsNavigationTests (Req01) | Yes |
| 14 | Instance navigation DataContext | Medium | Scope lifecycle on NavigateTo | `1366476` | HistoryDetailsNavigationTests (Req06) | Yes |

---
