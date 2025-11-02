# MurtiWifi Connector

An enterprise-grade WiFi management CLI application for Windows, macOS, and Linux. Provides comprehensive network diagnostics, connection management, performance optimization, and security features.

## Features

- **Multi-Platform Support**: Windows, macOS, and Linux
- **Connection Management**: List, connect, disconnect, and manage WiFi networks
- **Performance Monitoring**: Real-time speed testing and bandwidth analysis
- **Network Diagnostics**: Comprehensive health checks and troubleshooting
- **Security**: Credential management and security scanning
- **Automation**: Command-line interface for scripting and automation
- **Configuration Management**: Profile-based configuration and preferences

## Requirements

- .NET 8.0 SDK or later
- Windows, macOS, or Linux operating system
- Administrator/sudo privileges for network operations

## Installation

### From Source

```bash
git clone https://github.com/MurtiSoft/MurtiWifiConnecter.git
cd MurtiWifiConnecter
dotnet build -c Release
dotnet publish -c Release -r win-x64  # or linux-x64, osx-x64
```

### From Binary

Download the latest release from [GitHub Releases](https://github.com/MurtiSoft/MurtiWifiConnecter/releases).

## Usage

### Basic Commands

```bash
# Show help
./MurtiWifiConnecter help

# Get current connection status
./MurtiWifiConnecter status

# Scan available networks
./MurtiWifiConnecter scan

# Connect to a network
./MurtiWifiConnecter connect "NetworkName" --password "your_password"

# List all profiles
./MurtiWifiConnecter profiles

# Run network diagnostics
./MurtiWifiConnecter diag

# Check network health
./MurtiWifiConnecter health

# Speed test
./MurtiWifiConnecter speed
```

### Advanced Commands

```bash
# Quick connect to saved network
./MurtiWifiConnecter quick

# Full system backup
./MurtiWifiConnecter fullbackup backup.json

# Restore from backup
./MurtiWifiConnecter restore backup.json

# Configure preferences
./MurtiWifiConnecter config

# View network analytics
./MurtiWifiConnecter analytics

# Check system information
./MurtiWifiConnecter info
```

## Project Structure

```
MurtiWifiConnecter/
├── Core/                      # Core functionality
│   ├── Windows/              # Windows-specific implementations
│   ├── Linux/                # Linux-specific implementations
│   ├── macOS/                # macOS-specific implementations
│   ├── Security/             # Security and authentication
│   ├── Billing/              # Subscription and licensing
│   ├── Configuration/        # Config management
│   ├── Network/              # Network operations
│   └── Handlers/             # Command handlers
├── Core.Tests/               # Unit tests
├── MinimalApi/               # REST API (optional)
├── Localization/             # i18n resources
├── docs/                     # Documentation
└── Resources/                # Static resources
```

## Architecture

### Core Components

- **ConnectionManager**: Manages WiFi network connections
- **ProfileManager**: Handles network profiles and preferences
- **CommandProcessor**: CLI command processing and dispatch
- **WifiManagerFactory**: Platform-specific WiFi management
- **SecurityManager**: Encryption and credential management
- **PerformanceMonitor**: System and network performance tracking

### Platform Abstraction

- **IWifiManager**: Platform-agnostic WiFi interface
- **WindowsWifiManager**: Windows implementation (netsh-based)
- **LinuxWifiManager**: Linux implementation (wpa_supplicant)
- **macOSWifiManager**: macOS implementation (airport)

## Building

### Release Build

```bash
dotnet build -c Release
```

### Platform-Specific Publish

```bash
# Windows x64
dotnet publish -c Release -r win-x64

# Linux x64
dotnet publish -c Release -r linux-x64

# macOS x64
dotnet publish -c Release -r osx-x64

# macOS ARM64 (Apple Silicon)
dotnet publish -c Release -r osx-arm64
```

## Testing

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test Core.Tests/

# Run with verbose output
dotnet test -v normal
```

## Configuration

Configuration is stored in `appsettings.json`:

```json
{
  "ConnectionSettings": {
    "AutoConnect": false,
    "Timeout": 30000
  },
  "SecuritySettings": {
    "EnableTLS": true,
    "StoreCredentials": false
  }
}
```

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE.txt](LICENSE.txt) file for details.

## Support

- **Issues**: [GitHub Issues](https://github.com/MurtiSoft/MurtiWifiConnecter/issues)
- **Discussions**: [GitHub Discussions](https://github.com/MurtiSoft/MurtiWifiConnecter/discussions)
- **Documentation**: See the `docs/` directory

## Troubleshooting

### Common Issues

**"Permission denied" errors**
- Run with elevated privileges (sudo on Linux/macOS, Administrator on Windows)

**"Network not found"**
- Ensure WiFi adapter is enabled: `./MurtiWifiConnecter scan`
- Check network is in range with: `./MurtiWifiConnecter status`

**Connection timeout**
- Verify credentials with: `./MurtiWifiConnecter profiles`
- Check signal strength with: `./MurtiWifiConnecter speed`

## Performance

- Minimal memory footprint (~50MB typical)
- Fast connection switching (<5 seconds)
- Efficient network scanning (<3 seconds)

## Security Considerations

- Credentials are stored securely using platform-specific key storage
- All network communications support TLS encryption
- Regular security audits and updates recommended

## Changelog

See [releases](https://github.com/MurtiSoft/MurtiWifiConnecter/releases) for detailed changelog.

## Related Projects

- [netsh](https://docs.microsoft.com/en-us/windows-server/administration/windows-commands/netsh) - Windows network shell
- [wpa_supplicant](https://w1.fi/wpa_supplicant/) - Linux WiFi authentication
- [airport](https://en.wikipedia.org/wiki/Airport_\(software\)) - macOS WiFi management

## Author

**MurtiSoft** - Enterprise Software Solutions

---

**Note**: This project is actively maintained. Please ensure you're using the latest version from the master branch.
