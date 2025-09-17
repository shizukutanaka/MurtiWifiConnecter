# Simple build script for MurtiWifiConnecter
param(
    [string]$Configuration = "Release"
)

Write-Host "Building MurtiWifiConnecter..." -ForegroundColor Green

# Clean previous builds
Remove-Item -Path "bin\$Configuration" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "obj\$Configuration" -Recurse -Force -ErrorAction SilentlyContinue

# Build the project
$buildArgs = @(
    "build",
    "-c", $Configuration,
    "--nologo",
    "-p:PublishSingleFile=true",
    "-p:PublishReadyToRun=true",
    "-p:EnableCompressionInSingleFile=true"
)

try {
    & dotnet $buildArgs

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Build successful!" -ForegroundColor Green
        Write-Host "Output: bin\$Configuration\net8.0-windows\win-x64\" -ForegroundColor Cyan
    } else {
        Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
} catch {
    Write-Host "Build error: $_" -ForegroundColor Red
    exit 1
}

# Publish for distribution
if ($Configuration -eq "Release") {
    Write-Host "`nPublishing release build..." -ForegroundColor Green

    $publishArgs = @(
        "publish",
        "-c", "Release",
        "--nologo",
        "-p:PublishSingleFile=true",
        "-p:PublishReadyToRun=true",
        "-p:EnableCompressionInSingleFile=true",
        "-o", "publish"
    )

    & dotnet $publishArgs

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Publish successful!" -ForegroundColor Green
        Write-Host "Published to: publish\MurtiWifiConnecter.exe" -ForegroundColor Cyan
    }
}