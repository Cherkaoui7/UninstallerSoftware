### Incident 4 — Hyper-V Copy-VMFile Failure

**Date**: ~2026-08-29 through 2026-08-30

#### Symptom
Attempts to transfer files from the host to the Hyper-V VM using `Copy-VMFile` failed with:
> 0x80070015 — device not ready

#### Trigger
Running `Copy-VMFile -Name "VM" -SourcePath "..." -DestinationPath "..." -FileSource Host` from the host.

#### Exact Evidence
- The Hyper-V Guest Service Interface was not reliably starting inside the VM.
- Host-side `vmicguestinterface` service failure prevented the file copy.
- SMB share attempts also failed due to VM firewall configuration and network isolation.

#### Root Cause
The Hyper-V Guest Service Interface (Integration Service) responsible for host-to-guest file copy was not enabled or not operational in the VM configuration. The VM's network configuration also prevented standard SMB file sharing.

#### Files / Components Involved
- Hyper-V VM configuration
- Guest Service Interface integration service
- Windows Firewall on VM

#### Resolution
Adopted **Enhanced Session (RDP) drive redirection** as the deployment mechanism. With Enhanced Session mode enabled, the host's drives are mapped inside the VM as `\\tsclient\C\...`, allowing direct file copy via Explorer or PowerShell:
```powershell
Copy-Item "\\tsclient\C\Users\USER\Documents\Uninstaller\publish-vm\Uninstaller.App.exe" -Destination "C:\Users\test\Desktop\"
```

This became the preferred and final deployment path because it:
1. Required no guest service configuration.
2. Worked immediately after enabling Enhanced Session mode.
3. Was verifiable via SHA-256 hash comparison.

#### Regression Tests
No automated test — infrastructure configuration.

#### VM Verification
All subsequent deployments used `\\tsclient` successfully.

#### Status
**FIXED** (workaround adopted, `Copy-VMFile` abandoned)

---
