# Enhanced build script for MurtiWifiConnecter v3.0.0
param(
    [string]$Configuration = "Release",
    [string[]]$Platforms = @("win-x64", "linux-x64", "osx-x64"),
    [switch]$BuildGUI = $true,
    [switch]$RunTests = $false
)

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  MurtiWifiConnecter v3.0.0 Build Script" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# Function to build for specific platform
function Build-Project {
    param([string]$Platform, [string]$ProjectFile)

    Write-Host "`nBuilding $ProjectFile for $Platform..." -ForegroundColor Green

    # Clean previous builds
    Remove-Item -Path "bin\$Configuration" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "obj\$Configuration" -Recurse -Force -ErrorAction SilentlyContinue

    # Build the project
    $buildArgs = @(
        "build",
        $ProjectFile,
        "-c", $Configuration,
        "--nologo",
        "-p:PublishSingleFile=true",
        "-p:PublishReadyToRun=true",
        "-p:EnableCompressionInSingleFile=true"
    )

    if ($Platform -ne "win-x64") {
        $buildArgs += @("-r", $Platform)
    }

    & dotnet $buildArgs

    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Build successful for $Platform" -ForegroundColor Green
        return $true
    } else {
        Write-Host "✗ Build failed for $Platform with exit code $LASTEXITCODE" -ForegroundColor Red
        return $false
    }
}

# Function to publish for distribution
function Publish-Project {
    param([string]$ProjectFile, [string]$OutputName)

    Write-Host "`nPublishing $ProjectFile..." -ForegroundColor Green

    $publishArgs = @(
        "publish",
        $ProjectFile,
        "-c", "Release",
        "--nologo",
        "-p:PublishSingleFile=true",
        "-p:PublishReadyToRun=true",
        "-p:EnableCompressionInSingleFile=true",
        "-o", "publish"
    )

    & dotnet $publishArgs

    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Publish successful!" -ForegroundColor Green
        Write-Host "  Published to: publish\$OutputName.exe" -ForegroundColor Cyan
    }
}

# Build main console application for all platforms
Write-Host "`nBuilding Main Console Application..." -ForegroundColor Yellow
$mainBuildSuccess = $true

foreach ($platform in $Platforms) {
    $success = Build-Project -Platform $platform -ProjectFile "MurtiWifiConnecter.csproj"
    $mainBuildSuccess = $mainBuildSuccess -and $success
}

# Build GUI application (Windows only)
if ($BuildGUI -and ($Platforms -contains "win-x64")) {
    Write-Host "`nBuilding GUI Application..." -ForegroundColor Yellow
    $guiSuccess = Build-Project -Platform "win-x64" -ProjectFile "MurtiWifiConnecter.GUI.csproj"

    if ($guiSuccess) {
        Publish-Project -ProjectFile "MurtiWifiConnecter.GUI.csproj" -OutputName "MurtiWifiConnecter.GUI"
    }
}

# Run tests if requested
if ($RunTests) {
    Write-Host "`nRunning Test Suite..." -ForegroundColor Yellow

    & .\run-tests.bat

    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ All tests passed!" -ForegroundColor Green
    } else {
        Write-Host "⚠ Some tests failed" -ForegroundColor Yellow
    }
}

# Final status
Write-Host "`n============================================" -ForegroundColor Cyan

if ($mainBuildSuccess) {
    Write-Host "✓ Main application build completed successfully!" -ForegroundColor Green
    Write-Host "  Supported platforms: $($Platforms -join ', ')" -ForegroundColor Cyan

    if ($BuildGUI) {
        Write-Host "✓ GUI application also built successfully!" -ForegroundColor Green
    }

    Write-Host "`nBuild outputs:" -ForegroundColor Yellow
    Write-Host "  Console App: publish\MurtiWifiConnecter.exe" -ForegroundColor Cyan
    if ($BuildGUI) {
        Write-Host "  GUI App: publish\MurtiWifiConnecter.GUI.exe" -ForegroundColor Cyan
    }
    Write-Host "  Test Results: run-tests.bat" -ForegroundColor Cyan

} else {
    Write-Host "✗ Build failed for one or more platforms" -ForegroundColor Red
    exit 1
}

Write-Host "============================================" -ForegroundColor Cyan