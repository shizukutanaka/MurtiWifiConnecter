namespace MurtiWifiConnecter.Core;

public interface IWifiManager
{
    /// <summary>
    /// Get list of available WiFi networks
    /// </summary>
    Task<List<WiFiNetwork>> GetAvailableNetworks();

    /// <summary>
    /// Get currently connected network
    /// </summary>
    Task<WiFiNetwork?> GetConnectedNetwork();

    /// <summary>
    /// Connect to a WiFi network
    /// </summary>
    Task<bool> ConnectAsync(string ssid, string password);

    /// <summary>
    /// Disconnect from current network
    /// </summary>
    Task<bool> DisconnectAsync();

    /// <summary>
    /// Get signal strength (0-100)
    /// </summary>
    Task<int> GetSignalStrength();

    /// <summary>
    /// Check if connection is active
    /// </summary>
    Task<bool> IsConnected();
}

public class WiFiNetwork
{
    public required string SSID { get; set; }
    public required string BSSID { get; set; }
    public int SignalStrength { get; set; }
    public string? SecurityType { get; set; }
    public required string Band { get; set; }
    public int Channel { get; set; }
    public required DateTime Discovered { get; set; }

    public override string ToString() => $"{SSID} ({SignalStrength}%) - {Band}";
}
