<#
.SYNOPSIS
    Build and packaging automation script for Vyntech Remote Manager.

.DESCRIPTION
    Builds, tests, and packages Remote Manager as a standalone portable EXE ZIP
    and an MSI installer using WiX Toolset v5.

.PARAMETER Target
    Build target: All, Build, Test, Exe, Msi, Package, Clean. Default: All.

.PARAMETER Configuration
    Build configuration: Release or Debug. Default: Release.

.PARAMETER Version
    Semantic version string (e.g. 1.0.0). Defaults to git tag or 1.0.0.

.PARAMETER OutputDir
    Directory where release artifacts will be saved. Default: artifacts.

.PARAMETER SkipTests
    Skip running automated tests before packaging.

.EXAMPLE
    .\build.ps1 -Target All
    .\build.ps1 -Target Exe
    .\build.ps1 -Target Msi -Version 1.2.0
    .\build.ps1 -Target Clean
#>

[CmdletBinding()]
param (
    [ValidateSet("All", "Build", "Test", "Exe", "Msi", "Package", "Clean")]
    [string]$Target = "All",

    [string]$Configuration = "Release",

    [string]$Version = "",

    [string]$OutputDir = "artifacts",

    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$artifactsPath = Join-Path $repoRoot $OutputDir
$appProject = Join-Path $repoRoot "src\RemoteManager.App\RemoteManager.App.csproj"
$testsProject = Join-Path $repoRoot "tests\RemoteManager.Tests\RemoteManager.Tests.csproj"
$wxsFile = Join-Path $repoRoot "packaging\Package.wxs"
$publishDir = Join-Path $repoRoot "src\RemoteManager.App\bin\$Configuration\net8.0-windows\win-x64\publish"

# Resolve Version
if ([string]::IsNullOrWhiteSpace($Version)) {
    try {
        $gitTag = git -C $repoRoot describe --tags --always 2>$null
        if ($gitTag -match '^v?(\d+\.\d+(\.\d+)?)') {
            $Version = $Matches[1]
            if ($Version.Split('.').Length -eq 2) { $Version = "$Version.0" }
        } else {
            $Version = "1.2.0"
        }
    } catch {
        $Version = "1.2.0"
    }
}
$Version = $Version.TrimStart('v')
# WiX requires version in format X.X.X[.X] with integers <= 65535
$versionParts = $Version.Split('.')
if ($versionParts.Length -eq 1) { $wixVersion = "$($versionParts[0]).0.0" }
elseif ($versionParts.Length -eq 2) { $wixVersion = "$($versionParts[0]).$($versionParts[1]).0" }
else { $wixVersion = "$($versionParts[0]).$($versionParts[1]).$($versionParts[2])" }

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Vyntech Remote Manager - Build & Packaging System" -ForegroundColor Cyan
Write-Host " Target:        $Target" -ForegroundColor Gray
Write-Host " Configuration: $Configuration" -ForegroundColor Gray
Write-Host " Version:       $wixVersion" -ForegroundColor Gray
Write-Host " Artifacts Dir: $artifactsPath" -ForegroundColor Gray
Write-Host "========================================================" -ForegroundColor Cyan

function Clean-Build {
    Write-Host "`n--> Cleaning output directories..." -ForegroundColor Yellow
    $dirsToClean = @(
        $artifactsPath,
        (Join-Path $repoRoot "dist"),
        (Join-Path $repoRoot ".wix"),
        (Join-Path $repoRoot "src\RemoteManager.App\bin"),
        (Join-Path $repoRoot "src\RemoteManager.App\obj"),
        (Join-Path $repoRoot "src\RemoteManager.Core\bin"),
        (Join-Path $repoRoot "src\RemoteManager.Core\obj"),
        (Join-Path $repoRoot "src\RemoteManager.Data\bin"),
        (Join-Path $repoRoot "src\RemoteManager.Data\obj"),
        (Join-Path $repoRoot "src\RemoteManager.Protocols\bin"),
        (Join-Path $repoRoot "src\RemoteManager.Protocols\obj"),
        (Join-Path $repoRoot "tests\RemoteManager.Tests\bin"),
        (Join-Path $repoRoot "tests\RemoteManager.Tests\obj")
    )
    foreach ($dir in $dirsToClean) {
        if (Test-Path $dir) {
            Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  Removed: $dir" -ForegroundColor DarkGray
        }
    }
    $msiFiles = Get-ChildItem -Path (Join-Path $repoRoot "packaging") -Include "*.msi","*.wixpdb" -Recurse -ErrorAction SilentlyContinue
    foreach ($f in $msiFiles) {
        Remove-Item $f.FullName -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Clean completed." -ForegroundColor Green
}

function Ensure-Tools {
    Write-Host "`n--> Restoring dotnet tools & WiX extensions..." -ForegroundColor Yellow
    & dotnet tool restore --tool-manifest (Join-Path $repoRoot "dotnet-tools.json")
    if ($LASTEXITCODE -ne 0) {
        # Fallback to local config
        & dotnet tool restore
    }
    # Ensure WiX UI extension is registered
    & dotnet wix extension add -g WixToolset.UI.wixext/5.0.2 2>$null
    Write-Host "Tools restored." -ForegroundColor Green
}

function Run-Tests {
    Write-Host "`n--> Running unit tests..." -ForegroundColor Yellow
    & dotnet test $testsProject --configuration $Configuration --nologo --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed!"
    }
    Write-Host "All tests passed." -ForegroundColor Green
}

function Build-Solution {
    Write-Host "`n--> Building solution ($Configuration)..." -ForegroundColor Yellow
    & dotnet build (Join-Path $repoRoot "RemoteManager.sln") --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed!"
    }
    Write-Host "Build succeeded." -ForegroundColor Green
}

