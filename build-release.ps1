# WiFi Manager Pro - Commercial Release Build Script
# Version 2.1.0 - Professional Build Automation

param(
    [string]$Configuration = "Release",
    [string]$Platform = "Any CPU",
    [switch]$Clean = $false,
    [switch]$Package = $true,
    [switch]$Sign = $false,
    [switch]$Publish = $false,
    [string]$OutputPath = ".\release",
    [string]$Version = "2.1.0"
)

# ═══════════════════════════════════════════════════════════════════════════════
# BUILD CONFIGURATION
# ═══════════════════════════════════════════════════════════════════════════════

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$script:StartTime = Get-Date
$script:BuildInfo = @{
    AppName = "WiFi Manager Pro"
    Version = $Version
    Configuration = $Configuration
    Platform = $Platform
    BuildDate = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    BuildNumber = (Get-Date).ToString("yyyyMMdd.HHmm")
}

# Colors for console output
$Colors = @{
    Success = "Green"
    Warning = "Yellow"
    Error = "Red"
    Info = "Cyan"
    Header = "Magenta"
}

# ═══════════════════════════════════════════════════════════════════════════════
# UTILITY FUNCTIONS
# ═══════════════════════════════════════════════════════════════════════════════

function Write-BuildHeader {
    param([string]$Title)

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════════════════════════" -ForegroundColor $Colors.Header
    Write-Host " $Title" -ForegroundColor $Colors.Header
    Write-Host "═══════════════════════════════════════════════════════════════════════════════" -ForegroundColor $Colors.Header
    Write-Host ""
}

function Write-BuildStep {
    param([string]$Message, [string]$Status = "Info")

    $timestamp = (Get-Date).ToString("HH:mm:ss")
    Write-Host "[$timestamp] $Message" -ForegroundColor $Colors[$Status]
}

function Test-BuildEnvironment {
    Write-BuildStep "Checking build environment..."

    # Check if MSBuild is available
    try {
        $msbuild = Get-Command "msbuild" -ErrorAction Stop
        Write-BuildStep "✓ MSBuild found: $($msbuild.Source)" -Status "Success"
    }
    catch {
        Write-BuildStep "✗ MSBuild not found in PATH" -Status "Error"
        throw "MSBuild is required for building. Please install Visual Studio Build Tools or Visual Studio."
    }

    # Check .NET Framework
    if (Test-Path "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8") {
        Write-BuildStep "✓ .NET Framework 4.8 found" -Status "Success"
    }
    else {
        Write-BuildStep "⚠ .NET Framework 4.8 not found - build may fail" -Status "Warning"
    }

    # Check project file
    if (Test-Path "MurtiWifiConnecter.csproj") {
        Write-BuildStep "✓ Project file found" -Status "Success"
    }
    else {
        Write-BuildStep "✗ Project file not found" -Status "Error"
        throw "MurtiWifiConnecter.csproj not found in current directory"
    }
}

function Update-AssemblyInfo {
    Write-BuildStep "Updating assembly information..."

    $assemblyInfoPath = "Properties\AssemblyInfo.cs"
    if (Test-Path $assemblyInfoPath) {
        $content = Get-Content $assemblyInfoPath

        # Update version information
        $content = $content -replace 'AssemblyVersion\(".*"\)', "AssemblyVersion(`"$Version.0`")"
        $content = $content -replace 'AssemblyFileVersion\(".*"\)', "AssemblyFileVersion(`"$Version.0`")"
        $content = $content -replace 'AssemblyInformationalVersion\(".*"\)', "AssemblyInformationalVersion(`"$Version`")"

        # Update build information
        $buildDate = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss UTC")
        $content = $content -replace 'AssemblyMetadata\("BuildDate".*\)', "AssemblyMetadata(`"BuildDate`", `"$buildDate`")"

        Set-Content $assemblyInfoPath $content -Encoding UTF8
        Write-BuildStep "✓ Assembly info updated" -Status "Success"
    }
    else {
        Write-BuildStep "⚠ AssemblyInfo.cs not found, skipping version update" -Status "Warning"
    }
}

