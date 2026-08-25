# First Production Milestone (MVP)

## Overview
Building the complete Windows Uninstaller workflow is complex and high-risk. Attempting to build everything at once will delay launch and increase the likelihood of catastrophic bugs. Therefore, the first production milestone (V1 MVP) focuses on a safe, limited, but highly functional core loop.

## 1. MVP Scope (V1)
The V1 release must successfully execute the following linear flow:

1.  **Discover:** Enumerate Installed Apps (Registry).
2.  **Select:** User Selects an App.
3.  **Uninstall:** Run Official Uninstaller (Wait for completion).
4.  **Scan:** Scan Leftovers (Files, Folders, Registry only).
5.  **Classify:** Show Confidence scores.
6.  **Review:** User Selects items for Cleanup.
7.  **Clean:** Remove selected artifacts (Files & Registry).
8.  **Verify:** Verify deletion.
9.  **Report:** Generate final Summary.

## 2. Deferred Features
To reach the first milestone quickly and safely, the following features are intentionally deferred to subsequent updates (V1.x or V2):
*   Services cleanup (Complex permissions and high risk of bricking the OS).
*   Scheduled Tasks cleanup.
*   Full Backup/Rollback (File recovery is complex; initial release may only offer registry backups).
*   Installation Tracking/Snapshots (Phase 7).

## 3. Goal
Deliver a "legitimate V1" that is significantly better than the default Windows "Add/Remove Programs" by safely finding and removing standard leftover files and registry keys, without risking the stability of the operating system.
