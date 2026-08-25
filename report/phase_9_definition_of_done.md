# Definition of Done (V1 Release Criteria)

## Overview
The application is considered production-ready for its first major release when it can safely and reliably perform the complete uninstall lifecycle. The driving metric is not aggressive deletion, but absolute safety and reliability.

## 1. Core Philosophy: Do No Harm
*   **The Prime Metric:** How reliably can we remove application-owned artifacts *without* touching user-owned (documents, pictures) or system-owned (Windows OS) resources?
*   **False Negatives over False Positives:** It is infinitely better to leave an orphan registry key behind than to accidentally delete `C:\Windows\System32`.
*   Unknown artifacts must default to "DO NOT DELETE AUTOMATICALLY."

## 2. Security & Safety Gates
Before release, the application must pass strict security checks:
*   [ ] UAC privilege escalation is handled securely.
*   [ ] Path validation prevents path traversal (e.g., `../../Windows`).
*   [ ] System directories and untrusted symbolic links are explicitly blacklisted.
*   [ ] No arbitrary recursive deletion of registry trees.
*   [ ] Every deletion has an explicit, logged reason and confidence score.

## 3. Testing Quality Gates
The test suite must validate the application against various hostile or complex environments using dedicated Windows VMs (not the developer's main machine).
*   [ ] Windows 10 and Windows 11 tested on x64 architectures.
*   [ ] Locked files are handled gracefully without crashing the cleanup loop.
*   [ ] Uninstaller crashes are caught and handled.
*   [ ] Access Denied errors are logged, not fatal.
*   [ ] The application handles broken/missing official uninstallers by falling back to manual scanning.

## 4. Final Sign-off
V1 is complete when the architecture, confidence engine, logging, and cleanup execution work together seamlessly to provide a drastically safer and cleaner uninstall experience than native Windows tools.