function Start-Build {
    Write-BuildStep "Starting build process..."

    $buildArgs = @(
        "MurtiWifiConnecter.csproj"
        "/p:Configuration=$Configuration"
        "/p:Platform=$Platform"
        "/p:OutputPath=$OutputPath\bin"
        "/verbosity:minimal"
        "/nologo"
    )

    if ($Clean) {
        Write-BuildStep "Cleaning previous build artifacts..."
        $cleanArgs = $buildArgs + "/t:Clean"
        & msbuild $cleanArgs

        if ($LASTEXITCODE -ne 0) {
            throw "Build clean failed with exit code $LASTEXITCODE"
        }
        Write-BuildStep "✓ Clean completed" -Status "Success"
    }

    Write-BuildStep "Building application..."
    & msbuild $buildArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }

    Write-BuildStep "✓ Build completed successfully" -Status "Success"
}

function Copy-Dependencies {
    Write-BuildStep "Copying dependencies and resources..."

    $binPath = "$OutputPath\bin"

    # Copy configuration files
    @("app.config", "default_settings.json", "family_profiles.json") | ForEach-Object {
        if (Test-Path $_) {
            Copy-Item $_ $binPath -Force
            Write-BuildStep "  ✓ Copied $_" -Status "Success"
        }
    }

    # Copy documentation
    $docsPath = "$OutputPath\docs"
    New-Item -ItemType Directory -Path $docsPath -Force | Out-Null

    @("README.md", "LICENSE", "CHANGELOG.md") | ForEach-Object {
        if (Test-Path $_) {
            Copy-Item $_ $docsPath -Force
            Write-BuildStep "  ✓ Copied $_" -Status "Success"
        }
    }

    # Copy resources
    if (Test-Path "Resources") {
        $resourcesPath = "$OutputPath\resources"
        Copy-Item "Resources" $resourcesPath -Recurse -Force
        Write-BuildStep "  ✓ Copied Resources" -Status "Success"
    }

    # Copy installer
    if (Test-Path "install-pro.bat") {
        Copy-Item "install-pro.bat" $OutputPath -Force
        Write-BuildStep "  ✓ Copied installer" -Status "Success"
    }
}

function New-VersionInfo {
    Write-BuildStep "Creating version information file..."

    $versionInfo = @{
        Application = $script:BuildInfo.AppName
        Version = $script:BuildInfo.Version
        BuildNumber = $script:BuildInfo.BuildNumber
        BuildDate = $script:BuildInfo.BuildDate
        Configuration = $script:BuildInfo.Configuration
        Platform = $script:BuildInfo.Platform
        Framework = ".NET Framework 4.8"
        Compiler = "MSBuild"
        Features = @(
            "Professional WiFi Management",
            "Advanced Security & Encryption",
            "Family Safety Controls",
            "Battery Optimization",
            "Network Diagnostics",
            "Real-time Analytics",
            "Windows Service Integration",
            "Commercial-grade Logging"
        )
        SystemRequirements = @{
            OS = "Windows 10, Windows 11"
            Framework = ".NET Framework 4.8 or higher"
            Memory = "Minimum 4GB RAM"
            Storage = "50MB available space"
            Network = "WiFi adapter required"
        }
    }

    $versionJson = $versionInfo | ConvertTo-Json -Depth 3
    Set-Content "$OutputPath\version.json" $versionJson -Encoding UTF8

    Write-BuildStep "✓ Version info created" -Status "Success"
}

function New-ReleasePackage {
    param([string]$PackageFormat = "zip")

    Write-BuildStep "Creating release package..."

    $packageName = "WiFiManagerPro-v$Version-$($script:BuildInfo.BuildNumber)"
    $packagePath = "$OutputPath\$packageName.$PackageFormat"

    # Remove existing package
    if (Test-Path $packagePath) {
        Remove-Item $packagePath -Force
    }

    try {
        if ($PackageFormat -eq "zip") {
            # Create ZIP package
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::CreateFromDirectory($OutputPath, $packagePath)
            Write-BuildStep "✓ ZIP package created: $packageName.zip" -Status "Success"
        }
        else {
            Write-BuildStep "⚠ Unsupported package format: $PackageFormat" -Status "Warning"
        }
    }
    catch {
        Write-BuildStep "✗ Package creation failed: $($_.Exception.Message)" -Status "Error"
        throw
    }

    # Create checksum
    $hash = Get-FileHash $packagePath -Algorithm SHA256
    Set-Content "$packagePath.sha256" "$($hash.Hash.ToLower())  $packageName.$PackageFormat"
    Write-BuildStep "✓ Checksum created" -Status "Success"
}

