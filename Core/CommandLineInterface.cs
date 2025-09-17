using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Command-line interface for WiFi management
    /// </summary>
    public class CommandLineInterface
    {
        private readonly SimplifiedWifiManager _wifiManager;
        private readonly ProfileManager _profileManager;
        private readonly SpeedTest _speedTest;
        private bool _isRunning;

        public CommandLineInterface()
        {
            _wifiManager = new SimplifiedWifiManager();
            _profileManager = new ProfileManager();
            _speedTest = new SpeedTest();
        }

        public async Task RunAsync(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                await RunInteractiveMode();
                return;
            }

            await ExecuteCommand(args);
        }

        private async Task RunInteractiveMode()
        {
            _isRunning = true;
            Console.Clear();
            Console.WriteLine("MurtiWiFi Connector - Interactive CLI");
            Console.WriteLine("=====================================");
            Console.WriteLine();

            while (_isRunning)
            {
                Console.WriteLine("\nAvailable commands:");
                Console.WriteLine("  1. Scan - Scan for available networks");
                Console.WriteLine("  2. Connect - Connect to a network");
                Console.WriteLine("  3. Disconnect - Disconnect from current network");
                Console.WriteLine("  4. Status - Show current connection status");
                Console.WriteLine("  5. Profiles - Manage saved profiles");
                Console.WriteLine("  6. Speed - Test connection speed");
                Console.WriteLine("  7. Diagnostics - Run network diagnostics");
                Console.WriteLine("  8. Exit - Exit the application");
                Console.WriteLine();
                Console.Write("Enter command (1-8): ");

                var input = Console.ReadLine()?.Trim();

                try
                {
                    switch (input)
                    {
                        case "1":
                        case "scan":
                            await ScanNetworks();
                            break;
                        case "2":
                        case "connect":
                            await ConnectToNetwork();
                            break;
                        case "3":
                        case "disconnect":
                            await DisconnectFromNetwork();
                            break;
                        case "4":
                        case "status":
                            await ShowStatus();
                            break;
                        case "5":
                        case "profiles":
                            await ManageProfiles();
                            break;
                        case "6":
                        case "speed":
                            await TestSpeed();
                            break;
                        case "7":
                        case "diagnostics":
                            await RunDiagnostics();
                            break;
                        case "8":
                        case "exit":
                        case "quit":
                            _isRunning = false;
                            Console.WriteLine("Exiting...");
                            break;
                        default:
                            Console.WriteLine("Invalid command. Please try again.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        private async Task ExecuteCommand(string[] args)
        {
            var command = args[0].ToLower();

            switch (command)
            {
                case "--help":
                case "-h":
                    ShowHelp();
                    break;
                case "--scan":
                case "-s":
                    await ScanNetworks();
                    break;
                case "--connect":
                case "-c":
                    if (args.Length >= 3)
                    {
                        await ConnectToNetwork(args[1], args[2]);
                    }
                    else
                    {
                        Console.WriteLine("Usage: --connect <SSID> <Password>");
                    }
                    break;
                case "--disconnect":
                case "-d":
                    await DisconnectFromNetwork();
                    break;
                case "--status":
                    await ShowStatus();
                    break;
                case "--profiles":
                case "-p":
                    await ListProfiles();
                    break;
                case "--speed":
                    await TestSpeed();
                    break;
                case "--diagnostics":
                    await RunDiagnostics();
                    break;
                case "--auto-connect":
                    if (args.Length >= 3)
                    {
                        EnableAutoConnect(args[1], args[2]);
                    }
                    else
                    {
                        Console.WriteLine("Usage: --auto-connect <SSID> <Password>");
                    }
                    break;
                default:
                    Console.WriteLine($"Unknown command: {command}");
                    ShowHelp();
                    break;
            }
        }

        private void ShowHelp()
        {
            Console.WriteLine("MurtiWiFi Connector - Command Line Interface");
            Console.WriteLine("===========================================");
            Console.WriteLine();
            Console.WriteLine("Usage: MurtiWifiConnecter.exe [command] [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  --help, -h                     Show this help message");
            Console.WriteLine("  --scan, -s                     Scan for available networks");
            Console.WriteLine("  --connect, -c <SSID> <Pass>    Connect to a network");
            Console.WriteLine("  --disconnect, -d               Disconnect from current network");
            Console.WriteLine("  --status                       Show connection status");
            Console.WriteLine("  --profiles, -p                 List saved profiles");
            Console.WriteLine("  --speed                        Test connection speed");
            Console.WriteLine("  --diagnostics                  Run network diagnostics");
            Console.WriteLine("  --auto-connect <SSID> <Pass>   Enable auto-reconnect");
            Console.WriteLine();
            Console.WriteLine("Interactive mode:");
            Console.WriteLine("  Run without arguments to enter interactive mode");
        }

        private async Task ScanNetworks()
        {
            Console.WriteLine("Scanning for networks...");

            var networks = await _wifiManager.ScanNetworksAsync();

            if (networks.Count == 0)
            {
                Console.WriteLine("No networks found.");
                return;
            }

            Console.WriteLine($"\nFound {networks.Count} networks:");
            Console.WriteLine("=====================================");
            Console.WriteLine("SSID                          Signal  Security");
            Console.WriteLine("-------------------------------------");

            foreach (var network in networks.OrderByDescending(n => n.SignalStrength))
            {
                var ssid = network.SSID.Length > 28
                    ? network.SSID.Substring(0, 25) + "..."
                    : network.SSID.PadRight(28);

                var signalBar = GetSignalBar(network.SignalStrength);
                var security = network.IsSecured ? network.Authentication : "Open";

                Console.WriteLine($"{ssid}  {signalBar}  {security}");
            }
        }

        private async Task ConnectToNetwork()
        {
            Console.Write("Enter SSID: ");
            var ssid = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(ssid))
            {
                Console.WriteLine("SSID cannot be empty.");
                return;
            }

            // Check if profile exists
            var password = _profileManager.GetPassword(ssid);

            if (string.IsNullOrEmpty(password))
            {
                Console.Write("Enter Password: ");
                password = ReadPassword();

                if (string.IsNullOrEmpty(password))
                {
                    Console.WriteLine("Password cannot be empty.");
                    return;
                }

                Console.Write("Save profile? (y/n): ");
                if (Console.ReadLine()?.ToLower() == "y")
                {
                    await _profileManager.SaveProfileAsync(ssid, password, false);
                    Console.WriteLine("Profile saved.");
                }
            }
            else
            {
                Console.WriteLine("Using saved profile...");
            }

            await ConnectToNetwork(ssid, password);
        }

        private async Task ConnectToNetwork(string ssid, string password)
        {
            Console.WriteLine($"Connecting to {ssid}...");

            var success = await _wifiManager.ConnectAsync(ssid, password);

            if (success)
            {
                Console.WriteLine($"Successfully connected to {ssid}");

                // Save profile if successful
                await _profileManager.SaveProfileAsync(ssid, password, false);
            }
            else
            {
                Console.WriteLine($"Failed to connect to {ssid}");
            }
        }

        private async Task DisconnectFromNetwork()
        {
            Console.WriteLine("Disconnecting...");

            var success = await _wifiManager.DisconnectAsync();

            if (success)
            {
                Console.WriteLine("Disconnected successfully");
            }
            else
            {
                Console.WriteLine("Failed to disconnect");
            }
        }

        private async Task ShowStatus()
        {
            var currentSSID = await _wifiManager.GetCurrentSSIDAsync();

            if (string.IsNullOrEmpty(currentSSID))
            {
                Console.WriteLine("Status: Not connected");
            }
            else
            {
                Console.WriteLine($"Status: Connected");
                Console.WriteLine($"Network: {currentSSID}");

                // Get signal strength
                var networks = await _wifiManager.ScanNetworksAsync();
                var currentNetwork = networks.FirstOrDefault(n => n.SSID == currentSSID);

                if (currentNetwork != null)
                {
                    Console.WriteLine($"Signal: {currentNetwork.SignalStrength}% {GetSignalBar(currentNetwork.SignalStrength)}");
                    Console.WriteLine($"Security: {currentNetwork.Authentication}");
                }
            }
        }

        private async Task ManageProfiles()
        {
            Console.WriteLine("\nProfile Management:");
            Console.WriteLine("1. List profiles");
            Console.WriteLine("2. Delete profile");
            Console.WriteLine("3. Export profiles");
            Console.WriteLine("4. Import profiles");
            Console.WriteLine("5. Back");
            Console.Write("Choice: ");

            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":
                    await ListProfiles();
                    break;
                case "2":
                    await DeleteProfile();
                    break;
                case "3":
                    await ExportProfiles();
                    break;
                case "4":
                    await ImportProfiles();
                    break;
            }
        }

        private async Task ListProfiles()
        {
            var profiles = _profileManager.GetAllProfiles();

            if (profiles.Count == 0)
            {
                Console.WriteLine("No saved profiles.");
                return;
            }

            Console.WriteLine("\nSaved Profiles:");
            Console.WriteLine("=====================================");
            Console.WriteLine("SSID                     Auto  Used");
            Console.WriteLine("-------------------------------------");

            foreach (var profile in profiles.OrderBy(p => p.SSID))
            {
                var ssid = profile.SSID.Length > 23
                    ? profile.SSID.Substring(0, 20) + "..."
                    : profile.SSID.PadRight(23);
                var auto = profile.AutoConnect ? "Yes" : "No ";
                var used = profile.ConnectionCount.ToString().PadLeft(4);

                Console.WriteLine($"{ssid}  {auto}  {used}");
            }
        }

        private async Task DeleteProfile()
        {
            Console.Write("Enter SSID to delete: ");
            var ssid = Console.ReadLine()?.Trim();

            if (!string.IsNullOrEmpty(ssid))
            {
                if (await _profileManager.RemoveProfileAsync(ssid))
                {
                    Console.WriteLine($"Profile '{ssid}' deleted.");
                }
                else
                {
                    Console.WriteLine($"Profile '{ssid}' not found.");
                }
            }
        }

        private async Task ExportProfiles()
        {
            Console.Write("Enter export file path: ");
            var path = Console.ReadLine()?.Trim();

            if (!string.IsNullOrEmpty(path))
            {
                if (await _profileManager.ExportProfilesAsync(path))
                {
                    Console.WriteLine($"Profiles exported to {path}");
                }
                else
                {
                    Console.WriteLine("Export failed.");
                }
            }
        }

        private async Task ImportProfiles()
        {
            Console.Write("Enter import file path: ");
            var path = Console.ReadLine()?.Trim();

            if (!string.IsNullOrEmpty(path))
            {
                var count = await _profileManager.ImportProfilesAsync(path);
                Console.WriteLine($"Imported {count} profiles.");
            }
        }

        private async Task TestSpeed()
        {
            Console.WriteLine("Testing connection speed...");

            // Test latency
            var latencyResult = await _speedTest.TestLatencyAsync();
            if (latencyResult.Success)
            {
                Console.WriteLine($"Latency: {latencyResult.RoundtripTime:F0} ms");
            }

            // Test download speed
            var speedResult = await _speedTest.TestDownloadSpeedAsync();
            if (speedResult.Success)
            {
                Console.WriteLine($"Download: {speedResult.DownloadSpeed:F2} Mbps");
            }
            else
            {
                Console.WriteLine($"Speed test failed: {speedResult.Message}");
            }
        }

        private async Task RunDiagnostics()
        {
            Console.WriteLine("Running network diagnostics...");
            Console.WriteLine();

            // Check current connection
            var currentSSID = await _wifiManager.GetCurrentSSIDAsync();
            Console.WriteLine($"[{(currentSSID != null ? "OK" : "FAIL")}] WiFi Connection: {currentSSID ?? "Not connected"}");

            // Check adapter status
            var adapterRecovery = new AdapterRecovery();
            var adapterWorking = await adapterRecovery.IsAdapterWorkingAsync();
            Console.WriteLine($"[{(adapterWorking ? "OK" : "FAIL")}] WiFi Adapter: {(adapterWorking ? "Working" : "Not working")}");

            // Check DNS resolution
            try
            {
                var addresses = await System.Net.Dns.GetHostAddressesAsync("google.com");
                Console.WriteLine($"[OK] DNS Resolution: {addresses.Length} addresses resolved");
            }
            catch
            {
                Console.WriteLine("[FAIL] DNS Resolution: Cannot resolve domain names");
            }

            // Check internet connectivity
            var pingResult = await _speedTest.TestLatencyAsync("8.8.8.8");
            Console.WriteLine($"[{(pingResult.Success ? "OK" : "FAIL")}] Internet Connection: {(pingResult.Success ? $"Latency {pingResult.RoundtripTime}ms" : "No connection")}");

            Console.WriteLine("\nDiagnostics complete.");
        }

        private void EnableAutoConnect(string ssid, string password)
        {
            _wifiManager.EnableAutoReconnect(ssid, password, 30);
            Console.WriteLine($"Auto-reconnect enabled for {ssid}");
        }

        private string GetSignalBar(int strength)
        {
            if (strength >= 80) return "[****]";
            if (strength >= 60) return "[*** ]";
            if (strength >= 40) return "[**  ]";
            if (strength >= 20) return "[*   ]";
            return "[    ]";
        }

        private string ReadPassword()
        {
            var password = "";
            ConsoleKeyInfo key;

            do
            {
                key = Console.ReadKey(true);

                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    password += key.KeyChar;
                    Console.Write("*");
                }
                else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password = password.Substring(0, password.Length - 1);
                    Console.Write("\b \b");
                }
            }
            while (key.Key != ConsoleKey.Enter);

            Console.WriteLine();
            return password;
        }

        public void Dispose()
        {
            _wifiManager?.Dispose();
        }
    }
}