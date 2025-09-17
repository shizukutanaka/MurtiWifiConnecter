# WiFi Manager Pro - Deployment Guide
**Version 2.1.0 - Commercial Release**

## Overview

This guide provides comprehensive instructions for deploying WiFi Manager Pro in various environments, from individual installations to enterprise-wide deployments.

## Prerequisites

### System Requirements
- **Operating System**: Windows 10 (1909) or Windows 11
- **Framework**: .NET 6.0 Runtime (Windows Desktop)
- **Memory**: Minimum 4GB RAM (8GB recommended)
- **Storage**: 100MB available disk space
- **Network**: WiFi adapter required
- **Privileges**: Administrator rights for installation

### Development Requirements (for building from source)
- **Visual Studio 2022** or **Visual Studio Build Tools 2022**
- **.NET 6.0 SDK**
- **Windows SDK** (latest version)
- **PowerShell 5.0** or later (for build scripts)

## Quick Installation

### For End Users

1. **Download the installer**:
   ```
   WiFiManagerPro-v2.1.0-Installer.exe
   ```

2. **Run as Administrator**:
   - Right-click the installer
   - Select "Run as administrator"
   - Follow the installation wizard

3. **Launch the application**:
   - Desktop shortcut: "WiFi Manager Pro"
   - Start Menu: WiFi Manager Pro
   - Command line: `wifimanagerpro`

### Silent Installation
```batch
WiFiManagerPro-v2.1.0-Installer.exe /S /D=C:\Program Files\WiFiManagerPro
```

## Building from Source

### Prerequisites Check
```powershell
# Check .NET SDK
dotnet --version

# Check MSBuild
msbuild -version

# Check Windows SDK
dir "C:\Program Files (x86)\Windows Kits\10\bin"
```

### Build Process

1. **Clone the repository**:
   ```bash
   git clone https://github.com/MurtiWifi/WiFiManagerPro.git
   cd WiFiManagerPro
   ```

2. **Restore dependencies**:
   ```powershell
   dotnet restore MurtiWifiConnecter.csproj
   ```

3. **Build the application**:
   ```powershell
   # Using PowerShell build script (recommended)
   .\build-release.ps1 -Configuration Release -Clean -Package

   # Or using dotnet CLI
   dotnet build MurtiWifiConnecter.csproj --configuration Release

   # Or using MSBuild
   msbuild MurtiWifiConnecter.csproj /p:Configuration=Release
   ```

### Build Script Options
```powershell
.\build-release.ps1 [options]

Options:
  -Configuration     Build configuration (Debug/Release)
  -Platform         Target platform (Any CPU/x64/x86)
  -Clean            Clean before building
  -Package          Create deployment package
  -Sign             Enable code signing
  -Publish          Publish to configured targets
  -OutputPath       Output directory
  -Version          Version number
```

### Build Output
```
release/
├── bin/                     # Application binaries
│   ├── MurtiWifiConnecter.exe
│   ├── *.dll
│   └── config files
├── docs/                    # Documentation
├── resources/               # Application resources
├── install-pro.bat         # Installer script
├── version.json            # Build information
└── WiFiManagerPro-v2.1.0.zip  # Distribution package
```

## Enterprise Deployment

### Group Policy Deployment

1. **Create deployment share**:
   ```batch
   net share WiFiManagerPro=C:\Deployment\WiFiManagerPro /grant:everyone,read
   ```

2. **Configure Group Policy**:
   - Computer Configuration → Software Settings → Software Installation
   - New → Package → Browse to installer
   - Deployment Method: Assigned or Published

3. **Registry-based configuration**:
   ```registry
   [HKEY_LOCAL_MACHINE\SOFTWARE\Policies\MurtiWifi\WiFiManagerPro]
   "AutoStart"=dword:00000001
   "EnableFamilyProfiles"=dword:00000001
   "DefaultTheme"="Auto"
   "EnableDiagnostics"=dword:00000001
   ```

### SCCM Deployment

1. **Create Application**:
   - Application Name: WiFi Manager Pro
   - Installation Program: `install-pro.bat /S`
   - Detection Method: Registry key or file existence

2. **Requirements**:
   - Operating System: Windows 10/11
   - Primary Device: Yes
   - Free Disk Space: 100 MB