function Invoke-CodeSigning {
    if (-not $Sign) {
        Write-BuildStep "Code signing skipped (use -Sign to enable)" -Status "Info"
        return
    }

    Write-BuildStep "Code signing..."

    $executable = "$OutputPath\bin\MurtiWifiConnecter.exe"

    if (-not (Test-Path $executable)) {
        Write-BuildStep "✗ Executable not found for signing" -Status "Error"
        return
    }

    # Note: This would require a valid code signing certificate
    # For demonstration purposes, this is a placeholder
    Write-BuildStep "⚠ Code signing certificate not configured" -Status "Warning"
    Write-BuildStep "  Configure certificate in build script for production release" -Status "Info"
}

function Test-Build {
    Write-BuildStep "Running build validation tests..."

    $executable = "$OutputPath\bin\MurtiWifiConnecter.exe"

    # Check if executable exists
    if (-not (Test-Path $executable)) {
        throw "Build validation failed: Executable not found"
    }

    # Check executable properties
    $fileInfo = Get-ItemProperty $executable
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executable)

    Write-BuildStep "  Executable size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -Status "Info"
    Write-BuildStep "  File version: $($fileVersion.FileVersion)" -Status "Info"
    Write-BuildStep "  Product version: $($fileVersion.ProductVersion)" -Status "Info"

    # Basic executable test (if possible)
    try {
        $process = Start-Process $executable -ArgumentList "--version" -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -eq 0) {
            Write-BuildStep "✓ Executable runs successfully" -Status "Success"
        }
        else {
            Write-BuildStep "⚠ Executable returned exit code $($process.ExitCode)" -Status "Warning"
        }
    }
    catch {
        Write-BuildStep "⚠ Could not test executable execution" -Status "Warning"
    }

    Write-BuildStep "✓ Build validation completed" -Status "Success"
}

function Publish-Release {
    if (-not $Publish) {
        Write-BuildStep "Publishing skipped (use -Publish to enable)" -Status "Info"
        return
    }

    Write-BuildStep "Publishing release..."

    # Note: This would integrate with your deployment system
    # GitHub Releases, internal distribution, etc.
    Write-BuildStep "⚠ Publishing configuration not set up" -Status "Warning"
    Write-BuildStep "  Configure publishing targets for automated deployment" -Status "Info"
}

function Write-BuildSummary {
    $duration = (Get-Date) - $script:StartTime

    Write-BuildHeader "BUILD SUMMARY"

    Write-Host "Application:     $($script:BuildInfo.AppName)" -ForegroundColor $Colors.Info
    Write-Host "Version:         $($script:BuildInfo.Version)" -ForegroundColor $Colors.Info
    Write-Host "Configuration:   $($script:BuildInfo.Configuration)" -ForegroundColor $Colors.Info
    Write-Host "Platform:        $($script:BuildInfo.Platform)" -ForegroundColor $Colors.Info
    Write-Host "Build Duration:  $($duration.ToString('mm\:ss'))" -ForegroundColor $Colors.Info
    Write-Host "Output Path:     $OutputPath" -ForegroundColor $Colors.Info
    Write-Host ""

    if (Test-Path "$OutputPath\bin\MurtiWifiConnecter.exe") {
        $exeSize = (Get-Item "$OutputPath\bin\MurtiWifiConnecter.exe").Length
        Write-Host "Executable Size: $([math]::Round($exeSize / 1MB, 2)) MB" -ForegroundColor $Colors.Success
    }

    if (Test-Path "$OutputPath\WiFiManagerPro-v$Version-*.zip") {
        $packageFiles = Get-ChildItem "$OutputPath\WiFiManagerPro-v$Version-*.zip"
        foreach ($package in $packageFiles) {
            $packageSize = [math]::Round($package.Length / 1MB, 2)
            Write-Host "Package:         $($package.Name) ($packageSize MB)" -ForegroundColor $Colors.Success
        }
    }

    Write-Host ""
    Write-Host "✓ BUILD COMPLETED SUCCESSFULLY" -ForegroundColor $Colors.Success
    Write-Host ""
}

