<#
    Patient Consent Hub - build script

    Usage (from the repository root, in PowerShell):

        .\build.ps1                 # publish the application
        .\build.ps1 -Installer      # publish, then build the setup EXE

    Requirements:
        - .NET 8 SDK           https://dotnet.microsoft.com/download
        - Inno Setup 6         https://jrsoftware.org/isdl.php   (only for -Installer)
        - ffmpeg.exe placed in src\PatientConsentHub\Tools\ffmpeg\
#>

[CmdletBinding()]
param(
    [switch]$Installer,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$publishDir = Join-Path $root "publish"
$ffmpeg = Join-Path $root "src\PatientConsentHub\Tools\ffmpeg\ffmpeg.exe"

Write-Host "=== Patient Consent Hub build ===" -ForegroundColor Cyan

if (-not (Test-Path $ffmpeg)) {
    Write-Warning "ffmpeg.exe was not found at: $ffmpeg"
    Write-Warning "The application will build, but recording will not work until it is added."
    Write-Warning "See src\PatientConsentHub\Tools\ffmpeg\PLACE_FFMPEG_HERE.txt"
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "`nPublishing ($Configuration, win-x64, self-contained)..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\PatientConsentHub\PatientConsentHub.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "`nPublished to: $publishDir" -ForegroundColor Green

if (-not $Installer) {
    Write-Host "Run .\build.ps1 -Installer to produce PatientConsentHub_Setup.exe" -ForegroundColor DarkGray
    return
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) { throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php" }

Write-Host "`nBuilding the installer..." -ForegroundColor Cyan
& $iscc (Join-Path $root "installer\PatientConsentHub.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }

Write-Host "`nInstaller ready: installer\Output\PatientConsentHub_Setup.exe" -ForegroundColor Green
