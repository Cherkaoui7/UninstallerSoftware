## Security Lessons

### Principles Strengthened During Phase 5

1. **Fail-Closed Execution**: Every safety check defaults to **reject**. An artifact is only authorized if it explicitly passes all validation rules. This was intentionally NOT weakened when it caused false-positive rejections (Incident 7) — instead, the classification logic was refined to correctly identify application-owned paths.

2. **Canonical Paths**: All paths are normalized via `Path.GetFullPath()` before any comparison. This prevents `..` traversal attacks and case-sensitivity inconsistencies.

3. **Protected Roots**: System directories, user profile roots, and program file directories are protected at exact match. Their children are recursively protected unless they are explicitly application-owned subdirectories.

4. **Recursive Deletion Safety**: Blind `Directory.Delete(path, true)` was intentionally rejected. The safe recursive implementation validates each child for containment, reparse-point status, and protection before deletion.

5. **Reparse Point Handling**: Symlinks, junctions, and mount points are detected via `FileAttributes.ReparsePoint` and rejected. This prevents an attacker from placing a junction inside a residual directory that redirects deletion to a system directory.

6. **No Shell Execution Bypass**: `cmd.exe`, `powershell.exe`, and shell script execution are strictly blocked by `CommandParser`. Only validated PE executables are allowed as uninstall commands.

7. **Mandatory Backup**: No cleanup item executes without a persisted backup. The backup is created and verified before the transaction journal transitions to `Executing`.

8. **Transaction Journaling**: Every cleanup operation records state transitions in a journal. If the application crashes mid-cleanup, the startup recovery service can reconcile based on journal state.

9. **Identity Consistency**: `UninstallSession.Id` (the persisted database identity) is the authoritative reference for all downstream entities. Ephemeral in-memory identifiers (like `ResidualAnalysisSession.Id`) must never be used for FK relationships.

10. **Verification After Mutation**: After every destructive operation, the system asserts that the target no longer exists. This is not optimistic — it is a hard assertion that fails the operation if the target persists.

11. **No Automatic Deletion of High-Risk Residuals**: Registry keys and other high-risk artifacts are displayed to the user but NOT auto-selected in the cleanup plan. The user must explicitly choose to delete them.

### Decisions That Were NOT Weakened

- When `ValidateScopes = true` caused crashes (Incidents 5–6), the solution was to fix the scope architecture, not disable validation.
- When the protected path check falsely rejected application directories (Incident 7), the solution was to refine the classification hierarchy, not remove protection.
- When `Directory.Delete(path, false)` failed on non-empty directories (Incident 9), the solution was to implement safe recursive deletion, not use blind `Directory.Delete(path, true)`.

---
