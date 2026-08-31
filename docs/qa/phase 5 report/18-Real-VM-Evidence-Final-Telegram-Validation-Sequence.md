## Real VM Evidence — Final Telegram Validation Sequence

1. **Application Discovery**: Telegram Desktop appeared in the Applications list after registry scan.
2. **View Details**: Application metadata (name, publisher, version, install location) displayed correctly.
3. **Official Uninstall**: Telegram's official uninstaller subprocess executed and completed.
4. **Post-Uninstall State**: `IsPresent` updated to `false`; uninstall button disabled; record retained.
5. **Residual Analysis**: Three engines (filesystem, registry, shortcut) executed concurrently.
6. **Two Residual Artifacts Detected**: AppData directory (`C:\Users\test\AppData\Roaming\Telegram Desktop`) and registry entry.
7. **Cleanup Plan**: Plan rendered with risk-scored artifacts.
8. **Low-Risk Selection**: AppData directory auto-selected (Low Risk, Application Owned). Registry key NOT auto-selected (High Risk).
9. **Preflight Authorization**: Selected directory passed canonical path, containment, reparse-point, and protection checks.
10. **Backup**: ZIP archive created; `Backup.SessionId` correctly referenced `UninstallSession.Id`; zero FK errors.
11. **Journal**: `TransactionJournalEntry` persisted with state `Pending` → `Executing` → `Committed`.
12. **Recursive Directory Deletion**: Safe recursive cleanup: read-only reset → file deletion → subdirectory deletion → root deletion.
13. **Verification**: `Directory.Exists` returned `false`. Independent verification:
    ```powershell
    Test-Path "C:\Users\test\AppData\Roaming\Telegram Desktop"
    # False
    ```
14. **Cleanup Result**: Success: 1, Failed: 0, Skipped: 0, Cancelled: 0.
15. **Finish & View History**: Button navigated to `HistoryViewModel` under a fresh isolated scope.
16. **History Rendering**: `HistoryViewModel` loaded 21 recent activities.
17. **History Details**: Details button resolved `ApplicationHistoryView` with populated timeline.
18. **Back Navigation**: Returned cleanly to `HistoryViewModel` without `ObjectDisposedException`.
19. **Process Stability**: Application remained alive with zero unhandled exceptions.
20. **No Error Logs**: No `[ERR]`, `[FTL]`, or fatal events in the final validated workflow.

---