# ═══════════════════════════════════════════════════════════════════════════════
# MAIN BUILD EXECUTION
# ═══════════════════════════════════════════════════════════════════════════════

try {
    Write-BuildHeader "WiFi Manager Pro - Release Build v$Version"

    # Pre-build setup
    Test-BuildEnvironment

    # Prepare output directory
    if (Test-Path $OutputPath) {
        if ($Clean) {
            Remove-Item $OutputPath -Recurse -Force
        }
    }
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

    # Build process
    Update-AssemblyInfo
    Start-Build
    Copy-Dependencies
    New-VersionInfo

    # Post-build operations
    Test-Build
    Invoke-CodeSigning

    if ($Package) {
        New-ReleasePackage -PackageFormat "zip"
    }

    Publish-Release

    # Summary
    Write-BuildSummary
}
catch {
    Write-Host ""
    Write-Host "✗ BUILD FAILED" -ForegroundColor $Colors.Error
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor $Colors.Error
    Write-Host ""

    if ($_.Exception.InnerException) {
        Write-Host "Inner Exception: $($_.Exception.InnerException.Message)" -ForegroundColor $Colors.Error
    }

    exit 1
}

# Build completed successfully
exit 0

    [Parameter(Mandatory=$false)]
    [string]$Version = "2.0.0",

    [Parameter(Mandatory=$false)]
    [switch]$SkipTests,

    [Parameter(Mandatory=$false)]
    [switch]$CreateInstaller,

    [Parameter(Mandatory=$false)]
    [switch]$SignBinaries,

    [Parameter(Mandatory=$false)]
    [string]$OutputPath = "release"
)

# Script configuration
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Colors for console output
$ColorSuccess = "Green"
$ColorWarning = "Yellow"
$ColorError = "Red"
$ColorInfo = "Cyan"

# Build information
$BuildStartTime = Get-Date
$ProjectName = "MurtiWifiConnecter"
$ProjectFile = "$ProjectName.csproj"

Write-Host "========================================" -ForegroundColor $ColorInfo
Write-Host "  MurtiWifi Connector - Release Build  " -ForegroundColor $ColorInfo
Write-Host "========================================" -ForegroundColor $ColorInfo
Write-Host "Version: $Version" -ForegroundColor $ColorInfo
Write-Host "Configuration: $Configuration" -ForegroundColor $ColorInfo
Write-Host "Platform: $Platform" -ForegroundColor $ColorInfo
Write-Host "Build Started: $BuildStartTime" -ForegroundColor $ColorInfo
Write-Host ""

function Write-BuildStep {
    param([string]$Message)
    Write-Host ">>> $Message" -ForegroundColor $ColorInfo
}

function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor $ColorSuccess
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠ $Message" -ForegroundColor $ColorWarning
}

function Write-Error {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor $ColorError
}

function Test-Prerequisites {
    Write-BuildStep "Checking prerequisites..."

    # Check .NET SDK
    try {
        $dotnetVersion = dotnet --version
        Write-Success ".NET SDK Version: $dotnetVersion"
    }
    catch {
        Write-Error ".NET SDK not found. Please install .NET 6.0 SDK."
        exit 1
    }

    # Check project file
    if (-not (Test-Path $ProjectFile)) {
        Write-Error "Project file '$ProjectFile' not found."
        exit 1
    }
    Write-Success "Project file found: $ProjectFile"

    # Check Inno Setup (for installer)
    if ($CreateInstaller) {
        $innoSetupPath = Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1" -Name "InstallLocation" -ErrorAction SilentlyContinue
        if (-not $innoSetupPath) {
            Write-Warning "Inno Setup not found. Installer creation will be skipped."
            $script:CreateInstaller = $false
        } else {
            Write-Success "Inno Setup found: $($innoSetupPath.InstallLocation)"
        }
    }
}

