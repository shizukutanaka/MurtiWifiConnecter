using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Windows WiFi management using netsh command
    /// </summary>
    public class WindowsWifiManager : IWifiManager
    {
        public async Task<List<WiFiNetwork>> GetAvailableNetworks()
        {
            return await Task.Run(() =>
            {
                var networks = new List<WiFiNetwork>();
                try
                {
                    var output = RunNetsh("wlan show network mode=bssid");
                    var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                    string? currentSSID = null;
                    string? currentBSSID = null;
                    int? currentSignal = null;
                    string? currentBand = null;
                    int currentChannel = 0;
                    string? currentSecurity = null;

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();

                        // Extract SSID
                        if (trimmed.StartsWith("SSID"))
                        {
                            var match = Regex.Match(trimmed, @"SSID\s*:\s*(.+)");
                            if (match.Success)
                            {
                                currentSSID = match.Groups[1].Value.Trim();
                            }
                        }

                        // Extract Signal Strength
                        if (trimmed.StartsWith("Signal"))
                        {
                            var match = Regex.Match(trimmed, @"Signal\s*:\s*(\d+)%");
                            if (match.Success)
                            {
                                currentSignal = int.Parse(match.Groups[1].Value);
                            }
                        }

                        // Extract Security
                        if (trimmed.StartsWith("Authentication"))
                        {
                            var match = Regex.Match(trimmed, @"Authentication\s*:\s*(.+)");
                            if (match.Success)
                            {
                                currentSecurity = match.Groups[1].Value.Trim();
                            }
                        }

                        // Extract BSSID and band info
                        if (trimmed.StartsWith("BSSID"))
                        {
                            var match = Regex.Match(trimmed, @"BSSID\s*:\s*([0-9A-Fa-f:]+)");
                            if (match.Success)
                            {
                                currentBSSID = match.Groups[1].Value;

                                // Detect band based on channel frequency
                                if (currentSignal.HasValue && !string.IsNullOrEmpty(currentSSID))
                                {
                                    currentBand = DetectBand(currentSignal.Value);

                                    var network = new WiFiNetwork
                                    {
                                        SSID = currentSSID,
                                        BSSID = currentBSSID,
                                        SignalStrength = currentSignal.Value,
                                        SecurityType = currentSecurity,
                                        Band = currentBand,
                                        Channel = currentChannel,
                                        Discovered = DateTime.UtcNow
                                    };

                                    // Avoid duplicates
                                    if (!networks.Any(n => n.SSID == network.SSID && n.BSSID == network.BSSID))
                                    {
                                        networks.Add(network);
                                    }
                                }
                            }
                        }
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
                    var output = RunNetsh("wlan show interfaces");
                    var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                    string? ssid = null;
                    int signalStrength = 0;
                    string? band = "Unknown";

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();

                        if (trimmed.StartsWith("SSID"))
                        {
                            var match = Regex.Match(trimmed, @"SSID\s*:\s*(.+)");
                            if (match.Success)
                            {
                                ssid = match.Groups[1].Value.Trim();
                            }
                        }

                        if (trimmed.StartsWith("Signal"))
                        {
                            var match = Regex.Match(trimmed, @"Signal\s*:\s*(\d+)%");
                            if (match.Success)
                            {
                                signalStrength = int.Parse(match.Groups[1].Value);
                            }
                        }

                        if (trimmed.StartsWith("Channel"))
                        {
                            var match = Regex.Match(trimmed, @"Channel\s*:\s*(\d+)");
                            if (match.Success)
                            {
                                int channel = int.Parse(match.Groups[1].Value);
                                band = DetectBand(channel);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(ssid))
                    {
                        return new WiFiNetwork
                        {
                            SSID = ssid,
                            BSSID = "Unknown",
                            SignalStrength = signalStrength,
                            SecurityType = null,
                            Band = band ?? "Unknown",
                            Channel = 0,
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
                    // Create XML profile for connection
                    string profile = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig>
        <SSID>
            <hex>{string.Join("", Encoding.UTF8.GetBytes(ssid).Select(b => b.ToString("X2")))}</hex>
            <name>{ssid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <MSM>
        <security>
            <authEncryption>
                <authentication>open</authentication>
                <encryption>WPA2Personal</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{password}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";

                    // Save profile to temporary file
                    string tempFile = Path.Combine(Path.GetTempPath(), $"{ssid}.xml");
                    File.WriteAllText(tempFile, profile);

                    try
                    {
                        // Add the profile
                        RunNetsh($"wlan add profile filename=\"{tempFile}\"");

                        // Connect to the network
                        RunNetsh($"wlan connect name=\"{ssid}\"");

                        // Wait a moment for connection
                        System.Threading.Thread.Sleep(1000);

                        return true;
                    }
                    finally
                    {
                        // Clean up temp file
                        try { File.Delete(tempFile); } catch { }
                    }
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
                    RunNetsh("wlan disconnect");
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

        private static string RunNetsh(string command)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = command,
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

        private static string DetectBand(int signalOrChannel)
        {
            // Simple heuristic: 5GHz has different characteristics
            // In real implementation, would check frequency data
            return "2.4GHz";
        }
    }
}
