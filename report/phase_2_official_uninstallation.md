# Phase 2: Official Uninstallation & Process Management

## Overview
Phase 2 executes the first major action of the uninstallation workflow. The principle is to *always* attempt to use the application's official uninstall mechanism before doing any manual cleanup. This phase includes pre-uninstall analysis to ensure the uninstaller can run smoothly without locked files.

## 1. Pre-Uninstall Analysis (Process Manager)
Before launching the uninstaller, the system must ensure the application isn't currently running.
*   **Detection:** Scan active Windows processes and map them to the application using signals such as executable path, digital signature, and process name.
*   **Association:** Do not blindly terminate processes with matching names. E.g., `MyAppUpdater.exe` should be confidently linked to the app's install directory.
*   **Termination:** Present active processes to the user and request termination. If a process cannot be killed, log the failure rather than silently ignoring it.

## 2. Executing the Uninstaller
Once the environment is prepped, the engine executes the official uninstaller.
*   **Command Parsing:** Interpret the `UninstallString` or `QuietUninstallString`.
*   **Elevation:** Detect if the uninstaller requires UAC elevation and handle the prompt securely.
*   **Supported Types:** Must support standard `.exe` installers (Nullsoft, Inno Setup), MSI installers (`msiexec.exe /x`), and silent/quiet flags.
*   **Execution & Monitoring:** Launch the process, wait for it to complete, and capture the exit code.

## 3. Handling Outcomes
*   **Do Not Trust Exit Code 0:** An exit code of `0` does not mean all files were removed. It simply means the uninstaller *program* didn't crash.
*   **Failures:** If the official uninstaller fails, crashes, or is missing entirely, the system must gracefully catch this and prompt the user to proceed to the manual residual scan (Phase 3).

## 4. Acceptance Criteria
*   Can successfully shut down associated running processes.
*   Can successfully launch and wait for EXE and MSI uninstallers.
*   Handles UAC prompts correctly.
