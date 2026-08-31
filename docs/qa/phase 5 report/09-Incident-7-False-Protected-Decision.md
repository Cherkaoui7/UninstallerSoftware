### Incident 7 — False Protected Decision

**Date**: ~2026-08-31 (commit `a2aeb2f`)

#### Symptom
The Cleanup Plan displayed the Telegram AppData directory as:
- `Protected = false`
- `Risk = Low`
- `Recommended = true`

But when the user selected it and proceeded to cleanup, the preflight validator rejected it as **Protected**.

#### Trigger
Selecting `C:\Users\test\AppData\Roaming\Telegram Desktop` in the cleanup plan and proceeding to execution.

#### Exact Evidence
The `CleanupPreflightValidator` used a broad path containment check against a list of protected paths. The `_protectedPaths` list included `UserProfile` (e.g., `C:\Users\test`). The containment logic checked whether any protected path was a **prefix** of the artifact path. Since `C:\Users\test` is a prefix of `C:\Users\test\AppData\Roaming\Telegram Desktop`, the artifact was classified as Protected — even though it was clearly an application-owned subdirectory.

The `EvidenceEngine` (which scored the artifact for the plan UI) used a different, more permissive classification, leading to the disagreement between the plan display and the preflight decision.

#### Root Cause
Two independent safety models existed:
1. `EvidenceEngine` — scored artifacts for the plan UI (showed Low Risk).
2. `CleanupPreflightValidator` — authorized artifacts for execution (rejected as Protected).

The preflight validator's containment check was too broad: treating the entire user profile tree as protected blocked legitimate application-owned directories.

#### Files / Components Involved
- `src/Uninstaller.Core/Services/CleanupPreflightValidator.cs`
- `src/Uninstaller.Core/Services/EvidenceEngine.cs`
- `src/Uninstaller.Windows/Services/WindowsFileCleanupExecutor.cs`

#### Resolution
Implemented a unified protection hierarchy:
1. **Protected exact roots**: System directories (`C:\Windows`, `C:\Program Files`, etc.) — always blocked.
2. **Recursively protected system trees**: `C:\Windows\System32\...` — always blocked at any depth.
3. **User data trees with application exceptions**: `C:\Users\<user>\AppData\Roaming\<AppName>` — allowed when the subdirectory name matches the application being cleaned.
4. **Desktop special handling**: Direct `Desktop` children may be shortcuts (allowed), but the Desktop directory itself is protected.
5. **Canonical path normalization**: All paths normalized via `Path.GetFullPath` before comparison.
6. **Reparse point / symlink / junction defense**: Artifacts on reparse points are rejected.

The preflight and evidence engine were aligned to use the same classification rules.

#### Regression Tests
- `CleanupPreflightValidatorTests.cs`: Updated test expectations.
- `ProductionCleanupSafetyPipelineTests.cs`: End-to-end cleanup plan and preflight tests.

#### VM Verification
Telegram AppData directory correctly authorized by preflight.

#### Status
**FIXED** (security was strengthened, not weakened)

---
