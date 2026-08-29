param (
    [string]$Version = "0.19.0",
    [switch]$NoClean
)

$ErrorActionPreference = "Stop"

$ProjectFile = "C:\Users\USER\Documents\Uninstaller\src\Uninstaller.App\Uninstaller.App.csproj"
$OutputDirectory = "C:\Users\USER\Documents\Uninstaller\Publish"
$Configuration = "Release"
$TargetFramework = "net10.0-windows"
$RuntimeIdentifier = "win-x64"

Write-Host "Uninstaller Release Publish Script" -ForegroundColor Cyan
Write-Host "Version: $Version"
Write-Host "Runtime: $RuntimeIdentifier (Self-Contained)"

if (-not $NoClean) {
    if (Test-Path $OutputDirectory) {
        Write-Host "Cleaning output directory..."
        Remove-Item -Path "$OutputDirectory\*" -Recurse -Force
    } else {
        New-Item -Path $OutputDirectory -ItemType Directory -Force | Out-Null
    }
}

Write-Host "Restoring..."
dotnet restore $ProjectFile

Write-Host "Publishing..."
dotnet publish $ProjectFile `
    --configuration $Configuration `
    --framework $TargetFramework `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $OutputDirectory `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "Publish completed successfully: $OutputDirectory" -ForegroundColor Green
