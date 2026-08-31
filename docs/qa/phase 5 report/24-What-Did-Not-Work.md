## What Did Not Work

| Failed Approach | Lesson Learned |
|:---|:---|
| **Copy-VMFile** | Hyper-V Guest Service Interface is unreliable; Enhanced Session `\\tsclient` is robust. |
| **SMB share on VM** | Firewall and network isolation make SMB impractical for isolated test VMs. |
| **Relying on installed Program Files version** | Stale installed binaries mask code changes; always deploy from `publish-vm/`. |
| **Using stale binaries without hash verification** | SHA-256 comparison is mandatory; version numbers alone are insufficient. |
| **Blind recursive `Directory.Delete(path, true)`** | Unsafe — no child validation, no reparse-point defense, no containment check. |
| **Resolving scoped services from root provider** | Violates DI scope contract; must create explicit `IServiceScope` per operation. |
| **Keeping one navigation scope alive indefinitely** | Leaks DbContexts and services; scope must match ViewModel lifetime. |
| **Using `ResidualAnalysisSession.Id` as `UninstallSession.Id`** | Ephemeral IDs must never be used for FK relationships; persist first, reference second. |
| **Relying on missing WPF implicit DataTemplates** | Every ViewModel needs an explicit `DataTemplate`; WPF silently degrades to `.ToString()`. |
| **Treating passing unit tests as sufficient** | Unit tests with mocked dependencies do not catch DI lifecycle, EF translation, or filesystem interaction bugs. Real VM testing is irreplaceable. |

---
