# Uninstaller Software

## Project Purpose
A safe, clean architecture-based Windows application uninstaller.

## Architecture
Phase 0 establishes a clean architecture:
- `Domain`: Framework-independent models.
- `Core`: Application business logic.
- `Infrastructure`: Persistence (EF Core + SQLite) and Logging (Serilog).
- `Windows`: OS-specific APIs.
- `App`: WPF UI and Composition Root.

## Technology Stack
- .NET 10
- WPF
- Entity Framework Core 10 (SQLite)
- Serilog
- xUnit & NetArchTest.Rules

## Current Phase
**Phase 0 — Architecture Foundation**
*Note: This is an architectural baseline. No uninstall logic, scanning, or deletion exists yet.*

## Supported Windows Versions
Windows 10 / Windows 11

## Development Setup
1. Install .NET 10 SDK
2. Open `Uninstaller.sln`

## Build Instructions
```bash
dotnet restore
dotnet build --configuration Release
```

## Testing Instructions
```bash
dotnet test
```

## Security Model
See `SECURITY.md`.

## Roadmap
- Phase 1: Application Discovery
- Phase 2: Metadata Aggregation
- Phase 3: Smart Scanning Engine
- Phase 4: UI Build
- Phase 5: Deletion Engine
- Phase 6: System Restore
- Phase 7: App Store
- Phase 8: Winget
- Phase 9: Settings
- Phase 10: Beta