3. **User Experience**:
   - Installation Behavior: Install for system
   - Logon Requirement: Whether or not a user is logged on
   - Installation Program Visibility: Hidden

### Active Directory Integration

1. **Create service account**:
   ```powershell
   New-ADUser -Name "WiFiManagerProSvc" -AccountPassword (ConvertTo-SecureString "SecurePassword123!" -AsPlainText -Force) -Enabled $true
   ```

2. **Grant permissions**:
   ```powershell
   # Network configuration permissions
   Add-LocalGroupMember -Group "Network Configuration Operators" -Member "DOMAIN\WiFiManagerProSvc"
   ```

3. **Configure service**:
   ```batch
   sc config WiFiManagerProService obj=DOMAIN\WiFiManagerProSvc password=SecurePassword123!
   ```

## Configuration Management

### Default Configuration
```json
{
  "wifi": {
    "scanIntervalSeconds": 30,
    "connectionTimeoutSeconds": 15,
    "maxRetryAttempts": 3,
    "autoReconnect": true
  },
  "ui": {
    "theme": "auto",
    "refreshIntervalMs": 2000,
    "showDetailedNetworkInfo": true,
    "minimizeToTray": true
  },
  "security": {
    "encryptPasswords": true,
    "warnOnInsecureNetworks": true,
    "auditSecurityEvents": true
  },
  "family": {
    "enableFamilyProfiles": false,
    "defaultTimeLimit": "02:00:00",
    "requireParentalApproval": true
  }
}
```

### Environment-Specific Configuration

#### Corporate Environment
```json
{
  "wifi": {
    "scanIntervalSeconds": 60,
    "preferredSecurityTypes": ["WPA3", "WPA2-Enterprise"]
  },
  "security": {
    "requirePasswordForSettings": true,
    "maxPasswordRetries": 3
  },
  "advanced": {
    "enableDiagnostics": true,
    "detailedLogging": true,
    "enableAnalytics": false
  }
}
```

#### Home Environment
```json
{
  "family": {
    "enableFamilyProfiles": true,
    "profiles": [
      {
        "name": "Children",
        "timeLimit": "01:30:00",
        "blockedNetworks": ["GuestNetwork"],
        "allowedHours": "09:00-20:00"
      }
    ]
  },
  "battery": {
    "enableBatteryOptimization": true,
    "autoTowerSaving": true
  }
}
```

## Security Considerations

### Code Signing
```powershell
# Sign executable (requires valid certificate)
signtool sign /sha1 CertificateThumbprint /t http://timestamp.digicert.com MurtiWifiConnecter.exe
```

### Windows Defender Configuration
```xml
<!-- Exclusion policy for Windows Defender -->
<Policy>
  <ExclusionPath>C:\Program Files\WiFiManagerPro\</ExclusionPath>
  <ExclusionProcess>MurtiWifiConnecter.exe</ExclusionProcess>
</Policy>
```

### Firewall Configuration
```batch
# Add firewall exception
netsh advfirewall firewall add rule name="WiFi Manager Pro" dir=in action=allow program="C:\Program Files\WiFiManagerPro\bin\MurtiWifiConnecter.exe"
```

## Monitoring and Maintenance

### Health Monitoring
```powershell
# Check service status
Get-Service WiFiManagerProService

# Check application logs
Get-EventLog -LogName Application -Source "WiFiManagerPro" -Newest 10

# Check performance counters
Get-Counter "\Process(MurtiWifiConnecter)\% Processor Time"
```

### Log File Locations
```
System Logs:    %ProgramData%\WiFiManagerPro\logs\
User Logs:      %APPDATA%\WiFiManagerPro\logs\
Crash Dumps:    %ProgramData%\WiFiManagerPro\crashes\
Configuration:  %ProgramData%\WiFiManagerPro\config\
Analytics:      %ProgramData%\WiFiManagerPro\analytics\
```

