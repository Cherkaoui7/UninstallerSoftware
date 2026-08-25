# Phase 0: System Architecture & Persistence Foundation

## Overview
Phase 0 establishes the bedrock of the Windows Uninstaller application. Before implementing any destructive or scanning logic, a robust, modular, and safe architecture must be constructed. The overarching principle is the strict separation of concerns, ensuring that the User Interface never directly touches the Windows filesystem or registry.

## 1. Architectural Layers
The application is structured into clearly defined layers, flowing downwards:

*   **UI Layer (WPF / WinUI 3):** Handles presentation (Dashboard, Application List, Cleanup Wizard). It communicates exclusively through Application Services.
*   **Application Services:** Orchestrates the flow of the application, managing states and passing commands between the UI and Domain logic.
*   **Domain Logic:** Contains the core business rules (Scanner, Analyzer, Cleanup logic, Backup metadata). It operates on abstract representations of files and registry keys, not the actual system.
*   **Infrastructure:** Connects the abstract domain to the physical machine (SQLite database interaction, Logging configuration).
*   **Windows API Integration:** The lowest level, interacting directly with the Windows Registry, Filesystem, Process APIs, and Service Control Manager. This layer performs the actual destructive or read operations and requires UAC elevation when necessary.

## 2. Storage & Persistence (SQLite)
A local SQLite database is required to maintain the history, session states, and rollback capabilities. 
Key tables to implement:
*   `applications`: Stores metadata of discovered apps (Name, Publisher, InstallLocation, UninstallCommand).
*   `uninstall_sessions`: Tracks the active state of an ongoing uninstall transaction (e.g., Scanning, WaitingForConfirmation, Executing, Completed, RolledBack).
*   `artifacts`: Links discovered items (files, registry keys) to a session, including their confidence score and user-selection status.
*   `backups` & `operations`: Stores paths to backed-up files/registry keys to facilitate the rollback mechanism.
*   `logs`: A centralized audit trail.

## 3. Project Structure
The solution should be partitioned into distinct libraries:
*   `Uninstaller.App` (UI executable)
*   `Uninstaller.Core` (Application Services)
*   `Uninstaller.Domain` (Business rules)
*   `Uninstaller.Infrastructure` (Database, file access)
*   `Uninstaller.Windows` (Windows API wrappers)

## 4. Key Deliverables for this Phase
*   Establish the repository and project structure.
*   Implement Dependency Injection (DI).
*   Setup structured logging (e.g., Serilog) for auditing everything.
*   Initialize the SQLite database schema.
*   Set up the CI/CD pipeline.