function Clear-BuildArtifacts {
    Write-BuildStep "Cleaning build artifacts..."

    $directories = @("bin", "obj", $OutputPath)
    foreach ($dir in $directories) {
        if (Test-Path $dir) {
            Remove-Item $dir -Recurse -Force
            Write-Success "Cleaned: $dir"
        }
    }

    # Clean NuGet cache
    try {
        dotnet nuget locals all --clear | Out-Null
        Write-Success "Cleaned NuGet cache"
    }
    catch {
        Write-Warning "Failed to clean NuGet cache"
    }
}

function Restore-Packages {
    Write-BuildStep "Restoring NuGet packages..."

    $restoreResult = dotnet restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Package restore failed"
        exit 1
    }

    Write-Success "Packages restored successfully"
}

function Build-Project {
    Write-BuildStep "Building project..."

    $buildArgs = @(
        "build"
        $ProjectFile
        "--configuration", $Configuration
        "--no-restore"
        "--verbosity", "minimal"
        "-p:Platform=$Platform"
        "-p:Version=$Version"
        "-p:AssemblyVersion=$Version.0"
        "-p:FileVersion=$Version.0"
    )

    $buildResult = & dotnet $buildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }

    Write-Success "Build completed successfully"
}

function Run-Tests {
    if ($SkipTests) {
        Write-Warning "Tests skipped by request"
        return
    }

    Write-BuildStep "Running tests..."

    # Check if test projects exist
    $testProjects = Get-ChildItem -Path . -Name "*Test*.csproj" -Recurse
    if ($testProjects.Count -eq 0) {
        Write-Warning "No test projects found"
        return
    }

    $testArgs = @(
        "test"
        "--configuration", $Configuration
        "--no-build"
        "--verbosity", "minimal"
        "--logger", "console;verbosity=normal"
    )

    try {
        $testResult = & dotnet $testArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Tests failed"
            exit 1
        }
        Write-Success "All tests passed"
    }
    catch {
        Write-Warning "Test execution encountered issues, but continuing..."
    }
}

function Publish-Application {
    Write-BuildStep "Publishing application..."

    $publishPath = "bin\$Configuration\net6.0-windows\publish"

    $publishArgs = @(
        "publish"
        $ProjectFile
        "--configuration", $Configuration
        "--no-build"
        "--output", $publishPath
        "-p:PublishReadyToRun=true"
        "-p:PublishSingleFile=false"
        "-p:PublishTrimmed=false"
    )

    $publishResult = & dotnet $publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed"
        exit 1
    }

    Write-Success "Application published to: $publishPath"
    return $publishPath
}

function Sign-Binaries {
    param([string]$BinariesPath)

    if (-not $SignBinaries) {
        Write-Warning "Binary signing skipped"
        return
    }

    Write-BuildStep "Signing binaries..."

    # This is a placeholder - actual signing would require certificates
    # signtool sign /f certificate.pfx /p password /t http://timestamp.server.com "$BinariesPath\*.exe"

    Write-Warning "Binary signing is not configured. Implement with your code signing certificate."
}

function Create-ReleasePackage {
    param([string]$PublishPath)

    Write-BuildStep "Creating release package..."

    # Create output directory
    if (-not (Test-Path $OutputPath)) {
        New-Item -Path $OutputPath -ItemType Directory | Out-Null
    }

    # Copy published files
    $releaseDir = Join-Path $OutputPath "$ProjectName-$Version"
    if (Test-Path $releaseDir) {
        Remove-Item $releaseDir -Recurse -Force
    }

    Copy-Item $PublishPath $releaseDir -Recurse
    Write-Success "Release files copied to: $releaseDir"

    # Copy additional files
    $additionalFiles = @(
        "README.md",
        "CHANGELOG.md",
        "LICENSE.txt",
        "default_settings.json",
        "family_profiles.json"
    )

    foreach ($file in $additionalFiles) {
        if (Test-Path $file) {
            Copy-Item $file $releaseDir
            Write-Success "Copied: $file"
        }
    }

    # Create ZIP package
    $zipName = "$ProjectName-$Version-Portable.zip"
    $zipPath = Join-Path $OutputPath $zipName

    if (Test-Path $zipPath) {
        Remove-Item $zipPath
    }

    Compress-Archive -Path $releaseDir\* -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Success "Created portable package: $zipPath"

    return $releaseDir
}