function Publish-Exe {
    Write-Host "`n--> Publishing self-contained single-file win-x64 executable..." -ForegroundColor Yellow
    if (-not (Test-Path $artifactsPath)) {
        New-Item -ItemType Directory -Path $artifactsPath -Force | Out-Null
    }

    & dotnet publish $appProject `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:Version=$wixVersion `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed!"
    }

    $zipName = "RemoteManager-v$wixVersion-win-x64-portable.zip"
    $zipPath = Join-Path $artifactsPath $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    Write-Host "--> Creating portable ZIP: $zipName..." -ForegroundColor Yellow
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force
    Write-Host "Portable archive created: $zipPath" -ForegroundColor Green
}

function Build-Msi {
    Write-Host "`n--> Building WiX v5 MSI Installer..." -ForegroundColor Yellow
    if (-not (Test-Path $publishDir)) {
        Publish-Exe
    }
    if (-not (Test-Path $artifactsPath)) {
        New-Item -ItemType Directory -Path $artifactsPath -Force | Out-Null
    }

    Ensure-Tools

    $msiName = "RemoteManager-v$wixVersion-win-x64-Setup.msi"
    $msiPath = Join-Path $artifactsPath $msiName

    & dotnet wix build $wxsFile `
        -ext WixToolset.UI.wixext `
        -d Version=$wixVersion `
        -d SourceDir=$repoRoot `
        -d PublishDir=$publishDir `
        -arch x64 `
        -o $msiPath

    if ($LASTEXITCODE -ne 0) {
        throw "WiX MSI build failed!"
    }
    Write-Host "MSI Installer created: $msiPath" -ForegroundColor Green
}

function Generate-Checksums {
    Write-Host "`n--> Generating SHA256 checksums..." -ForegroundColor Yellow
    $checksumFile = Join-Path $artifactsPath "checksums.txt"
    if (Test-Path $checksumFile) { Remove-Item $checksumFile -Force }

    $artifactFiles = Get-ChildItem -Path $artifactsPath -File | Where-Object { $_.Name -ne "checksums.txt" -and $_.Extension -ne ".wixpdb" }
    $checksumEntries = @()

    foreach ($file in $artifactFiles) {
        $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLower()
        $entry = "$hash  $($file.Name)"
        $checksumEntries += $entry
        Write-Host "  $entry" -ForegroundColor DarkGray
    }

    $checksumEntries | Out-File -FilePath $checksumFile -Encoding utf8
    Write-Host "Checksums written to: $checksumFile" -ForegroundColor Green
}

# Main Execution Switch
try {
    switch ($Target) {
        "Clean" {
            Clean-Build
        }
        "Build" {
            Build-Solution
        }
        "Test" {
            Run-Tests
        }
        "Exe" {
            if (-not $SkipTests) { Run-Tests }
            Publish-Exe
            Generate-Checksums
        }
        "Msi" {
            if (-not $SkipTests) { Run-Tests }
            Publish-Exe
            Build-Msi
            Generate-Checksums
        }
        "Package" {
            if (-not $SkipTests) { Run-Tests }
            Publish-Exe
            Build-Msi
            Generate-Checksums
        }
        "All" {
            Clean-Build
            Ensure-Tools
            if (-not $SkipTests) { Run-Tests }
            Publish-Exe
            Build-Msi
            Generate-Checksums
        }
    }

    Write-Host "`n========================================================" -ForegroundColor Green
    Write-Host " Build succeeded!" -ForegroundColor Green
    if (Test-Path $artifactsPath) {
        Write-Host " Artifacts available in $($artifactsPath):" -ForegroundColor Cyan
        Get-ChildItem -Path $artifactsPath -File | ForEach-Object {
            $sizeMb = [math]::Round($_.Length / 1MB, 2)
            Write-Host ("   {0,-48} {1,8} MB" -f $_.Name, $sizeMb) -ForegroundColor White
        }
    }
    Write-Host "========================================================" -ForegroundColor Green
}
catch {
    Write-Host "`nERROR: $_" -ForegroundColor Red
    exit 1
}