### Maintenance Tasks
```batch
@echo off
REM Daily maintenance script

REM Clean old log files (keep 30 days)
forfiles /p "%ProgramData%\WiFiManagerPro\logs" /s /m *.log /d -30 /c "cmd /c del @path"

REM Optimize database
"%ProgramFiles%\WiFiManagerPro\bin\MurtiWifiConnecter.exe" --optimize-db

REM Check for updates
"%ProgramFiles%\WiFiManagerPro\bin\MurtiWifiConnecter.exe" --check-updates

REM Generate health report
"%ProgramFiles%\WiFiManagerPro\bin\MurtiWifiConnecter.exe" --health-report
```

## Troubleshooting

### Common Issues

#### Installation Fails
```
Symptoms: Installer exits with error code 1603
Solution:
1. Run as Administrator
2. Check Windows Installer service is running
3. Verify .NET 6.0 Runtime is installed
4. Check Windows Event Log for detailed error
```

#### Service Won't Start
```
Symptoms: Windows service fails to start
Solution:
1. Check service account permissions
2. Verify executable exists and is not corrupted
3. Check application dependencies
4. Review service event logs
```

#### High Memory Usage
```
Symptoms: Application uses excessive memory
Solution:
1. Enable lightweight mode in settings
2. Reduce scan frequency
3. Disable detailed analytics
4. Check for memory leaks in logs
```

### Diagnostic Tools
```powershell
# Built-in diagnostics
MurtiWifiConnecter.exe --diagnostics

# Network diagnostics
MurtiWifiConnecter.exe --network-test

# Performance analysis
MurtiWifiConnecter.exe --performance-report

# Configuration validation
MurtiWifiConnecter.exe --validate-config
```

### Log Analysis
```powershell
# Search for errors
Select-String -Path "C:\ProgramData\WiFiManagerPro\logs\*.log" -Pattern "ERROR|FATAL"

# Network connection issues
Select-String -Path "C:\ProgramData\WiFiManagerPro\logs\*.log" -Pattern "Connection.*failed"

# Performance issues
Select-String -Path "C:\ProgramData\WiFiManagerPro\logs\*.log" -Pattern "Performance|Timeout"
```

## Uninstallation

### Standard Uninstall
1. **Control Panel**:
   - Apps & Features → WiFi Manager Pro → Uninstall

2. **PowerShell**:
   ```powershell
   Get-WmiObject -Class Win32_Product | Where-Object {$_.Name -eq "WiFi Manager Pro"} | ForEach-Object {$_.Uninstall()}
   ```

### Complete Removal
```batch
@echo off
REM Complete uninstall script

REM Stop services
sc stop WiFiManagerProService
sc delete WiFiManagerProService

REM Remove registry entries
reg delete "HKLM\SOFTWARE\MurtiWifi" /f
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WiFiManagerPro" /f
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "WiFiManagerPro" /f

REM Remove files
rmdir /s /q "%ProgramFiles%\WiFiManagerPro"
rmdir /s /q "%ProgramData%\WiFiManagerPro"
rmdir /s /q "%APPDATA%\WiFiManagerPro"

REM Remove shortcuts
del "%PUBLIC%\Desktop\WiFi Manager Pro.lnk"
del "%PROGRAMDATA%\Microsoft\Windows\Start Menu\Programs\WiFi Manager Pro\*.*"
rmdir "%PROGRAMDATA%\Microsoft\Windows\Start Menu\Programs\WiFi Manager Pro"

echo Uninstallation completed.
```

## Support and Updates

### Update Mechanism
- **Automatic Updates**: Enabled by default, checks weekly
- **Manual Updates**: Help → Check for Updates
- **Enterprise Updates**: WSUS or SCCM integration available

### Support Channels
- **Documentation**: [https://docs.wifimanagerpro.com](https://docs.wifimanagerpro.com)
- **Community Forum**: [https://community.wifimanagerpro.com](https://community.wifimanagerpro.com)
- **GitHub Issues**: [https://github.com/MurtiWifi/WiFiManagerPro/issues](https://github.com/MurtiWifi/WiFiManagerPro/issues)
- **Enterprise Support**: enterprise@wifimanagerpro.com

### Version History
- **v2.1.0**: Current release with advanced security features
- **v2.0.0**: Major release with family profiles and analytics
- **v1.x**: Legacy versions (deprecated)

---

*This deployment guide is maintained by the WiFi Manager Pro development team. For the latest version, visit our documentation website.*