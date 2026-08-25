
# Production Report: Windows Uninstaller Software

## 1. Project Objective

Build a production-grade Windows uninstaller capable of:

* Detecting installed applications.
* Executing official uninstallers safely.
* Detecting remaining files, folders, registry entries, services, scheduled tasks, and processes.
* Determining whether detected artifacts actually belong to the selected application.
* Presenting a cleanup plan before deletion.
* Performing cleanup safely with administrator privileges when required.
* Creating backups and supporting rollback where technically possible.
* Maintaining detailed logs and audit information.
* Eventually tracking installations to provide highly accurate uninstall operations.

The core product principle is:

> **Detect → Analyze → Plan → Confirm → Backup → Remove → Verify**

Do not build the system around indiscriminate deletion.

---

# 2. Target Platform

Primary target:

* Windows 10
* Windows 11
* x64 initially
* Administrator elevation when required

Recommended stack:

* C#
* .NET 10
* WPF or WinUI 3
* MVVM
* SQLite
* Serilog
* WiX or MSIX for application distribution

Windows APIs/components:

* Windows Registry
* Windows Service Control Manager
* Task Scheduler
* Process APIs
* Windows Installer/MSI
* Filesystem APIs
* Windows Security/UAC

---

# 3. Production Architecture

```text
┌──────────────────────────────────────────────┐
│                    UI Layer                  │
│                                              │
│ Dashboard / Applications / Scan / Cleanup    │
└───────────────────────┬──────────────────────┘
                        │
                        ▼
┌──────────────────────────────────────────────┐
│                 Application Core              │
│                                              │
│ Discovery │ Uninstall │ Scanner │ Analyzer   │
│ Cleanup   │ Backup    │ Verify  │ Recovery   │
└───────────────────────┬──────────────────────┘
                        │
                        ▼
┌──────────────────────────────────────────────┐
│              Windows Integration              │
│                                              │
│ Registry │ Filesystem │ Processes            │
│ Services │ Tasks      │ MSI / Windows APIs    │
└───────────────────────┬──────────────────────┘
                        │
                        ▼
┌──────────────────────────────────────────────┐
│                   Storage                    │
│                                              │
│ SQLite │ Logs │ Manifests │ Backup Metadata  │
└──────────────────────────────────────────────┘
```

The UI must not directly manipulate Windows resources.

Use:

```text
UI
 ↓
Application Services
 ↓
Domain Logic
 ↓
Infrastructure
 ↓
Windows APIs
```

This separation is important for testing and long-term maintenance.

---

# 4. Core Modules

## 4.1 Application Discovery

Responsible for discovering installed applications.

Sources should include:

```text
HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall
HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall
HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall
```

Also investigate:

* MSI-installed applications
* Microsoft Store/MSIX applications
* Portable applications as a separate category

Application model:

```text
InstalledApplication

Id
Name
Version
Publisher
Architecture
InstallLocation
UninstallCommand
QuietUninstallCommand
RegistrySource
InstallDate
EstimatedSize
IsSystemComponent
```

Do not assume that every application has a valid `InstallLocation`.

---

# 5. Official Uninstaller Engine

The first uninstall operation should always attempt to use the application's official uninstall mechanism.

Flow:

```text
User selects application
        ↓
Validate application metadata
        ↓
Locate official uninstaller
        ↓
Create uninstall session
        ↓
Stop related processes if necessary
        ↓
Execute official uninstaller
        ↓
Wait for completion
        ↓
Capture exit code
        ↓
Start residual scan
```

Handle:

* `.exe` uninstallers
* MSI uninstallers
* Quiet uninstall commands
* Uninstallers requiring elevation
* Uninstallers that return unusual exit codes
* Applications whose uninstallers are missing

Never assume an exit code of `0` means that every application artifact disappeared.

---

# 6. Process Manager

Before removal, identify processes associated with the application.

Evidence:

```text
Executable path
Publisher
Digital signature
Install directory
Process name
Parent process
Command line
```

Do not terminate a process merely because its name contains the application name.

Example:

```text
MyApp.exe
MyAppUpdater.exe
MyAppHelper.exe
```

should receive an association score rather than automatic trust.

Process workflow:

```text
Detect
 ↓
Associate
 ↓
Display active processes
 ↓
Request termination
 ↓
Verify termination
```

