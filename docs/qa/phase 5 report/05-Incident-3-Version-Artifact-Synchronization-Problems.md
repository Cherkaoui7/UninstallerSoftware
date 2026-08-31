### Incident 3 — Version / Artifact Synchronization Problems

**Date**: Throughout Phase 5 (~2026-08-29 through 2026-08-31)

#### Symptom
Multiple instances where the VM was running an older executable despite newer source changes being committed and published. Symptoms included:
- UI not reflecting code changes.
- Old version numbers displayed (0.19.0, 0.21.x when 0.22.0 was expected).
- Wrong file sizes.
- Mismatched `ProductVersion` strings.
- `publish-vm/` containing multiple artifacts from different builds.

#### Trigger
Deploying new builds to the VM without rigorous version and hash verification.

#### Exact Evidence
From the Phase 5G QA Reset Report: "All previous Uninstaller binaries in the brain folder have been destroyed to guarantee ONE single authoritative binary." Multiple stale artifacts (`Uninstaller-0.21.0-phase5k-win-x64.exe`, `Uninstaller-0.21.1-phase5k-fix2-win-x64.exe`, `Uninstaller-0.21.2-phase5k-fix3-win-x64.exe`) existed alongside newer versions. The VM was inadvertently running older binaries.

#### Root Cause
1. No mandatory hash verification step after deployment.
2. Multiple named artifacts accumulated in output directories.
3. The `\\tsclient` transfer mechanism required manual file selection, creating opportunity for error.
4. Windows cached the old version in Program Files paths when using installed-mode deployment.

#### Files / Components Involved
- `Directory.Build.props` (version bumps: 0.19.0 → 0.21.x → 0.22.0 → 0.23.0)
- `publish-vm/` output directory
- VM deployment scripts

#### Resolution
1. Established a mandatory **SHA-256 verification invariant**: `git rev-parse HEAD` must equal the Git SHA embedded in `ProductVersion`.
2. Published to a clean `publish-vm/` directory with a single authoritative executable.
3. Verified on the VM using: `(Get-Item Uninstaller.App.exe).VersionInfo.ProductVersion` and `Get-FileHash`.
4. Documented that old binaries must be explicitly destroyed before deploying new ones.

#### Regression Tests
No automated test — this is a deployment process discipline.

#### VM Verification
Final verified deployment:
- Host SHA-256: `1E26B5B3D3CC08FA610030337F4F6C9C253664128D74567807725CE97F17D5FC`
- VM SHA-256: matched.
- ProductVersion: `0.23.0+1366476d37c34c92f94a609d82f59a50ef094074`

#### Status
**FIXED** (process established)

---
