using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Interface for network operations with WiFi 7 support
    /// </summary>
    public interface INetworkOperations
    {
        /// <summary>
        /// Scan for available networks
        /// </summary>
        Task<List<NetworkInfo>> ScanNetworksAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Connect to a network
        /// </summary>
        Task<bool> ConnectAsync(string ssid, string password = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disconnect from current network
        /// </summary>
        Task<bool> DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get current connection status
        /// </summary>
        Task<ConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get saved network profiles
        /// </summary>
        Task<List<string>> GetSavedProfilesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Delete a saved network profile
        /// </summary>
        Task<bool> DeleteProfileAsync(string ssid, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enhanced WiFi 7 scanning with multi-link operation detection
        /// </summary>
        Task<List<NetworkInfo>> ScanNetworksEnhancedAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Connect with WiFi 7 optimizations
        /// </summary>
        Task<bool> ConnectEnhancedAsync(string ssid, string password = null, WifiStandard preferredStandard = WifiStandard.WiFi7, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get detailed network capabilities
        /// </summary>
        Task<NetworkCapabilities> GetNetworkCapabilitiesAsync(string ssid, CancellationToken cancellationToken = default);

        /// <summary>
        /// Optimize connection based on network capabilities
        /// </summary>
        Task<bool> OptimizeConnectionAsync(string ssid, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Network capabilities information
    /// </summary>
    public class NetworkCapabilities
    {
        public string Ssid { get; set; } = string.Empty;
        public WifiStandard MaxSupportedStandard { get; set; } = WifiStandard.WiFi6;
        public List<string> SupportedBands { get; set; } = new List<string> { "2.4GHz", "5GHz" };
        public int MaxSpatialStreams { get; set; } = 4;
        public bool Supports320MHz { get; set; }
        public bool Supports4KQAM { get; set; }
        public bool SupportsOFDMA { get; set; }
        public bool SupportsMUMIMO { get; set; }
        public bool SupportsTWT { get; set; }
        public MultiLinkCapabilities MultiLinkCapabilities { get; set; } = new MultiLinkCapabilities();
        public double TheoreticalMaxSpeed { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