If a process cannot be terminated, the cleanup engine should report the reason instead of silently failing.

---

# 7. Residual Scanner

The scanner is the core intelligence component.

Search locations should initially be limited to high-confidence areas:

```text
%APPDATA%
%LOCALAPPDATA%
%PROGRAMDATA%
%TEMP%
Program Files
Program Files (x86)
Start Menu
Desktop
```

Potential artifacts:

```text
Files
Folders
Shortcuts
Configuration
Caches
Logs
Databases
Updater components
```

Avoid unrestricted full-disk scanning in V1.

---

# 8. Registry Scanner

Search application-specific registry locations.

Primary areas:

```text
HKCU\Software
HKLM\Software
HKLM\Software\WOW6432Node
```

Potential artifacts:

```text
Application configuration
Uninstall information
File associations
Shell extensions
Updater configuration
Startup entries
```

Registry deletion is high risk.

Therefore:

```text
Exact application key
        ↓
High confidence

Partial name match
        ↓
Low confidence

Generic/shared key
        ↓
Do not automatically delete
```

The engine should never recursively delete an entire registry branch simply because the application name appears somewhere inside it.

---

# 9. Services

Detect Windows services associated with the application.

Association signals:

```text
Service executable path
Publisher
Service name
Display name
Install directory
```

Lifecycle:

```text
Detect service
 ↓
Verify ownership
 ↓
Stop service
 ↓
Official uninstaller
 ↓
Verify service removal
 ↓
Remove only if appropriate
```

Services should have their own cleanup category in the UI.

---

# 10. Scheduled Tasks

Inspect Windows Task Scheduler for application-related tasks.

Possible examples:

```text
Updater
Maintenance
Telemetry
Background Agent
Auto Update
```

Association must use executable paths and metadata rather than task-name matching alone.

---

# 11. Association / Confidence Engine

This should become one of the most important components of the product.

Every detected artifact receives a confidence score.

Example:

```text
Exact installation path             +100
Exact application registry path     +100
Executable inside install folder     +80
Publisher match                      +40
Digital signature match              +40
Application name match               +20
Recently created                      +10
Shared/common directory              -50
User documents directory              -80
System directory                      -100
```

Example result:

```text
Artifact
C:\ProgramData\MyApp

Confidence: 100
Classification: SAFE CANDIDATE
```

Another:

```text
Artifact
C:\Users\User\Documents\MyApp

Confidence: 25
Classification: USER DATA
Action: DO NOT AUTOMATICALLY DELETE
```

Use multiple signals rather than a single heuristic.

---

# 12. Cleanup Plan

Never immediately delete scanner results.

Create a cleanup plan:

```text
CleanupPlan

Application
Timestamp
Files[]
Folders[]
RegistryKeys[]
Services[]
ScheduledTasks[]
Shortcuts[]
EstimatedSize
RiskLevel
Confidence
```

The UI should show:

```text
MyApp Cleanup

Files                    37
Folders                   8
Registry keys             5
Services                  1
Scheduled tasks           2
Shortcuts                 3

Recoverable space:       428 MB

Risk:
LOW

[Cancel]       [Review]       [Remove]
```

The user should be able to deselect individual artifacts.

---

# 13. Backup and Recovery

Before destructive operations:

```text
Cleanup Plan
      ↓
Backup
      ↓
Deletion
      ↓
Verification
```

For registry modifications:

* Export affected registry keys where possible.

For files:

* Move files into a controlled recovery location rather than immediately permanently deleting them, where practical.

Store metadata:

```text
BackupId
ApplicationId
OriginalPath
BackupPath
Operation
Timestamp
Hash
```

Rollback:

```text
Cleanup failed
      ↓
Identify completed operations
      ↓
Restore backed-up resources
      ↓
Report rollback status
```

Rollback should never be advertised as universally guaranteed because some operations—especially service/driver/system-level changes—may not be perfectly reversible.

---

# 14. Transaction Engine

Treat uninstall as a transaction.

```text
START
 ↓
Create session
 ↓
Create cleanup plan
 ↓
Backup
 ↓
Operation 1
 ↓
Operation 2
 ↓
Operation 3
 ↓
Verification
 ↓
COMMIT
```

If an operation fails:

```text
ROLLBACK
```

Maintain states:

```text
Created
Scanning
Planning
WaitingForConfirmation
BackingUp
Executing
Verifying
Completed
Failed
Rollback
RolledBack
PartiallyCompleted
```

This makes the system much easier to debug.

---

# 15. Logging

Every operation should generate structured logs.

Example:

```text
Session ID: 8F4...
Application: MyApp

[INFO] Application discovered
[INFO] Official uninstaller found
[INFO] Process detected: MyApp.exe
[INFO] Process terminated
[INFO] Official uninstaller executed
[INFO] Exit code: 0
[INFO] Residual scan started
[INFO] 47 artifacts detected
[INFO] 39 high-confidence artifacts
[INFO] Cleanup started
[INFO] Cleanup completed
[INFO] Verification completed
```

Use structured logging rather than plain console messages.

---

# 16. Database

SQLite is sufficient initially.

Suggested tables:

```text
applications
uninstall_sessions
artifacts
cleanup_plans
cleanup_operations
backups
installation_manifests
logs
```

Example:

```text
applications
────────────────────────
id
name
version
publisher
install_location
uninstall_command
discovered_at
```

```text
artifacts
────────────────────────
id
session_id
type
path
confidence
classification
selected
status
```

---

# 17. Installation Tracking — V2/V3

This is the feature that can differentiate the product.

Instead of trying to guess what belongs to an application after installation, monitor the installation itself.

Before installation:

```text
System Snapshot A
```

Install application.

After installation:

```text
System Snapshot B
```

Calculate:

```text
B - A
```

Potential changes:

```text
Files
Registry
Services
Scheduled Tasks
Shortcuts
Environment variables
Firewall rules
Startup entries
```

Generate:

```text
InstallationManifest
```

Example:

```json
{
  "application": "MyApp",
  "files": [],
  "registryKeys": [],
  "services": [],
  "scheduledTasks": []
}
```

The manifest becomes the primary source of truth during future uninstall operations.

---

# 18. Security Requirements

This product operates with potentially dangerous privileges.

Mandatory requirements:

* UAC-aware privilege escalation.
* Never execute arbitrary commands without validation.
* Validate paths before deletion.
* Prevent path traversal.
* Never delete Windows system directories.
* Never follow untrusted symbolic links blindly.
* Validate executable paths.
* Verify digital signatures where relevant.
* Protect backup metadata.
* Avoid logging secrets or sensitive command-line arguments.
* Use least privilege whenever possible.

Critical rule:

```text
UNKNOWN ARTIFACT
      ↓
DO NOT DELETE AUTOMATICALLY
```

False negatives are preferable to destructive false positives.

---

# 19. UI

Recommended screens:

```text
Dashboard
│
├── Installed Applications
│
├── Application Details
│
├── Uninstall Wizard
│
├── Cleanup Scanner
│
├── Cleanup Review
│
├── History
│
├── Recovery
│
└── Settings
```

The uninstall workflow should be:

```text
Select application
        ↓
Analyze
        ↓
Official uninstall
        ↓
Scan leftovers
        ↓
Review
        ↓
Backup
        ↓
Remove
        ↓
Verify
        ↓
Report
```

Avoid creating a UI that looks like a file manager with a giant "DELETE EVERYTHING" button. The product should communicate risk and confidence clearly.

---

# 20. Development Roadmap

## Phase 0 — Architecture

Deliverables:

* Repository structure
* Domain models
* Dependency injection
* Logging
* Error handling
* Configuration
* CI pipeline

Do not implement aggressive cleanup yet.

---

## Phase 1 — Application Discovery

Implement:

* Registry discovery
* Application model
* Search/filter
* Application details
* Uninstall command detection

Acceptance criterion:

> Correctly enumerate common Windows desktop applications without crashing on malformed registry entries.

---

## Phase 2 — Official Uninstallation

Implement:

* EXE uninstallers
* MSI uninstallers
* Process detection
* UAC elevation
* Exit-code handling
* Uninstall session tracking

Acceptance criterion:

> Successfully uninstall a controlled test suite of applications using their official uninstallers.

---

## Phase 3 — Residual Scanner

Implement:

* Filesystem scanner
* AppData scanner
* ProgramData scanner
* Registry scanner
* Shortcut scanner

