# Phase 3: Residual Scanner & Confidence Engine

## Overview
Phase 3 is the core intelligence of the application. After the official uninstaller finishes (or if it fails), this engine searches the system for leftover artifacts. Importantly, **this phase only scans and scores; it does not delete anything.**

## 1. Scan Targets
The scanner should look in high-probability areas for leftovers rather than performing a blind full-disk scan:
*   **Filesystem:** `%APPDATA%`, `%LOCALAPPDATA%`, `%PROGRAMDATA%`, `Program Files`, `Program Files (x86)`, `Start Menu`, `Desktop`, `%TEMP%`.
*   **Registry:** `HKCU\Software`, `HKLM\Software`, `HKLM\Software\WOW6432Node`.
*   **System Extensions:** Services, Scheduled Tasks, Shortcuts, File Associations.

## 2. The Association / Confidence Engine
This is the most critical logic block in the product. Every detected artifact is assigned a confidence score based on multiple heuristics, ensuring that shared system files are never flagged for deletion.

**Example Scoring Heuristics:**
*   Exact install path match: `+100`
*   Executable inside install folder: `+80`
*   Publisher/Digital signature match: `+40`
*   Application name match (partial): `+20`
*   Inside a shared/common directory: `-50`
*   Inside User Documents: `-80`
*   Inside Windows/System32 directory: `-100`

## 3. Artifact Classification
Based on the confidence score, artifacts are categorized:
*   **90-100% (Very Likely):** Safe Candidate (Safe to recommend for deletion).
*   **70-89% (Likely):** Medium Confidence (Flag for user review).
*   **40-69% (Possible):** Low Confidence (Uncheck by default).
*   **0-39% (Unlikely):** System Component / Shared Component (Hide or mark as DO NOT DELETE).

## 4. Acceptance Criteria
*   The scanner rapidly identifies leftovers across files, registry, and tasks.
*   The confidence engine correctly scores items, penalizing system folders and rewarding exact matches.
*   The system never recursively flags an entire registry branch just because a keyword matched inside it.