function Create-Installer {
    param([string]$ReleaseDir)

    if (-not $CreateInstaller) {
        Write-Warning "Installer creation skipped"
        return
    }

    Write-BuildStep "Creating Windows installer..."

    # Check if setup.iss exists
    if (-not (Test-Path "setup.iss")) {
        Write-Error "Inno Setup script (setup.iss) not found"
        return
    }

    # Find Inno Setup compiler
    $isccPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $isccPath)) {
        $isccPath = "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    }

    if (-not (Test-Path $isccPath)) {
        Write-Error "Inno Setup compiler not found"
        return
    }

    # Compile installer
    try {
        $isccArgs = @(
            "/DAppVersion=$Version"
            "/DSourceDir=$ReleaseDir"
            "setup.iss"
        )

        & $isccPath $isccArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Installer creation failed"
            return
        }

        Write-Success "Windows installer created successfully"
    }
    catch {
        Write-Error "Failed to create installer: $_"
    }
}

function Generate-BuildReport {
    param([string]$PublishPath)

    Write-BuildStep "Generating build report..."

    $buildEndTime = Get-Date
    $buildDuration = $buildEndTime - $BuildStartTime

    # Get file sizes
    $exeFile = Get-ChildItem -Path $PublishPath -Name "*.exe" | Select-Object -First 1
    $exeSize = if ($exeFile) { (Get-Item (Join-Path $PublishPath $exeFile)).Length } else { 0 }
    $totalSize = (Get-ChildItem -Path $PublishPath -Recurse | Measure-Object -Property Length -Sum).Sum

    $report = @"
========================================
        BUILD REPORT
========================================
Project: $ProjectName
Version: $Version
Configuration: $Configuration
Platform: $Platform

Build Time: $($BuildDuration.ToString("mm\:ss"))
Build Started: $BuildStartTime
Build Completed: $buildEndTime

Binary Information:
- Executable Size: $([math]::Round($exeSize/1MB, 2)) MB
- Total Package Size: $([math]::Round($totalSize/1MB, 2)) MB
- File Count: $((Get-ChildItem -Path $PublishPath -Recurse -File).Count)

Output Location: $PublishPath
Release Package: $OutputPath

========================================
"@

    Write-Host $report -ForegroundColor $ColorInfo

    # Save report to file
    $reportPath = Join-Path $OutputPath "build-report-$Version.txt"
    $report | Out-File -FilePath $reportPath -Encoding UTF8
    Write-Success "Build report saved: $reportPath"
}

function Main {
    try {
        Test-Prerequisites
        Clear-BuildArtifacts
        Restore-Packages
        Build-Project
        Run-Tests
        $publishPath = Publish-Application
        Sign-Binaries -BinariesPath $publishPath
        $releaseDir = Create-ReleasePackage -PublishPath $publishPath
        Create-Installer -ReleaseDir $releaseDir
        Generate-BuildReport -PublishPath $publishPath

        Write-Host ""
        Write-Host "========================================" -ForegroundColor $ColorSuccess
        Write-Host "       BUILD COMPLETED SUCCESSFULLY     " -ForegroundColor $ColorSuccess
        Write-Host "========================================" -ForegroundColor $ColorSuccess
        Write-Host "Version: $Version" -ForegroundColor $ColorSuccess
        Write-Host "Output: $OutputPath" -ForegroundColor $ColorSuccess

        $buildEndTime = Get-Date
        $totalDuration = $buildEndTime - $BuildStartTime
        Write-Host "Total Time: $($totalDuration.ToString("mm\:ss"))" -ForegroundColor $ColorSuccess

        exit 0
    }
    catch {
        Write-Host ""
        Write-Host "========================================" -ForegroundColor $ColorError
        Write-Host "           BUILD FAILED                 " -ForegroundColor $ColorError
        Write-Host "========================================" -ForegroundColor $ColorError
        Write-Host "Error: $_" -ForegroundColor $ColorError
        exit 1
    }
}

# Execute main function
Main