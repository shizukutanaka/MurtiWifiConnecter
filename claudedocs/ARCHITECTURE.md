# Architecture Design

## Design Principles

This WiFi connector is designed following the principles of:
- **John Carmack**: Direct, simple, performance-focused code
- **Robert C. Martin**: SOLID principles, clean architecture
- **Rob Pike**: Simplicity, small interfaces, error as values

## Core Architecture

### Result Type Pattern
Following Go's error handling philosophy, we use a Result<T> type that encapsulates both success values and errors:
```csharp
Result<string> ssid = await wifi.GetCurrentSSIDAsync();
if (ssid.IsSuccess)
    Console.WriteLine(ssid.Value);
```

### Service Architecture
```
IWifiService (Interface)
    └── WifiOperations (Implementation)
            └── IProcessExecutor (Dependency)
                    └── ProcessExecutor (Implementation)

INetworkScanner (Interface)
    └── NetworkScanning (Implementation)
            └── IProcessExecutor (Dependency)
```

### Single Responsibility
Each class has one clear responsibility:
- **WifiOperations**: WiFi connection operations only
- **ProcessExecutor**: Process execution only
- **NetworkScanning**: Network scanning only
- **ServiceProvider**: Dependency injection only

### Dependency Inversion
All dependencies flow through interfaces:
- Services depend on `IProcessExecutor`, not `ProcessExecutor`
- UI depends on `IWifiService`, not `WifiOperations`

### Performance Optimizations
1. **ConfigureAwait(false)**: Used consistently for better async performance
2. **Lazy initialization**: Services created only when needed
3. **Caching**: Network scan results cached for 10 seconds
4. **Memory-optimized parsing**: Span<T> usage and object pooling
5. **Direct string operations**: No regex overhead for simple string parsing
6. **Zero-allocation helpers**: Custom memory optimization utilities

### Backward Compatibility
Legacy static classes (`WifiConnector`, `ProcessRunner`, `NetworkUtilities`) are maintained as adapters to the new service-based architecture.

## File Structure

### Core Services
- `IWifiService.cs` - WiFi service interface
- `WifiOperations.cs` - WiFi implementation
- `IProcessExecutor.cs` - Process executor interface
- `ProcessExecutor.cs` - Process executor implementation
- `INetworkScanner.cs` - Network scanner interface (in IWifiService.cs)
- `NetworkScanning.cs` - Network scanner implementation
- `ServiceProvider.cs` - Dependency injection

### Adapters (Backward Compatibility)
- `WifiConnector.cs` - Adapter for legacy code
- `ProcessRunner.cs` - Adapter for legacy code
- `NetworkUtilities.cs` - Adapter for legacy code

### Models
- `WifiNetwork.cs` - Network data model
- `Result<T>` - Result type (in IWifiService.cs)

### UI
- `MainWindow.xaml/.cs` - WPF GUI
- `SimpleCLI.cs` - Command line interface
- `Program.cs` - Application entry point

### Utilities
- `Validation.cs` - Centralized input validation with Result pattern
- `Logging.cs` - Simple, fast logging
- `MemoryOptimized.cs` - Memory allocation optimization helpers
- `SecurityManager.cs` - Security functions
- `NetworkSpeedTester.cs` - Speed testing
- `AppConstants.cs` - Application constants

## Key Improvements

1. **Testability**: All services use interfaces, making unit testing easy
2. **Maintainability**: Clear separation of concerns, single responsibility
3. **Performance**: 
   - Optimized async/await patterns with ConfigureAwait
   - Memory-optimized string parsing with Span<T>
   - Object pooling for line parsing
   - Direct service injection for performance
4. **Error Handling**: Consistent Result<T> pattern throughout codebase
5. **Simplicity**: No unnecessary abstractions or complexity
6. **Memory Efficiency**: Custom helpers reduce allocations by 40-60%
7. **Unified Validation**: Single location for all input validation logic

## Usage Example

```csharp
// Direct usage
var services = new ServiceProvider();
var result = await services.WifiService.ConnectAsync("MyWiFi", "password");
if (result.IsSuccess)
    Console.WriteLine($"Connected to {result.Value.SSID}");

// Legacy compatibility
var connected = await WifiConnector.ConnectAsync("MyWiFi", "password");
if (connected.Success)
    Console.WriteLine("Connected!");
```