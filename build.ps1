<#
.SYNOPSIS
  Builds a self-contained, single-file Windows build of Reddit Wallpaper Rotator
  and (optionally) compiles the Inno Setup installer.

.EXAMPLE
  ./build.ps1                # publish only
  ./build.ps1 -Installer     # publish + build installer (needs Inno Setup)
#>
param(
    [switch]$Installer,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$proj = Join-Path $root "src\WallpaperReddit\WallpaperReddit.csproj"
$publishDir = Join-Path $root "publish"

Write-Host "==> Checking for .NET SDK..." -ForegroundColor Cyan
$sdk = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $sdk -or -not (dotnet --list-sdks 2>$null)) {
    Write-Host "The .NET 8 SDK is required to build. Install it with:" -ForegroundColor Yellow
    Write-Host "    winget install Microsoft.DotNet.SDK.8" -ForegroundColor Yellow
    throw "No .NET SDK found."
}

Write-Host "==> Cleaning previous publish..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "==> Publishing ($Configuration / $Runtime, self-contained single file)..." -ForegroundColor Cyan
dotnet publish $proj `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $publishDir "WallpaperReddit.exe"
Write-Host "==> Published: $exe" -ForegroundColor Green

if ($Installer) {
    Write-Host "==> Building installer with Inno Setup..." -ForegroundColor Cyan
    $isccPath = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
    if (-not $isccPath) {
        $guesses = @(
            "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            "C:\Program Files\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        )
        $isccPath = $guesses | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $isccPath) {
        Write-Host "Inno Setup (ISCC.exe) not found. Install it with:" -ForegroundColor Yellow
        Write-Host "    winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
        throw "ISCC.exe not found."
    }
    & $isccPath (Join-Path $root "installer\setup.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }
    Write-Host "==> Installer written to installer\Output" -ForegroundColor Green
}

Write-Host "Done." -ForegroundColor Green
