# MurtiWifi Connector - User Guide
## Enterprise-Grade WiFi Management Made Easy

**Version**: 3.2.0
**Last Updated**: 2025-10-31

---

## Table of Contents

1. [Introduction](#introduction)
2. [Installation](#installation)
3. [Getting Started](#getting-started)
4. [Basic Operations](#basic-operations)
5. [Advanced Features](#advanced-features)
6. [Troubleshooting](#troubleshooting)
7. [FAQ](#faq)
8. [Support](#support)

---

## Introduction

### What is MurtiWifi Connector?

MurtiWifi Connector is an enterprise-grade WiFi management tool that makes managing wireless networks simple and powerful. Whether you're at home, in the office, or managing a large enterprise network, MurtiWifi Connector provides:

✅ **Automatic Network Connection** - Connect to the best available network automatically
✅ **WiFi 6/7 Support** - Take advantage of the latest WiFi technology
✅ **Enterprise Security** - WPA3 encryption and advanced security features
✅ **Smart Optimization** - AI-driven network performance optimization
✅ **Fast Roaming** - Seamless handoff between access points
✅ **Cross-Platform** - Works on Windows, macOS, and Linux

### Who Should Use This?

- **Home Users**: Get the best WiFi performance with minimal effort
- **IT Professionals**: Manage WiFi connections efficiently across devices
- **Enterprise Admins**: Deploy and manage WiFi at scale
- **Network Engineers**: Optimize and troubleshoot WiFi networks

### System Requirements

| Component | Requirement |
|-----------|------------|
| **Operating System** | Windows 10/11, macOS 10.15+, Linux (NetworkManager) |
| **RAM** | 512 MB minimum, 1 GB recommended |
| **Disk Space** | 50 MB for installation |
| **Network** | WiFi adapter (WiFi 6/7 recommended) |
| **Permissions** | Administrator/root access required |

---

## Installation

### Windows

#### Option 1: MSI Installer (Recommended)

1. **Download** the installer:
   ```
   MurtiWifiConnecter-3.2.0.0-x64.msi
   ```

2. **Run** the installer:
   - Double-click the MSI file
   - Follow the installation wizard
   - Choose installation location (default: `C:\Program Files\MurtiSoft\MurtiWifi Connector`)
   - Select features to install
   - Click **Install**

3. **Grant** administrator privileges when prompted

4. **Verify** installation:
   - Open Command Prompt or PowerShell
   - Type: `MurtiWifiConnecter help`
   - You should see the help menu

#### Option 2: Chocolatey

```powershell
choco install murtiwifi-connector
```

#### Option 3: Manual Installation

1. Download the ZIP file
2. Extract to `C:\Program Files\MurtiSoft\MurtiWifi Connector`
3. Add to PATH manually:
   ```powershell
   [Environment]::SetEnvironmentVariable("Path", $env:Path + ";C:\Program Files\MurtiSoft\MurtiWifi Connector", "Machine")
   ```

### macOS

#### Option 1: PKG Installer (Recommended)

1. **Download**: `MurtiWifiConnecter-3.2.0-macos.pkg`
2. **Run**: Double-click and follow installation wizard
3. **Verify**:
   ```bash
   murtiwifi-connector help
   ```

#### Option 2: Homebrew

```bash
brew install murtisoft/tap/murtiwifi-connector
```

### Linux

#### Debian/Ubuntu (DEB)

```bash
# Download
wget https://github.com/murtisoft/murtiwifi-connector/releases/download/v3.2.0/murtiwifi-connector_3.2.0_amd64.deb

# Install
sudo dpkg -i murtiwifi-connector_3.2.0_amd64.deb

# Fix dependencies
sudo apt-get install -f

# Verify
murtiwifi-connector help
```

#### Red Hat/CentOS (RPM)

```bash
# Download
wget https://github.com/murtisoft/murtiwifi-connector/releases/download/v3.2.0/murtiwifi-connector-3.2.0-1.x86_64.rpm

# Install
sudo rpm -ivh murtiwifi-connector-3.2.0-1.x86_64.rpm

# Verify
murtiwifi-connector help
```

---

## Getting Started

### First Launch

1. **Open** your terminal/command prompt

2. **Run as Administrator** (Windows):
   ```powershell
   # Right-click Command Prompt → "Run as administrator"
   MurtiWifiConnecter
   ```

3. **On first launch**, you'll see:
   - Welcome banner with version info
   - Security notice
   - Feature list
   - Command help

### Quick Start (5 minutes)

#### 1. Check Your WiFi Status

```bash
MurtiWifiConnecter status
```

**Output:**
```
✓ Connected to: MyHomeWiFi
  Signal: -45 dBm (Excellent)
  Speed: 866 Mbps
  WiFi Standard: 802.11ax (WiFi 6)
  Security: WPA3-Personal
```

#### 2. Scan for Available Networks

```bash
MurtiWifiConnecter scan
```

**Output:**
```
Available Networks:
1. MyHomeWiFi         [-45 dBm] ★★★★★ WPA3
2. Office_5GHz        [-52 dBm] ★★★★☆ WPA2
3. CoffeeShop_Guest   [-68 dBm] ★★☆☆☆ Open
```

#### 3. Connect to a Network

```bash
# Connect to saved network
MurtiWifiConnecter connect MyHomeWiFi

# Connect to new network
MurtiWifiConnecter connect "Office_5GHz" --password "YourPassword"
```

#### 4. Optimize Your Connection (WiFi 6/7)

```bash
MurtiWifiConnecter optimize-wifi6 MyHomeWiFi --profile balanced
```

**That's it!** You're now connected and optimized.

---

## Basic Operations

### Managing Connections

#### Connect to Network

```bash
# Saved network
MurtiWifiConnecter connect NetworkName

# New network with password
MurtiWifiConnecter connect "NetworkName" --password "password123"

# With security type
MurtiWifiConnecter connect "NetworkName" --password "password123" --security WPA2
```

#### Disconnect

```bash
MurtiWifiConnecter disconnect
```

#### View Current Status

```bash
MurtiWifiConnecter status

# Detailed status
MurtiWifiConnecter status --detailed

# Quick status
MurtiWifiConnecter q
```

### Scanning Networks

#### Basic Scan

```bash
MurtiWifiConnecter scan
```

#### Filter by Signal Strength

```bash
# Only show strong signals (>= -60 dBm)
MurtiWifiConnecter scan --min-signal -60

# Show excellent signals only
MurtiWifiConnecter scan --quality excellent
```

#### Filter by Security

```bash
# Show only WPA3 networks
MurtiWifiConnecter scan --security WPA3

# Show unsecured networks
MurtiWifiConnecter scan --security Open
```

#### Show Hidden Networks

```bash
MurtiWifiConnecter scan --show-hidden
```

### Managing Profiles

#### List Saved Profiles

```bash
MurtiWifiConnecter profiles
```

#### Add Profile

```bash
MurtiWifiConnecter add-profile "MyNetwork" --password "password123"
```

#### Remove Profile

```bash
MurtiWifiConnecter remove-profile "OldNetwork"
```

#### Export Profiles (Backup)

```bash
MurtiWifiConnecter export my-wifi-backup.json
```

#### Import Profiles (Restore)

```bash
MurtiWifiConnecter import my-wifi-backup.json
```

### Priority Management

#### Set Priority

```bash
# Higher number = higher priority
MurtiWifiConnecter prefer "HomeNetwork" 10
MurtiWifiConnecter prefer "OfficeNetwork" 5
```

#### Auto-Connect to Best Network

```bash
MurtiWifiConnecter quick
```

This will:
1. Scan available networks
2. Check saved profiles
3. Connect to highest priority network with best signal

---

## Advanced Features

### WiFi 6/6E Optimization

#### Check WiFi 6 Support

```bash
MurtiWifiConnecter detect-wifi6
```

**Output:**
```
WiFi 6/6E Capability Report:
✓ Adapter supports 802.11ax (WiFi 6)
✓ OFDMA supported
✓ MU-MIMO 8x8 supported
✓ BSS Coloring supported
✓ 6GHz band supported (WiFi 6E)
✓ Target Wake Time (TWT) supported
```

#### Optimization Profiles

##### Maximum Throughput (Best for streaming, downloads)
```bash
MurtiWifiConnecter optimize-wifi6 "NetworkName" --profile max-throughput
```
- Channel width: 160MHz
- OFDMA: Enabled
- MU-MIMO: Enabled
- Roaming: Standard

##### Balanced (Recommended for most users)
```bash
MurtiWifiConnecter optimize-wifi6 "NetworkName" --profile balanced
```
- Channel width: 80MHz
- OFDMA: Enabled
- MU-MIMO: Enabled
- Roaming: Moderate

##### Fast Roaming (Best for mobile users)
```bash
MurtiWifiConnecter optimize-wifi6 "NetworkName" --profile fast-roaming
```
- Channel width: 40MHz
- Roaming: Aggressive (-75dBm threshold)
- 802.11r/k/v enabled

##### Power Saving (Best for battery life)
```bash
MurtiWifiConnecter optimize-wifi6 "NetworkName" --profile power-saving
```
- Target Wake Time: Enabled
- Channel width: 40MHz
- Reduced scanning

### WiFi 7 (802.11be) - Multi-Link Operation

If your adapter supports WiFi 7:

```bash
# Enable Multi-Link Operation (simultaneous 2.4/5/6 GHz)
MurtiWifiConnecter enable-mlo "NetworkName"

# Check MLO status
MurtiWifiConnecter mlo-status
```

**Benefits:**
- 47% throughput increase
- Lower latency
- Better reliability

### Fast Roaming (802.11r/k/v/u)

For seamless handoff between access points:

#### Enable All Roaming Features

```bash
MurtiWifiConnecter enable-fast-roaming "EnterpriseNetwork"
```

This enables:
- **802.11r**: Fast BSS Transition (no re-authentication)
- **802.11k**: Radio Resource Management (neighbor reports)
- **802.11v**: BSS Transition Management (network-guided roaming)
- **802.11u**: Hotspot 2.0 / Passpoint

#### Enable Individual Features

```bash
# Fast BSS Transition only
MurtiWifiConnecter enable-80211r "NetworkName"

# Radio measurement
MurtiWifiConnecter enable-80211k "NetworkName"

# BSS transition management
MurtiWifiConnecter enable-80211v "NetworkName"

# Hotspot 2.0
MurtiWifiConnecter enable-80211u "NetworkName"
```

#### Configure Roaming Thresholds

```bash
# Roam when signal drops to -75 dBm
MurtiWifiConnecter set-roaming-threshold "NetworkName" --rssi -75

# Scan for better AP at -70 dBm
MurtiWifiConnecter set-roaming-threshold "NetworkName" --rssi -75 --scan -70
```

#### View Roaming Statistics

```bash
MurtiWifiConnecter roaming-stats "NetworkName"
```

### WPA3 Security Enhancement

#### Enable WPA3-Personal

```bash
# Pure WPA3 (most secure, requires WPA3 support on AP)
MurtiWifiConnecter enable-wpa3 "NetworkName" --mode personal --pure

# Transition mode (WPA2/WPA3 compatibility)
MurtiWifiConnecter enable-wpa3 "NetworkName" --mode personal --transition
```

#### Enable WPA3-Enterprise

```bash
# 192-bit security with EAP-TLS
MurtiWifiConnecter enable-wpa3 "EnterpriseNetwork" --mode enterprise --eap TLS

# With certificate
MurtiWifiConnecter enable-wpa3 "EnterpriseNetwork" \
    --mode enterprise \
    --eap TLS \
    --cert "C:\Certs\client.pfx" \
    --cert-password "password"
```

#### Enable Enhanced Open (OWE)

For public networks with encryption:

```bash
MurtiWifiConnecter enable-owe "CoffeeShopWiFi"
```

#### Enable Protected Management Frames (PMF)

```bash
# Required (WPA3)
MurtiWifiConnecter enable-pmf "NetworkName" --mode required

# Optional (WPA2)
MurtiWifiConnecter enable-pmf "NetworkName" --mode optional
```

### AI-Driven Network Optimization

#### Analyze Network Performance

```bash
MurtiWifiConnecter analyze
```

**Output:**
```
AI Network Analysis:
  Throughput Prediction: 650 Mbps (Confidence: 87%)
  Latency Prediction: 12 ms (Confidence: 92%)
  Recommended Channel: 36 (5GHz, Confidence: 95%)
  Congestion Forecast: Low (next 2 hours)
  Optimization Suggestions:
    • Switch to channel 36 for 15% throughput boost
    • Enable OFDMA for better multi-device performance
    • Current interference: Moderate on channel 48
```

#### Auto-Optimize

```bash
# Apply AI recommendations automatically
MurtiWifiConnecter auto-optimize
```

### Mesh Network Optimization

For mesh WiFi systems:

```bash
# Detect mesh topology
MurtiWifiConnecter mesh-detect

# Optimize mesh routing
MurtiWifiConnecter mesh-optimize --strategy throughput

# View mesh status
MurtiWifiConnecter mesh-status
```

### Automation & Scheduling

#### Auto-Connect Script

Create a script to automatically connect to the best network:

**Windows (PowerShell):**
```powershell
# auto-connect.ps1
MurtiWifiConnecter quick
```

**Schedule in Task Scheduler:**
- Trigger: On user login
- Action: `powershell.exe -File C:\Scripts\auto-connect.ps1`

**Linux (Bash):**
```bash
#!/bin/bash
# auto-connect.sh
murtiwifi-connector quick
```

**Schedule in cron:**
```bash
# Run every 5 minutes
*/5 * * * * /usr/local/bin/auto-connect.sh
```

---

## Troubleshooting

### Common Issues

#### ❌ "Permission Denied" Error

**Problem:** Application requires administrator privileges

**Solution:**
```powershell
# Windows: Right-click → "Run as administrator"

# Linux/macOS:
sudo murtiwifi-connector [command]
```

#### ❌ Can't Find WiFi Adapter

**Problem:** No WiFi adapter detected

**Solutions:**
1. Check adapter is enabled:
   ```powershell
   # Windows
   Get-NetAdapter | Where-Object {$_.InterfaceDescription -like "*Wireless*"}
   ```

2. Update drivers:
   - Windows: Device Manager → Network adapters → Update driver
   - Linux: `sudo apt-get install linux-firmware`
   - macOS: Update macOS

3. Restart WiFi service:
   ```powershell
   # Windows
   Restart-Service WlanSvc
   ```

#### ❌ Can't Connect to Network

**Problem:** Connection fails repeatedly

**Solutions:**

1. **Check password:**
   ```bash
   MurtiWifiConnecter connect "Network" --password "correct-password"
   ```

2. **Forget and reconnect:**
   ```bash
   MurtiWifiConnecter remove-profile "Network"
   MurtiWifiConnecter connect "Network" --password "password"
   ```

3. **Check security type:**
   ```bash
   # Scan to see network security
   MurtiWifiConnecter scan

   # Connect with explicit security
   MurtiWifiConnecter connect "Network" --password "password" --security WPA2
   ```

4. **Disable Fast Roaming if problematic:**
   ```bash
   MurtiWifiConnecter disable-fast-roaming "Network"
   ```

#### ❌ Slow Connection Speed

**Problem:** WiFi is slow despite good signal

**Solutions:**

1. **Run speed test:**
   ```bash
   MurtiWifiConnecter speed-test
   ```

2. **Check channel interference:**
   ```bash
   MurtiWifiConnecter analyze
   ```

3. **Optimize for WiFi 6:**
   ```bash
   MurtiWifiConnecter optimize-wifi6 "Network" --profile max-throughput
   ```

4. **Switch to 5GHz:**
   ```bash
   # Prefer 5GHz networks
   MurtiWifiConnecter scan --band 5GHz
   ```

#### ❌ Connection Drops Frequently

**Problem:** WiFi disconnects randomly

**Solutions:**

1. **Enable fast roaming:**
   ```bash
   MurtiWifiConnecter enable-fast-roaming "Network"
   ```

2. **Adjust roaming threshold:**
   ```bash
   # Roam earlier (at stronger signal)
   MurtiWifiConnecter set-roaming-threshold "Network" --rssi -65
   ```

3. **Disable power saving:**
   ```bash
   # Windows: Network adapter properties → Power Management →
   # Uncheck "Allow computer to turn off this device"
   ```

4. **Check for interference:**
   ```bash
   MurtiWifiConnecter analyze
   ```

### Diagnostic Tools

#### Generate Diagnostic Report

```bash
MurtiWifiConnecter diag
```

This creates a comprehensive report including:
- System information
- Network adapter details
- Recent logs
- Configuration
- Error history

**Output:** `diagnostic-20251031143022.txt`

Send this file to support when reporting issues.

#### View Logs

```bash
# View recent logs
MurtiWifiConnecter logs

# View error logs only
MurtiWifiConnecter logs --level error

# Open log directory
# Windows: %LOCALAPPDATA%\MurtiWifiConnecter\Logs
# macOS: ~/Library/Logs/MurtiWifiConnecter
# Linux: ~/.local/share/murtiwifi-connector/logs
```

#### Test Network Connectivity

```bash
# Ping test
MurtiWifiConnecter test-connection

# Full speed test
MurtiWifiConnecter speed-test

# Latency test
MurtiWifiConnecter latency-test
```

---

## FAQ

### General Questions

**Q: Is MurtiWifi Connector free?**
A: Yes, there's a free version with core features. Pro and Enterprise versions offer advanced features.

**Q: Does it work on Windows 10?**
A: Yes, Windows 10 (version 1903+) and Windows 11 are fully supported.

**Q: Can I use it without administrator privileges?**
A: No, WiFi management requires administrator/root access for security reasons.

**Q: Does it collect my data?**
A: Only with your consent. Telemetry is optional and anonymized. See Privacy Policy.

### Technical Questions

**Q: My adapter doesn't support WiFi 6. Can I still use this?**
A: Yes! All features work with WiFi 4 (802.11n) and WiFi 5 (802.11ac). WiFi 6/7 features are optional.

**Q: Will this conflict with Windows WiFi management?**
A: No, MurtiWifi Connector uses Windows APIs and works alongside native tools.

**Q: Can I manage multiple computers?**
A: Yes, with Enterprise edition you can deploy centrally and manage via API.

**Q: Does it support VPN connections?**
A: WiFi connection only. Use VPN client separately after WiFi connection.

### Feature Questions

**Q: What's the difference between optimization profiles?**
A:
- **Max Throughput**: Best for streaming, downloads (160MHz channels)
- **Balanced**: Good for everything (80MHz channels)
- **Fast Roaming**: Mobile users, VoIP calls (aggressive handoff)
- **Power Saving**: Laptops, tablets (reduced power consumption)

**Q: Should I enable WPA3?**
A: Yes if your router supports it. Use transition mode for compatibility with older devices.

**Q: What is Fast Roaming (802.11r/k/v)?**
A: Technologies that make handoff between WiFi access points faster (<50ms vs 1-2 seconds).

---

## Support

### Getting Help

1. **Documentation**: Check this guide and [README.md](../README.md)
2. **GitHub Issues**: https://github.com/murtisoft/murtiwifi-connector/issues
3. **Email Support**: support@murtisoft.com
4. **Community Forum**: (coming soon)

### Reporting Bugs

When reporting bugs, please include:

1. **Diagnostic Report**:
   ```bash
   MurtiWifiConnecter diag
   ```

2. **System Information**:
   - OS version
   - WiFi adapter model
   - Router model and firmware

3. **Steps to Reproduce**:
   - What you did
   - What you expected
   - What actually happened

4. **Error Message**: Exact error text and Error ID

### Feature Requests

Submit feature requests on GitHub Issues with:
- **Use Case**: Why you need this feature
- **Proposed Solution**: How it should work
- **Alternatives**: Other solutions you've tried

### Enterprise Support

For Enterprise customers:
- **Email**: enterprise@murtisoft.com
- **SLA**: 24-hour response time
- **Phone Support**: Available with Enterprise plan
- **Dedicated Account Manager**: For 100+ seat deployments

---

## Keyboard Shortcuts

| Command | Shortcut | Description |
|---------|----------|-------------|
| `status` | `q` | Quick status |
| `scan` | `s` | Quick scan |
| `quick` | - | Auto-connect to best network |
| `help` | `h` | Show help |
| `exit` | `Ctrl+C` | Exit application |

---

## Tips & Best Practices

### For Home Users

1. ✅ **Enable WPA3 transition mode** for security + compatibility
2. ✅ **Use balanced profile** for best all-around performance
3. ✅ **Auto-connect script** for convenience
4. ✅ **Keep profiles backed up** with `export`

### For Office Workers

1. ✅ **Enable fast roaming** for seamless mobility
2. ✅ **Set priority** for office networks over public WiFi
3. ✅ **Use WPA3-Enterprise** with company credentials
4. ✅ **Regular speed tests** to detect issues

### For IT Professionals

1. ✅ **Deploy via MSI** with silent install
2. ✅ **Use configuration files** for standardization
3. ✅ **Enable telemetry** for monitoring
4. ✅ **Script automation** for common tasks
5. ✅ **Monitor logs** for troubleshooting

### For Network Engineers

1. ✅ **Use AI analysis** for optimization insights
2. ✅ **Enable all 802.11r/k/v** for enterprise WiFi
3. ✅ **Mesh optimization** for mesh deployments
4. ✅ **Monitor roaming stats** for performance tuning
5. ✅ **Document configurations** for each location

---

## Quick Reference Card

### Most Common Commands

```bash
# Status
MurtiWifiConnecter status

# Scan
MurtiWifiConnecter scan

# Connect
MurtiWifiConnecter connect "NetworkName" --password "password"

# Disconnect
MurtiWifiConnecter disconnect

# Auto-connect
MurtiWifiConnecter quick

# Optimize
MurtiWifiConnecter optimize-wifi6 "NetworkName" --profile balanced

# Help
MurtiWifiConnecter help
```

### Emergency Commands

```bash
# Reset connection
MurtiWifiConnecter disconnect && MurtiWifiConnecter quick

# Generate diagnostic report
MurtiWifiConnecter diag

# Check logs
MurtiWifiConnecter logs --level error
```

---

**Need more help?** Visit https://github.com/murtisoft/murtiwifi-connector or email support@murtisoft.com

**Version**: 3.2.0 | **Last Updated**: 2025-10-31
