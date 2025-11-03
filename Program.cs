using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                ShowBanner();

                // Validate platform support
                if (!IsPlatformSupported())
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Unsupported platform");
                    Console.WriteLine("Supported: Windows, macOS, Linux");
                    Console.ResetColor();
                    return 1;
                }

                // Get the appropriate WiFi manager
                var wifiManager = GetWifiManager();
                if (wifiManager == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Failed to initialize WiFi manager");
                    Console.ResetColor();
                    return 1;
                }

                // Process command
                if (args.Length == 0)
                {
                    return await ShowInteractiveMenu(wifiManager);
                }

                return await ProcessCommand(wifiManager, args);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"Details: {ex.InnerException.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static void ShowBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔════════════════════════════════════════╗");
            Console.WriteLine("║   MurtiWiFi Connector v3.2.0          ║");
            Console.WriteLine("║   Cross-Platform WiFi Manager         ║");
            Console.WriteLine("╚════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        private static bool IsPlatformSupported()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                   RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                   RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        }

        private static IWifiManager? GetWifiManager()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new WindowsWifiManager();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return new LinuxWifiManager();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return new MacOSWifiManager();

            return null;
        }

        private static async Task<int> ProcessCommand(IWifiManager manager, string[] args)
        {
            return args[0].ToLower() switch
            {
                "help" => ShowHelp(),
                "scan" => await ScanNetworks(manager),
                "status" => await ShowStatus(manager),
                "connect" => await Connect(manager, args),
                "disconnect" => await Disconnect(manager),
                "profiles" => await ShowProfiles(manager),
                "info" => await ShowSystemInfo(),
                "diag" => await RunDiagnostics(),
                "security" => await AnalyzeSecurity(manager),
                "optimize" => await OptimizeChannels(manager),
                _ => ShowHelp(),
            };
        }

        private static async Task<int> ShowInteractiveMenu(IWifiManager manager)
        {
            while (true)
            {
                Console.WriteLine("\nOptions:");
                Console.WriteLine("1. Scan networks");
                Console.WriteLine("2. Show status");
                Console.WriteLine("3. Connect");
                Console.WriteLine("4. Disconnect");
                Console.WriteLine("5. Exit");
                Console.Write("\nSelect option (1-5): ");

                string? input = Console.ReadLine();
                Console.WriteLine();

                int result = input switch
                {
                    "1" => await ScanNetworks(manager),
                    "2" => await ShowStatus(manager),
                    "3" => await Connect(manager, Array.Empty<string>()),
                    "4" => await Disconnect(manager),
                    "5" => 0,
                    _ => -1,
                };

                if (result >= 0) return result;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Invalid option");
                Console.ResetColor();
            }
        }

        private static async Task<int> ScanNetworks(IWifiManager manager)
        {
            try
            {
                Console.WriteLine("Scanning networks...");
                var networks = await manager.GetAvailableNetworks();

                if (networks.Count == 0)
                {
                    Console.WriteLine("No networks found");
                    return 1;
                }

                Console.WriteLine($"\nFound {networks.Count} network(s):\n");
                foreach (var network in networks)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"SSID: {network.SSID}");
                    Console.ResetColor();
                    Console.WriteLine($" | Signal: {network.SignalStrength}% | Band: {network.Band}");
                    if (!string.IsNullOrEmpty(network.SecurityType))
                        Console.WriteLine($"     Security: {network.SecurityType}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Scan failed: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static async Task<int> ShowStatus(IWifiManager manager)
        {
            try
            {
                var connected = await manager.GetConnectedNetwork();

                if (connected == null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Not connected to any network");
                    Console.ResetColor();
                    return 0;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Connected Network:");
                Console.ResetColor();
                Console.WriteLine($"SSID: {connected.SSID}");
                Console.WriteLine($"Signal Strength: {connected.SignalStrength}%");
                Console.WriteLine($"Band: {connected.Band}");
                Console.WriteLine($"Channel: {connected.Channel}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Status check failed: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static async Task<int> Connect(IWifiManager manager, string[] args)
        {
            try
            {
                string ssid;
                string password;

                if (args.Length >= 2)
                {
                    ssid = args[1];
                    password = args[2];
                }
                else
                {
                    Console.Write("Enter SSID: ");
                    ssid = Console.ReadLine() ?? string.Empty;

                    Console.Write("Enter password: ");
                    password = Console.ReadLine() ?? string.Empty;
                }

                if (string.IsNullOrEmpty(ssid))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("SSID is required");
                    Console.ResetColor();
                    return 1;
                }

                Console.WriteLine($"Connecting to {ssid}...");
                bool success = await manager.ConnectAsync(ssid, password);

                if (success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Successfully connected to {ssid}");
                    Console.ResetColor();
                    return 0;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Connection failed");
                    Console.ResetColor();
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Connection error: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static async Task<int> Disconnect(IWifiManager manager)
        {
            try
            {
                Console.WriteLine("Disconnecting...");
                bool success = await manager.DisconnectAsync();

                if (success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Disconnected successfully");
                    Console.ResetColor();
                    return 0;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Disconnection failed");
                    Console.ResetColor();
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Disconnect error: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static Task<int> ShowProfiles(IWifiManager manager)
        {
            Console.WriteLine("Saved profiles feature coming soon");
            return Task.FromResult(0);
        }

        private static Task<int> ShowSystemInfo()
        {
            Console.WriteLine("\n=== System Information ===");
            Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
            Console.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine($".NET Runtime: {RuntimeInformation.FrameworkDescription}");
            Console.WriteLine($"Processors: {Environment.ProcessorCount}");
            Console.WriteLine();
            return Task.FromResult(0);
        }

        private static async Task<int> RunDiagnostics()
        {
            try
            {
                Console.WriteLine("\nRunning network diagnostics...\n");
                var report = await NetworkDiagnostics.RunFullDiagnosticsAsync();
                Console.WriteLine(report);
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Diagnostics error: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static async Task<int> AnalyzeSecurity(IWifiManager manager)
        {
            try
            {
                Console.WriteLine("\nAnalyzing network security...\n");
                var network = await manager.GetConnectedNetwork();

                if (network == null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Not connected to any network");
                    Console.ResetColor();
                    return 0;
                }

                var analysis = WiFiSecurityAnalyzer.AnalyzeSecurity(network);
                Console.WriteLine(analysis);
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Security analysis error: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static async Task<int> OptimizeChannels(IWifiManager manager)
        {
            try
            {
                Console.WriteLine("\nAnalyzing WiFi channels for optimization...\n");
                var networks = await manager.GetAvailableNetworks();

                if (networks.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("No networks detected");
                    Console.ResetColor();
                    return 0;
                }

                var analysis = ChannelOptimizer.AnalyzeChannelQuality(networks);
                Console.WriteLine(analysis);

                var recommendations = ChannelOptimizer.GetOptimizationRecommendations(analysis);
                Console.WriteLine("Optimization Recommendations:");
                foreach (var rec in recommendations)
                {
                    Console.WriteLine($"  {rec}");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Optimization analysis error: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static int ShowHelp()
        {
            Console.WriteLine("\n=== MurtiWiFi Connector Commands ===\n");
            Console.WriteLine("Usage: MurtiWifiConnecter [command] [options]\n");
            Console.WriteLine("Commands:");
            Console.WriteLine("  help               Show this help message");
            Console.WriteLine("  scan               Scan available WiFi networks");
            Console.WriteLine("  status             Show current connection status");
            Console.WriteLine("  connect SSID [PW]  Connect to network (interactive if no args)");
            Console.WriteLine("  disconnect         Disconnect from current network");
            Console.WriteLine("  profiles           Show saved profiles (coming soon)");
            Console.WriteLine("  info               Show system information");
            Console.WriteLine("  diag               Run network diagnostics");
            Console.WriteLine("  security           Analyze security of connected network");
            Console.WriteLine("  optimize           Analyze and optimize WiFi channels");
            Console.WriteLine();
            return 0;
        }
    }
}
