# Phase 5: Backup & Rollback Mechanism

## Overview
Phase 5 implements the safety net. Before any destructive operations are performed in Phase 4, the system must create a backup of the resources slated for deletion. The entire uninstall process is treated as a transaction, allowing for partial or full rollback if something goes critically wrong.

## 1. The Transaction Engine
The uninstall process is managed via a state machine:
*   `Created` -> `Scanning` -> `Planning` -> `WaitingForConfirmation` -> `BackingUp` -> `Executing` -> `Verifying` -> `Completed`
*   If an error occurs during execution, the state transitions to `Rollback` and finally `RolledBack` or `Failed`.

## 2. Backup Procedures
*   **Registry:** Export affected registry keys to a `.reg` file or store them directly in the SQLite database before deletion.
*   **Files:** Instead of permanent deletion, move files into a designated quarantine/recovery directory (e.g., `C:\ProgramData\Uninstaller\Recovery\{SessionId}`).
*   **System Restore Point:** Optionally, create a Windows System Restore point before beginning the cleanup.

## 3. Rollback Execution
If a user requests a rollback (via the History/Recovery UI), or if a critical failure occurs mid-transaction:
*   Read the metadata store for the specific `BackupId`.
*   Restore files from the recovery location back to their `OriginalPath`.
*   Re-import the exported registry keys.
*   Note: Rollback cannot be guaranteed for services or deeply integrated drivers.

## 4. Acceptance Criteria
*   Files are successfully moved to a recovery location before deletion.
*   Registry keys are backed up.
*   Simulated failures trigger a rollback that successfully restores previously modified resources.
*   The UI provides a "History & Recovery" tab to view past operations and trigger rollbacks.
