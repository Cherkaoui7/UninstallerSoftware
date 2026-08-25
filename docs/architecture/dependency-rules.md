# Dependency Rules

- Domain must remain framework-independent.
- Core must not directly access Windows APIs, filesystem, registry, services, processes, or SQLite.
- All Windows-specific operations belong in Uninstaller.Windows.
- All persistence belongs in Uninstaller.Infrastructure.
- UI must never directly access operating-system resources.