Do not automatically delete anything yet.

Acceptance criterion:

> Scanner produces a reviewable list of candidates with confidence scores.

---

## Phase 4 — Cleanup Engine

Implement:

* Cleanup plans
* User selection
* File deletion
* Registry cleanup
* Shortcut cleanup
* Service cleanup
* Scheduled-task cleanup

Acceptance criterion:

> Every destructive operation is represented in a cleanup plan and logged.

---

## Phase 5 — Backup / Rollback

Implement:

* Registry backups
* File recovery
* Transaction state
* Rollback
* Recovery UI

Acceptance criterion:

> Simulated failures can recover previously modified resources.

---

## Phase 6 — Verification

After cleanup:

```text
Re-scan
 ↓
Compare
 ↓
Remaining artifacts
 ↓
Report
```

Result:

```text
Removed:        42
Remaining:       3
Failed:          1
Skipped:         2
```

Do not claim "100% clean" unless your verification mechanism can actually substantiate it.

---

## Phase 7 — Installation Monitoring

Implement:

* Pre-install snapshot
* Post-install snapshot
* System diff
* Manifest generation
* Manifest persistence

This should become the foundation of advanced uninstall accuracy.

---

# 21. Testing Strategy

Create a dedicated Windows test environment.

Test categories:

```text
Normal application
Portable application
MSI application
MSIX application
32-bit application
64-bit application
Application with service
Application with updater
Application with scheduled task
Application with registry configuration
Application with user data
Application with locked files
Application requiring administrator privileges
Broken/missing uninstaller
```

Also test failure scenarios:

```text
Process cannot terminate
File is locked
Registry key unavailable
Access denied
Uninstaller crashes
Machine loses power
Cleanup operation fails
Backup fails
Rollback partially fails
```

Never use your main development machine as the primary destructive test environment.

Use:

* Windows VM snapshots
* Disposable test machines
* Automated test fixtures

---

# 22. Production Quality Gates

Before releasing V1:

```text
[ ] No arbitrary recursive deletion
[ ] Every deletion has an explicit reason
[ ] Confidence scoring implemented
[ ] UAC handling tested
[ ] Registry handling tested
[ ] Locked files handled
[ ] Services tested
[ ] Scheduled tasks tested
[ ] Official uninstallers supported
[ ] Structured logging implemented
[ ] Cleanup preview implemented
[ ] Failure states implemented
[ ] Test suite running
[ ] Windows 10 tested
[ ] Windows 11 tested
[ ] x64 tested
[ ] Recovery mechanism tested
```

---

# 23. Recommended Repository Strategy

```text
src/
├── Uninstaller.App
├── Uninstaller.Core
├── Uninstaller.Domain
├── Uninstaller.Infrastructure
├── Uninstaller.Windows
└── Uninstaller.Data

tests/
├── Core.Tests
├── Scanner.Tests
├── Registry.Tests
├── Cleanup.Tests
├── Windows.Tests
└── Integration.Tests

docs/
├── architecture/
├── security/
├── testing/
└── product/
```

Keep Windows-specific implementation out of the core domain whenever possible.

---

# 24. First Production Milestone

Do not attempt the complete product immediately.

The first production milestone should be:

```text
Installed Apps
      ↓
Select App
      ↓
Run Official Uninstaller
      ↓
Scan Leftovers
      ↓
Show Confidence
      ↓
User Selects Cleanup
      ↓
Remove
      ↓
Verify
      ↓
Generate Report
```

That is a legitimate V1.

Then add:

```text
Services
        +
Scheduled Tasks
        +
Backup/Rollback
        +
Installation Tracking
```

incrementally.

---

# 25. Definition of Done for V1

V1 is production-ready when the application can safely perform the complete lifecycle:

```text
DISCOVER
   ↓
IDENTIFY
   ↓
UNINSTALL
   ↓
SCAN
   ↓
CLASSIFY
   ↓
REVIEW
   ↓
BACKUP
   ↓
CLEAN
   ↓
VERIFY
   ↓
REPORT
```

The most important engineering metric should not be "how many files can we delete."

It should be:

> **How reliably can we remove application-owned artifacts without touching user-owned or system-owned resources?**

That metric should drive the architecture, testing strategy, confidence engine, and release criteria.
