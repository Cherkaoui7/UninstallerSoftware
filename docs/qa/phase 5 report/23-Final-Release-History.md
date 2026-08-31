## Final Release History

| Version | Commit | Purpose | Status |
|:---|:---|:---|:---|
| 0.19.0 | (early Phase 5) | Initial production shell | Superseded |
| 0.21.0 | (Phase 5K) | ApplicationDetails data-context fix | Superseded |
| 0.21.1 | (Phase 5K fix2) | Parser logging injection | Superseded |
| 0.21.2 | (Phase 5K fix3) | Extended tracing for VM diagnosis | Superseded |
| 0.21.3 | (Phase 5G sync fix) | Command deduplicator fix | Superseded |
| 0.22.0 | `a876735` | Clean E2E-App-002 QA reset | Superseded |
| 0.23.0 | `1366476` | **Final validated release** | **RELEASE READY** |

Old VM binaries were rejected because SHA-256 verification showed they did not match the current source. The final release process requires:
1. Clean `git status`.
2. `git rev-parse HEAD` matches embedded `ProductVersion` SHA.
3. Host SHA-256 matches VM SHA-256.

---
