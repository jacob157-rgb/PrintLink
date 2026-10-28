param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$outputDir = Join-Path $scriptDir "output"
$tempPayloadDir = Join-Path $scriptDir "temp_payload"
$setupProjDir = Join-Path $scriptDir "PrintLink.Setup"
$payloadZipPath = Join-Path $setupProjDir "payload.zip"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   L-Net PrintLink - All-In-One Standalone Installer Build" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Configuration:  $Configuration"
Write-Host "Runtime:        $Runtime"
Write-Host "Self-Contained: $SelfContained"
Write-Host "Output:         $outputDir"

# Clean previous build artifacts
if (Test-Path $outputDir) {
    Get-ChildItem -Path $outputDir | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

if (Test-Path $tempPayloadDir) {
    Remove-Item -Path $tempPayloadDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $tempPayloadDir -Force | Out-Null

if (Test-Path $payloadZipPath) {
    Remove-Item -Path $payloadZipPath -Force -ErrorAction SilentlyContinue
}

$scArg = if ($SelfContained) { "--self-contained" } else { "--no-self-contained" }

# 1. Publish Desktop GUI into temporary payload directory
Write-Host "`n[1/4] Publishing PrintLink.Desktop ($Configuration)..." -ForegroundColor Yellow
$desktopProj = Join-Path $rootDir "src\PrintLink.Desktop\PrintLink.Desktop.csproj"
& dotnet publish $desktopProj -c $Configuration -r $Runtime $scArg -o $tempPayloadDir
if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed!" }

# 2. Publish Background Windows Service into temporary payload directory
Write-Host "`n[2/4] Publishing PrintLink.Service ($Configuration)..." -ForegroundColor Yellow
$serviceProj = Join-Path $rootDir "src\PrintLink.Service\PrintLink.Service.csproj"
& dotnet publish $serviceProj -c $Configuration -r $Runtime $scArg -o $tempPayloadDir
if ($LASTEXITCODE -ne 0) { throw "Service publish failed!" }

# 3. Compress payload directory directly into payload.zip for embedding
Write-Host "`n[3/4] Compressing application payload into embedded payload.zip..." -ForegroundColor Yellow
Compress-Archive -Path "$tempPayloadDir\*" -DestinationPath $payloadZipPath -Force
Remove-Item -Path $tempPayloadDir -Recurse -Force

# 4. Publish All-In-One Setup Executable (Embeds payload.zip inside the .exe)
Write-Host "`n[4/4] Compiling Standalone Single-File PrintLink-Setup-v1.0.0.exe..." -ForegroundColor Yellow
$setupProj = Join-Path $setupProjDir "PrintLink.Setup.csproj"
$setupTempDir = Join-Path $scriptDir "temp_setup"
& dotnet publish $setupProj -c $Configuration -r $Runtime $scArg -o $setupTempDir
if ($LASTEXITCODE -ne 0) { throw "Setup publish failed!" }

# Move final standalone executable to output
$setupExeSource = Join-Path $setupTempDir "PrintLink.Setup.exe"
$setupExeDest = Join-Path $outputDir "PrintLink-Setup-v1.0.0.exe"
Copy-Item $setupExeSource $setupExeDest -Force

# Cleanup temp files and intermediate zip
Remove-Item $setupTempDir -Recurse -Force
Remove-Item $payloadZipPath -Force -ErrorAction SilentlyContinue

$exeSizeMb = (Get-Item $setupExeDest).Length / 1MB

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "   BUILD SUCCEEDED! 100% STANDALONE SINGLE-FILE INSTALLER " -ForegroundColor Green
Write-Host "   -> File: $setupExeDest" -ForegroundColor White
Write-Host "   -> Size: $($exeSizeMb.ToString('F2')) MB" -ForegroundColor White
Write-Host "   -> Single-file self-extracting installer (No ZIP required!)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Green
