using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// macOS WiFi management using airport utility and networksetup
    /// </summary>
    public class MacOSWifiManager : IWifiManager
    {
        private const string AirportPath = "/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport";

        public async Task<List<WiFiNetwork>> GetAvailableNetworks()
        {
            return await Task.Run(() =>
            {
                var networks = new List<WiFiNetwork>();
                try
                {
                    var output = RunCommand(AirportPath, "-s");
                    var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                    foreach (var line in lines.Skip(1)) // Skip header
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        // Parse airport output: SSID BSSID RSSI CHANNEL HT CC SECURITY
                        var parts = line.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 7) continue;

                        try
                        {
                            string ssid = parts[0];
                            string bssid = parts[1];
                            int rssi = int.Parse(parts[2]);
                            int channel = int.Parse(parts[3]);
                            string security = string.Join(" ", parts.Skip(6));

                            // Convert RSSI to signal strength (0-100)
                            int signalStrength = Math.Max(0, Math.Min(100, (rssi + 100)));

                            string band = channel <= 14 ? "2.4GHz" : "5GHz";

                            var network = new WiFiNetwork
                            {
                                SSID = ssid,
                                BSSID = bssid,
                                SignalStrength = signalStrength,
                                SecurityType = security,
                                Band = band,
                                Channel = channel,
                                Discovered = DateTime.UtcNow
                            };

                            // Avoid duplicates
                            if (!networks.Any(n => n.SSID == network.SSID && n.BSSID == network.BSSID))
                            {
                                networks.Add(network);
                            }
                        }
                        catch { }
                    }

                    return networks;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error scanning networks: {ex.Message}");
                    return networks;
                }
            });
        }

        public async Task<WiFiNetwork?> GetConnectedNetwork()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var output = RunCommand(AirportPath, "-I");
                    var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                    string? ssid = null;
                    string? bssid = null;
                    int rssi = -100;
                    int channel = 0;

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();

                        if (trimmed.StartsWith("SSID:"))
                        {
                            ssid = trimmed.Substring(5).Trim();
                        }

                        if (trimmed.StartsWith("BSSID:"))
                        {
                            bssid = trimmed.Substring(6).Trim();
                        }

                        if (trimmed.StartsWith("RSSI:"))
                        {
                            var match = Regex.Match(trimmed, @"-?\d+");
                            if (match.Success) rssi = int.Parse(match.Value);
                        }

                        if (trimmed.StartsWith("channel:"))
                        {
                            var match = Regex.Match(trimmed, @"\d+");
                            if (match.Success) channel = int.Parse(match.Value);
                        }
                    }

                    if (!string.IsNullOrEmpty(ssid))
                    {
                        int signalStrength = Math.Max(0, Math.Min(100, (rssi + 100)));
                        string band = channel <= 14 ? "2.4GHz" : "5GHz";

                        return new WiFiNetwork
                        {
                            SSID = ssid,
                            BSSID = bssid ?? "Unknown",
                            SignalStrength = signalStrength,
                            SecurityType = null,
                            Band = band,
                            Channel = channel,
                            Discovered = DateTime.UtcNow
                        };
                    }

                    return null;
                }
                catch
                {
                    return null;
                }
            });
        }

        public async Task<bool> ConnectAsync(string ssid, string password)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Use networksetup to connect to WiFi
                    // Note: Requires admin privileges
                    RunCommand("networksetup", $"-setairportnetwork en0 \"{ssid}\" \"{password}\"");
                    System.Threading.Thread.Sleep(1000);
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Connection error: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<bool> DisconnectAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Disconnect from WiFi
                    RunCommand("networksetup", "-setairportpower en0 off");
                    System.Threading.Thread.Sleep(500);
                    RunCommand("networksetup", "-setairportpower en0 on");
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<int> GetSignalStrength()
        {
            var network = await GetConnectedNetwork();
            return network?.SignalStrength ?? 0;
        }

        public async Task<bool> IsConnected()
        {
            var network = await GetConnectedNetwork();
            return network != null;
        }

        private string RunCommand(string command, string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output;
        }
    }
}
