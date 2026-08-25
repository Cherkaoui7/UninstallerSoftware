# Phase 1: Application Discovery

## Overview
Phase 1 focuses on accurately and safely identifying all installed applications on the target Windows system. It is the entry point for the user and requires reading from multiple system sources without crashing on corrupted or malformed entries.

## 1. Data Sources
The discovery engine must scan various locations to compile a comprehensive list of applications:
*   **Primary Registry Keys:**
    *   `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall` (System-wide, 64-bit)
    *   `HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall` (System-wide, 32-bit)
    *   `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall` (User-specific)
*   **Other Sources:**
    *   MSI / Windows Installer APIs
    *   Windows Store / MSIX Packages
    *   (Future) Portable applications detected via file system scans

## 2. The Application Model
Data retrieved from the sources must be mapped to a unified `InstalledApplication` model. Key properties include:
*   `Id` (Unique identifier)
*   `Name`, `Version`, `Publisher`
*   `InstallLocation` (Crucial for later scanning, but cannot be assumed to be present)
*   `UninstallString` / `QuietUninstallString` (Command to run the official uninstaller)
*   `InstallDate`, `EstimatedSize`

## 3. UI Requirements
*   **Application List View:** Present the discovered applications to the user in a clean, filterable, and sortable list.
*   **Search/Filter:** Allow users to quickly find applications by name or publisher.
*   **Details View:** Show the user the metadata extracted (install path, version, uninstall string) before they proceed.

## 4. Acceptance Criteria
*   The engine can enumerate applications across Windows 10 and 11 reliably.
*   Malformed or missing registry keys (e.g., missing `DisplayName`) are handled gracefully without application crashes.
*   System components and updates are filtered out or clearly marked as non-removable/high-risk.
