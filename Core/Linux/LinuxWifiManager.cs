using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Linux WiFi management using nmcli (NetworkManager)
    /// </summary>
    public class LinuxWifiManager : IWifiManager
    {
        public async Task<List<WiFiNetwork>> GetAvailableNetworks()
        {
            return await Task.Run(() =>
            {
                var networks = new List<WiFiNetwork>();
                try
                {
                    // Try nmcli first
                    if (CommandExists("nmcli"))
                    {
                        return GetNetworksWithNmcli();
                    }

                    // Fallback to wpa_cli
                    if (CommandExists("wpa_cli"))
                    {
                        return GetNetworksWithWpaCli();
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
                    if (CommandExists("nmcli"))
                    {
                        var output = RunCommand("nmcli", "device wifi show");
                        var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                        string? ssid = null;
                        int signal = 0;
                        string? band = null;

                        foreach (var line in lines)
                        {
                            if (line.Contains("SSID:"))
                            {
                                var parts = line.Split(':');
                                if (parts.Length > 1) ssid = parts[1].Trim();
                            }

                            if (line.Contains("SIGNAL:"))
                            {
                                var match = Regex.Match(line, @"(\d+)");
                                if (match.Success) signal = int.Parse(match.Groups[1].Value);
                            }

                            if (line.Contains("FREQ:"))
                            {
                                var match = Regex.Match(line, @"(\d+)");
                                if (match.Success)
                                {
                                    int freq = int.Parse(match.Groups[1].Value);
                                    band = freq < 3000 ? "2.4GHz" : "5GHz";
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(ssid))
                        {
                            return new WiFiNetwork
                            {
                                SSID = ssid,
                                BSSID = "Unknown",
                                SignalStrength = signal,
                                SecurityType = null,
                                Band = band ?? "Unknown",
                                Channel = 0,
                                Discovered = DateTime.UtcNow
                            };
                        }
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
                    if (CommandExists("nmcli"))
                    {
                        // Create and connect to WiFi network using nmcli
                        RunCommand("nmcli", $"device wifi connect \"{ssid}\" password \"{password}\"");
                        System.Threading.Thread.Sleep(1000);
                        return true;
                    }

                    return false;
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
                    if (CommandExists("nmcli"))
                    {
                        RunCommand("nmcli", "device disconnect wlan0");
                        return true;
                    }

                    return false;
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

        private List<WiFiNetwork> GetNetworksWithNmcli()
        {
            var networks = new List<WiFiNetwork>();
            try
            {
                var output = RunCommand("nmcli", "device wifi list");
                var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                foreach (var line in lines.Skip(1)) // Skip header
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5) continue;

                    try
                    {
                        var network = new WiFiNetwork
                        {
                            BSSID = parts[0],
                            SSID = parts[1],
                            Band = parts[2],
                            Channel = int.TryParse(parts[3], out var ch) ? ch : 0,
                            SignalStrength = int.TryParse(parts[4], out var sig) ? sig : 0,
                            SecurityType = parts.Length > 5 ? parts[5] : null,
                            Discovered = DateTime.UtcNow
                        };

                        networks.Add(network);
                    }
                    catch { }
                }
            }
            catch { }

            return networks;
        }

        private List<WiFiNetwork> GetNetworksWithWpaCli()
        {
            var networks = new List<WiFiNetwork>();
            try
            {
                var output = RunCommand("wpa_cli", "scan");
                System.Threading.Thread.Sleep(2000); // Wait for scan

                output = RunCommand("wpa_cli", "scan_results");
                var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split('\t');
                    if (parts.Length < 4) continue;

                    try
                    {
                        var network = new WiFiNetwork
                        {
                            BSSID = parts[0],
                            SignalStrength = int.TryParse(parts[2], out var sig) ? Math.Min(100, (sig + 100)) : 0,
                            SecurityType = parts[3],
                            SSID = parts.Length > 4 ? parts[4] : "Hidden",
                            Band = "Unknown",
                            Channel = 0,
                            Discovered = DateTime.UtcNow
                        };

                        networks.Add(network);
                    }
                    catch { }
                }
            }
            catch { }

            return networks;
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
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output;
        }

        private bool CommandExists(string command)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = command,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
            }
            catch
            {
                return false;
            }
        }
    }
}
