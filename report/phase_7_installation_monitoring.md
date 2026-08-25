# Phase 7: Installation Monitoring (V2/V3)

## Overview
Phase 7 represents the advanced evolution of the product. Instead of relying on heuristics to guess what an application installed (which is inherently flawed), this phase implements real-time installation tracking. This becomes the ultimate source of truth for future uninstalls.

## 1. Snapshot Mechanism
*   **Pre-Install Snapshot:** Before a user runs an installer, the application takes a rapid snapshot of the system state (files, registry, services).
*   **Installation:** The user installs the application normally.
*   **Post-Install Snapshot:** The system takes a second snapshot after the installation completes.

## 2. System Diff & Manifest Generation
*   Calculate the exact difference between Snapshot A and Snapshot B (`B - A`).
*   Capture all new files, modified registry keys, new services, scheduled tasks, and firewall rules.
*   Generate an `InstallationManifest` (e.g., in JSON format) containing the exact, absolute paths of every modification.

## 3. Integration with the Uninstaller
When the user later decides to uninstall an application that has an `InstallationManifest`:
*   The scanner engine bypasses the heuristic guessing game.
*   It directly loads the manifest and targets the exact artifacts known to belong to the application.
*   Confidence scores are automatically `100% (Very Likely)`.

## 4. Acceptance Criteria
*   The diff engine correctly identifies file and registry changes without getting bogged down by background OS noise.
*   Manifests are securely persisted to the SQLite database.
*   The cleanup engine can consume a manifest to perform a hyper-accurate uninstall.
