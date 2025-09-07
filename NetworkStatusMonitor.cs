using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    public class NetworkStatusMonitor : IDisposable
    {
        private readonly Timer _statusTimer;
        private bool _lastConnectionStatus;
        private string _lastConnectedSSID = string.Empty;
        private readonly SemaphoreSlim _monitorSemaphore = new(1, 1);
        private bool _disposed;

        public event EventHandler<NetworkStatusChangedEventArgs>? StatusChanged;

        public NetworkStatusMonitor()
        {
            _statusTimer = new Timer(CheckStatusAsync, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        }

        private async void CheckStatusAsync(object? state)
        {
            if (_disposed || !await _monitorSemaphore.WaitAsync(100))
                return;

            try
            {
                bool isConnected = NetworkInterface.GetIsNetworkAvailable();
                string currentSSID = await GetCurrentSSIDAsync();

                bool statusChanged = _lastConnectionStatus != isConnected ||
                                   !string.Equals(_lastConnectedSSID, currentSSID, StringComparison.OrdinalIgnoreCase);

                if (statusChanged)
                {
                    _lastConnectionStatus = isConnected;
                    _lastConnectedSSID = currentSSID;

                    StatusChanged?.Invoke(this, new NetworkStatusChangedEventArgs
                    {
                        IsConnected = isConnected,
                        ConnectedSSID = currentSSID,
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch { }
            finally
            {
                _monitorSemaphore.Release();
            }
        }

        private async Task<string> GetCurrentSSIDAsync()
        {
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("netsh", "wlan show interfaces")
                        {
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        };

                        using var proc = System.Diagnostics.Process.Start(psi);
                        if (proc == null) return string.Empty;

                        if (!proc.WaitForExit(3000))
                        {
                            proc.Kill();
                            return string.Empty;
                        }

                        string output = proc.StandardOutput.ReadToEnd();
                        if (proc.ExitCode != 0) return string.Empty;

                        var lines = output.Split('\n');
                        foreach (var line in lines)
                        {
                            var trimmedLine = line.Trim();
                            if (trimmedLine.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                                !trimmedLine.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                            {
                                var colonIndex = trimmedLine.IndexOf(':');
                                if (colonIndex > 0 && colonIndex < trimmedLine.Length - 1)
                                {
                                    var ssid = trimmedLine.Substring(colonIndex + 1).Trim();
                                    return string.IsNullOrWhiteSpace(ssid) ? string.Empty : ssid;
                                }
                            }
                        }
                    }
                    catch { }
                    return string.Empty;
                });
            }
            catch
            {
                return string.Empty;
            }
        }

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            // ネットワーク可用性変更時に即座にチェック
            _ = Task.Run(() => CheckStatusAsync(null));
        }

        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            // ネットワークアドレス変更時に1秒後チェック（遅延はIP取得待ち）
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                CheckStatusAsync(null);
            });
        }

        public async Task<NetworkStatus> GetCurrentStatusAsync()
        {
            await _monitorSemaphore.WaitAsync();
            try
            {
                bool isConnected = NetworkInterface.GetIsNetworkAvailable();
                string ssid = await GetCurrentSSIDAsync();

                return new NetworkStatus
                {
                    IsConnected = isConnected,
                    ConnectedSSID = ssid,
                    HasInternetAccess = await HasInternetAccessAsync(),
                    Timestamp = DateTime.Now
                };
            }
            finally
            {
                _monitorSemaphore.Release();
            }
        }

        private static async Task<bool> HasInternetAccessAsync()
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 3000);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            _statusTimer?.Dispose();
            _monitorSemaphore?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public class NetworkStatusChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public string ConnectedSSID { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class NetworkStatus
    {
        public bool IsConnected { get; set; }
        public string ConnectedSSID { get; set; } = string.Empty;
        public bool HasInternetAccess { get; set; }
        public DateTime Timestamp { get; set; }
    }
}