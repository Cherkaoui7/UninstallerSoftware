## Final Lessons Learned

1. **Unit tests were insufficient by themselves.** All 270+ unit tests passed before Phase 5 VM testing. The VM revealed 14 defects invisible to mocked test environments.

2. **Real VM testing found defects.** DI scope violations, EF translation errors, filesystem permission issues, and WPF DataTemplate gaps only manifest in integrated execution.

3. **Artifact identity matters.** Without SHA-256 verification, it is impossible to confirm the VM is running the correct binary. Version numbers can be stale or duplicated.

4. **Lifecycle ownership matters.** When one component creates a scope and another component disposes it, the services within that scope become unusable. Ownership must be explicit and documented.

5. **Persistence IDs must be explicit.** Using an ephemeral in-memory ID where a database FK is expected causes constraint violations that only surface with real data. The identity chain must be traced end-to-end.

6. **Safety rules must be shared.** When two components (EvidenceEngine and PreflightValidator) implement independent safety classification, they will inevitably disagree. A single authoritative safety model prevents conflicting decisions.

7. **UI and backend state must agree.** Showing an artifact as "Low Risk / Recommended" in the plan but rejecting it as "Protected" during preflight destroys user trust. The same logic must drive both decisions.

8. **Every destructive action needs verification.** After deleting a directory, `Directory.Exists` must return `false`. After persisting a backup, the FK chain must be valid. Optimistic assumptions are unacceptable for irreversible operations.

---
