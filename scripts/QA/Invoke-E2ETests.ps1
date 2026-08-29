param (
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# --------------------------------------------------
# 1. QA SAFETY GATE
# --------------------------------------------------
if ($env:UNINSTALLER_E2E -ne "1") {
    Write-Error "QA SAFETY GATE FAILED: Environment variable UNINSTALLER_E2E must be set to '1' to run QA fixtures."
    exit 1
}

$TestRoot = "C:\Uninstaller-E2E-TestRoot"
$MarkerFile = Join-Path $TestRoot "QA-ENVIRONMENT.txt"

if (-not (Test-Path $TestRoot) -or -not (Test-Path $MarkerFile)) {
    Write-Error "QA SAFETY GATE FAILED: Test root or marker file does not exist. Run Setup-TestApps.ps1 first."
    exit 1
}

Write-Host "QA SAFETY GATE PASSED. Invoking tests..." -ForegroundColor Green

# --------------------------------------------------
# 2. RUN TESTS
# --------------------------------------------------
# In a real environment, this script would wrap the Uninstaller.App CLI
# or provide manual step-by-step instructions for the tester.

Write-Host "Uninstaller Phase 5G E2E Validation Script"
Write-Host "Please perform the following validations manually in the Uninstaller UI:"
Write-Host ""
Write-Host "1. Launch Uninstaller.App.exe"
Write-Host "2. Verify all E2E-App-* fixtures appear in the Discovery list."
Write-Host "3. Run 'Official Uninstall' on E2E-App-A."
Write-Host "4. Run 'Residual Analysis' on E2E-App-A."
Write-Host "5. Verify cleanup plan includes ProgramFiles and Registry, but rejects UserData."
Write-Host "6. Execute cleanup and confirm backup/deletion."
Write-Host "7. Go to History -> Recover and ensure items are restored."
Write-Host "8. For Crash testing, terminate the process during 'Executing' state and verify reconciliation on restart."
Write-Host ""
Write-Host "Refer to phase_5g_e2e_report.md to record your results."
