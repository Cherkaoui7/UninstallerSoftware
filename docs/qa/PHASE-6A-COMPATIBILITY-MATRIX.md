# Phase 6A — Real Application Compatibility Baseline

**Status**: BASELINE IN PROGRESS

## 1. Objective
Perform a comprehensive real-world compatibility audit of the current Uninstaller release using REAL Windows applications to discover compatibility gaps before changing the implementation.

## 2. Environment
- **OS**: Windows (x64)
- **Methodology**: Manual UI Validation

## 3. Release Identity
- **Git SHA**: `39d50eda9272f4f0cd4ad5679ca5db3c4eebe3c5`
- **FileVersion**: `0.23.0`
- **ProductVersion**: `0.23.0+39d50eda9272f4f0cd4ad5679ca5db3c4eebe3c5`
- **Executable SHA-256**: `CE301A19D87F6E5FCE83A3B5CB95E39C7AD3D9ADD64B400D606BF19C16537022`

## 4. Applications Tested
1. 7-Zip 26.02
2. Telegram Desktop
3. Microsoft Visual C++ 2013 Redistributable (x86)
4. Git
5. Python 3.12.10 (64-bit)
6. SteelSeries GG
7. Composer - PHP Dependency Manager
8. Mafia: Definitive Edition
9. Cisco Packet Tracer 9.0.1 64Bit
10. Free Download Manager

## 11. Compatibility Matrix

### Application Overview
| App | Install Type | Architecture | Uninstall Mechanism |
|----|----|----|----|
| 7-Zip 26.02 | Per-Machine | x64 | EXE Uninstaller |
| Telegram Desktop | Per-User | x64 | Inno Setup (EXE) |
| MS VC++ 2013 | Per-Machine | x86 (WoW64) | WiX Burn Bootstrapper |
| Git | | | |
| Python 3.12.10 | | | |
| SteelSeries GG | | | |
| Composer | | | |
| Mafia: Def. Ed. | | | |
| Cisco Packet Tracer | | | |
| Free Download Mgr | | | |

### Discovery & Validation
| App | Discovery | Command Validation | Official Uninstall | Exit Code | Verification |
|----|----|----|----|----|----|
| 7-Zip 26.02 | Registry (HKLM) | Parsed (`C:\Program Files\7-Zip\Uninstall.exe`) | Run EXE | 0 (Success) | VERIFIED PASS |
| Telegram Desktop | Registry (HKCU) | Executable (`unins000.exe`) | Run EXE | 0 (Success) | VERIFIED PASS |
| MS VC++ 2013 | Registry (WoW64) | Executable (`vcredist_x86.exe`) | Run EXE | 0 (Success) | VERIFIED PASS |
| Git | | | | | |
| Python 3.12.10 | | | | | |
| SteelSeries GG | | | | | |
| Composer | | | | | |
| Mafia: Def. Ed. | | | | | |
| Cisco Packet Tracer | | | | | |
| Free Download Mgr | | | | | |

### Residual Analysis
| App | Residuals Found | Classification | Risk | Recommended |
|----|----|----|----|----|
| 7-Zip 26.02 | None | N/A | Low | N/A |
| Telegram Desktop | None | N/A | Low | N/A |
| MS VC++ 2013 | None | N/A | Low | N/A |
| Git | | | | |
| Python 3.12.10 | | | | |
| SteelSeries GG | | | | |
| Composer | | | | |
| Mafia: Def. Ed. | | | | |
| Cisco Packet Tracer | | | | |
| Free Download Mgr | | | | |

### Cleanup Results
| App | Cleanup Tested | Result | Failure Reason |
|----|----|----|----|
| 7-Zip 26.02 | N/A | N/A | N/A |
| Telegram Desktop | N/A | N/A | N/A |
| MS VC++ 2013 | N/A | N/A | N/A |
| Git | | | |
| Python 3.12.10 | | | |
| SteelSeries GG | | | |
| Composer | | | |
| Mafia: Def. Ed. | | | |
| Cisco Packet Tracer | | | |
| Free Download Mgr | | | |

## 12. Failures & Evidence
*(Populated dynamically during execution)*

## 13. False Positives / False Negatives
*(Populated dynamically during execution)*

## 14. Application-Specific Findings
- **7-Zip**: The NSIS uninstaller returns `0` immediately before the registry keys are removed. Added a bounded retry in `UninstallService` (20 retries, 500ms delay) which successfully catches the delayed cleanup.
- **Telegram Desktop**: The Inno Setup uninstaller successfully removes its registry keys (HKCU) and completes verification. Verification loop correctly identified absence of the application.
- **MS VC++ 2013**: The WiX Bootstrapper uninstaller (x86 on WoW64) effectively removes its registry keys. The retry verification correctly captured the delayed registry deletion after the initial engine command completed.

## 15. Top Compatibility Gaps
*(To be completed at end of audit)*

## 16. Recommended Phase 6B Priorities
*(To be completed at end of audit)*

## 17. Final Conclusion
*(To be completed at end of audit)*
