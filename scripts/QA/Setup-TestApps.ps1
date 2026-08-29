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

if (-not (Test-Path $TestRoot)) {
    if ($Force) {
        New-Item -Path $TestRoot -ItemType Directory -Force | Out-Null
    } else {
        Write-Error "QA SAFETY GATE FAILED: Test root $TestRoot does not exist."
        exit 1
    }
}

if (-not (Test-Path $MarkerFile)) {
    if ($Force) {
        Set-Content -Path $MarkerFile -Value "This environment is approved for Uninstaller destructive testing."
    } else {
        Write-Error "QA SAFETY GATE FAILED: Marker file $MarkerFile does not exist."
        exit 1
    }
}

Write-Host "QA SAFETY GATE PASSED. Setting up isolated fixtures in $TestRoot" -ForegroundColor Green

# --------------------------------------------------
# 2. TEST APPLICATION MATRIX SETUP
# --------------------------------------------------

$ProgramFilesSim = Join-Path $TestRoot "ProgramFiles"
$ProgramDataSim = Join-Path $TestRoot "ProgramData"
$AppDataSim = Join-Path $TestRoot "AppData"
$RegistrySim = "HKCU:\Software\Uninstaller-E2E"
$ShortcutsSim = Join-Path $TestRoot "Shortcuts"
$UserDataSim = Join-Path $TestRoot "UserData"

New-Item -Path $ProgramFilesSim -ItemType Directory -Force | Out-Null
New-Item -Path $ProgramDataSim -ItemType Directory -Force | Out-Null
New-Item -Path $AppDataSim -ItemType Directory -Force | Out-Null
New-Item -Path $ShortcutsSim -ItemType Directory -Force | Out-Null
New-Item -Path $UserDataSim -ItemType Directory -Force | Out-Null

if (-not (Test-Path $RegistrySim)) {
    New-Item -Path $RegistrySim -Force | Out-Null
}

function Create-AppFixture {
    param (
        [string]$Publisher,
        [string]$AppName,
        [switch]$IncludeProgramFiles,
        [switch]$IncludeProgramData,
        [switch]$IncludeAppData,
        [switch]$IncludeRegistry,
        [switch]$IncludeShortcuts,
        [switch]$IncludeUserData
    )

    Write-Host "Setting up fixture: $Publisher \ $AppName" -ForegroundColor Cyan

    $AppUninstallKey = Join-Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall" $AppName

    if (-not (Test-Path $AppUninstallKey)) {
        New-Item -Path $AppUninstallKey -Force | Out-Null
    }

    Set-ItemProperty -Path $AppUninstallKey -Name "DisplayName" -Value $AppName
    Set-ItemProperty -Path $AppUninstallKey -Name "Publisher" -Value $Publisher
    Set-ItemProperty -Path $AppUninstallKey -Name "DisplayVersion" -Value "1.0.0"
    Set-ItemProperty -Path $AppUninstallKey -Name "UninstallString" -Value "cmd.exe /c echo 'Mock Uninstall $AppName'"

    if ($IncludeProgramFiles) {
        $pfPath = Join-Path $ProgramFilesSim (Join-Path $Publisher $AppName)
        New-Item -Path $pfPath -ItemType Directory -Force | Out-Null
        Set-Content -Path (Join-Path $pfPath "app.exe") -Value "MZ..."
        Set-ItemProperty -Path $AppUninstallKey -Name "InstallLocation" -Value $pfPath
    }

    if ($IncludeProgramData) {
        $pdPath = Join-Path $ProgramDataSim (Join-Path $Publisher $AppName)
        New-Item -Path $pdPath -ItemType Directory -Force | Out-Null
        Set-Content -Path (Join-Path $pdPath "data.db") -Value "database..."
    }

    if ($IncludeAppData) {
        $adPath = Join-Path $AppDataSim (Join-Path $Publisher $AppName)
        New-Item -Path $adPath -ItemType Directory -Force | Out-Null
        Set-Content -Path (Join-Path $adPath "config.json") -Value "{}"
    }

    if ($IncludeRegistry) {
        $regPath = Join-Path $RegistrySim (Join-Path $Publisher $AppName)
        New-Item -Path $regPath -Force | Out-Null
        Set-ItemProperty -Path $regPath -Name "LicenseKey" -Value "12345-ABCDE"
    }

    if ($IncludeShortcuts) {
        $shortcutPath = Join-Path $ShortcutsSim "$AppName.lnk"
        $WshShell = New-Object -ComObject WScript.Shell
        $Shortcut = $WshShell.CreateShortcut($shortcutPath)
        $Shortcut.TargetPath = Join-Path $ProgramFilesSim (Join-Path $Publisher $AppName)
        $Shortcut.Save()
    }

    if ($IncludeUserData) {
        $docsPath = Join-Path $UserDataSim "Documents"
        New-Item -Path $docsPath -ItemType Directory -Force | Out-Null
        Set-Content -Path (Join-Path $docsPath "$AppName-Project.txt") -Value "User data for $AppName"
    }
}

# A. Simple EXE
Create-AppFixture -Publisher "E2E-Publisher-Simple" -AppName "E2E-App-A" -IncludeProgramFiles -IncludeRegistry

# B. MSI-like (Just full metadata)
Create-AppFixture -Publisher "E2E-Publisher-MSI" -AppName "E2E-App-B" -IncludeProgramFiles -IncludeProgramData -IncludeAppData -IncludeRegistry -IncludeShortcuts

# I. Shared Publisher
Create-AppFixture -Publisher "E2E-Publisher-Shared" -AppName "E2E-App-C1" -IncludeProgramFiles -IncludeAppData
Create-AppFixture -Publisher "E2E-Publisher-Shared" -AppName "E2E-App-C2" -IncludeProgramFiles -IncludeAppData

# O. User Data
Create-AppFixture -Publisher "E2E-Publisher-User" -AppName "E2E-App-D" -IncludeProgramFiles -IncludeUserData

Write-Host "Setup complete." -ForegroundColor Green
