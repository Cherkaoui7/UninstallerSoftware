# Phase 4: Cleanup Engine & Execution

## Overview
Phase 4 translates the scanner's findings into a concrete cleanup plan, presents it to the user for confirmation, and then safely executes the destructive operations in a specific order.

## 1. The Cleanup Plan & UI Review
Before any deletion occurs, a `CleanupPlan` object is generated.
*   **Data Structure:** Contains lists of Files, Folders, RegistryKeys, Services, and Tasks, along with Estimated Size and overall Risk Level.
*   **User Interface:** Present a clear, tree-style view to the user (e.g., "MyApp Cleanup").
*   **User Control:** High confidence items are checked by default. The user must be able to expand categories, review specific registry keys or file paths, and deselect anything they wish to keep.

## 2. Cleanup Execution Order
Destructive operations must be executed in a safe, logical order to prevent locks and system instability:
1.  **Stop & Remove Services:** Prevent background tasks from locking files.
2.  **Delete Scheduled Tasks:** Remove triggers from the Windows Task Scheduler.
3.  **Delete Files & Folders:** Move files to a recovery location or delete them. Handle locked files gracefully (e.g., schedule for deletion on reboot).
4.  **Remove Shortcuts & Links:** Clean up the Start Menu and Desktop.
5.  **Clean Registry Keys:** Safely delete isolated application keys.

## 3. Execution Safety Principles
*   **Validate Paths:** Prevent path traversal attacks (e.g., trying to delete `C:\Windows`).
*   **Handle Access Denied:** If a file cannot be deleted, log the error and continue. Do not crash the cleanup loop.
*   **Verify Each Step:** Capture the success or error of each individual deletion operation for the final report.

## 4. Acceptance Criteria
*   The UI clearly communicates the risk and allows granular deselection of artifacts.
*   Operations are executed in the correct order (services before files).
*   Destructive actions are fully logged.
*   The engine safely handles locked files and permission errors.
