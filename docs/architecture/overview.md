# Architecture Overview

This project uses strict Clean Architecture principles.

- `Uninstaller.Domain`: Framework-independent models and pure C#.
- `Uninstaller.Core`: Business logic and abstractions. Depends on Domain.
- `Uninstaller.Infrastructure`: Persistence, EF Core, and Logging. Depends on Core and Domain.
- `Uninstaller.Windows`: Implementation of OS-specific APIs. Depends on Core and Domain.
- `Uninstaller.App`: The composition root and UI. Depends on everything but contains no direct business logic.
