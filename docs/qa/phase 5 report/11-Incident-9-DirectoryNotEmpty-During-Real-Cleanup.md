### Incident 9 — DirectoryNotEmpty During Real Cleanup

**Date**: ~2026-08-31 (commit `574c7c5`)

#### Symptom
After successful preflight, backup, and journal initialization, the cleanup executor failed:
> `DirectoryNotEmpty: 'C:\Users\test\AppData\Roaming\Telegram Desktop'`

Independent verification showed the directory still existed and contained `log_start0.txt`.

#### Trigger
Executing cleanup on the Telegram Desktop residual directory.

#### Exact Evidence
The `WindowsFileCleanupExecutor` called `Directory.Delete(path, recursive: false)`, which fails if the directory is not empty. The directory contained `log_start0.txt`, a log file created by Telegram Desktop.

Running on the VM:
```powershell
Test-Path "C:\Users\test\AppData\Roaming\Telegram Desktop"
# True

Get-ChildItem "C:\Users\test\AppData\Roaming\Telegram Desktop"
# log_start0.txt
```

#### Root Cause
`Directory.Delete(path, false)` only deletes empty directories. The cleanup executor did not implement recursive deletion because blind `Directory.Delete(path, true)` was considered unsafe — it could delete unexpected content without verification.

#### Technical Analysis
Simply switching to `Directory.Delete(path, true)` was rejected as unsafe because:
1. It provides no opportunity to validate each child before deletion.
2. It cannot detect reparse points (symlinks/junctions) that could redirect deletion to unintended locations.
3. It does not verify containment — a symlink inside the directory could point outside the authorized tree.

#### Files / Components Involved
- `src/Uninstaller.Windows/Services/WindowsFileCleanupExecutor.cs`

#### Resolution
Implemented a **safe recursive cleanup** algorithm:
1. **Enumerate children** — list all files and subdirectories.
2. **Canonical containment check** — verify each child's canonical path is strictly contained within the authorized parent path.
3. **Reparse point defense** — reject any child that is a reparse point (symlink, junction, or mount point).
4. **Read-only attribute reset** — clear read-only flags on files before deletion.
5. **Bottom-up deletion** — delete files first, then empty subdirectories, then the root directory.
6. **Post-delete verification** — assert `!Directory.Exists(path)` after deletion.

#### Regression Tests
- `WindowsFileCleanupExecutorTests.cs`: Tests for recursive deletion, read-only files, reparse point rejection, containment validation, and post-delete verification.

#### VM Verification
```powershell
Test-Path "C:\Users\test\AppData\Roaming\Telegram Desktop"
# False
```
The directory was successfully and safely deleted.

#### Status
**FIXED**

---
