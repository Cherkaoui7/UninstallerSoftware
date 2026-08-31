## E2E Test Evolution

### Synthetic E2E Fixtures (E2E-App-001 / E2E-App-002)

Synthetic test applications were created as controlled fixtures for VM testing.

**E2E-App-001** was the first fixture — a minimal .NET console application with registry entries. It was used to validate:
- Application discovery from registry.
- Application Details display.
- Official uninstall subprocess execution.

What it **failed to prove**: It did not produce residual artifacts after uninstallation, so the entire residual analysis → cleanup pipeline could not be tested.

**E2E-App-002** was a refined fixture with a proper PE uninstaller executable that deleted its own install directory. It successfully validated the uninstall subprocess flow but still produced only minimal residuals.

### Real Application Testing

The limitations of synthetic fixtures led to testing with real installed applications:

- **Freebuff**: Not established from available evidence whether this was tested.
- **Git for Windows**: Used for initial discovery and details validation.
- **Telegram Desktop**: Became the **principal end-to-end cleanup test** because after official uninstallation, it left behind:
  1. `C:\Users\test\AppData\Roaming\Telegram Desktop` (directory with `log_start0.txt`)
  2. A registry residual entry.

  Telegram was the only application that exercised the **full production path**: discovery → details → official uninstall → residual analysis → cleanup plan → preflight → backup → journal → recursive directory deletion → verification → history → history details.

---
