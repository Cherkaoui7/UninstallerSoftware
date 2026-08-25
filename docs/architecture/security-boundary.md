# Security Boundary

The UI layer is an unprivileged orchestrator. Any destructive operations must be strictly routed through the `Uninstaller.Windows` interfaces, which acts as the security boundary. 

No privileged operations should ever execute on application startup.
