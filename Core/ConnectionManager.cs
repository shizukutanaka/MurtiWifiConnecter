using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Manages WiFi connections with retry logic and stability monitoring
    /// </summary>
    public sealed class ConnectionManager : IDisposable
    {
        private readonly SimplifiedWifiManager _wifiManager;
        private readonly System.Timers.Timer _stabilityCheckTimer;
        private readonly System.Timers.Timer _reconnectTimer;
        private readonly Queue<ConnectionAttempt> _connectionHistory;

        private string? _currentSSID;
        private string? _currentPassword;
        private int _consecutiveFailures;
        private DateTime _lastSuccessfulConnection;
        private bool _isMonitoring;
        private bool _isReconnecting;

        // Configuration
        private const int MaxRetryAttempts = 3;
        private const int RetryDelaySeconds = 5;
        private const int StabilityCheckIntervalSeconds = 60;
        private const int MaxHistorySize = 100;

        public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;
        public event EventHandler<StabilityReportEventArgs>? StabilityReportAvailable;

        public ConnectionManager(SimplifiedWifiManager wifiManager)
        {
            _wifiManager = wifiManager ?? throw new ArgumentNullException(nameof(wifiManager));
            _connectionHistory = new Queue<ConnectionAttempt>(MaxHistorySize);

            _stabilityCheckTimer = new System.Timers.Timer(StabilityCheckIntervalSeconds * 1000);
            _stabilityCheckTimer.Elapsed += OnStabilityCheck;

            _reconnectTimer = new System.Timers.Timer(RetryDelaySeconds * 1000);
            _reconnectTimer.Elapsed += OnReconnectTimerElapsed;
        }

        /// <summary>
        /// Connect with automatic retry logic
        /// </summary>
        public async Task<bool> ConnectWithRetryAsync(string ssid, string password, CancellationToken ct = default)
        {
            _currentSSID = ssid;
            _currentPassword = password;
            _consecutiveFailures = 0;

            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                if (ct.IsCancellationRequested)
                    break;

                OnConnectionStatusChanged($"Connection attempt {attempt} of {MaxRetryAttempts}...", ExtendedConnectionState.Connecting);

                var success = await _wifiManager.ConnectAsync(ssid, password, ct);

                if (success)
                {
                    _lastSuccessfulConnection = DateTime.UtcNow;
                    _consecutiveFailures = 0;
                    RecordConnectionAttempt(ssid, true, attempt);
                    OnConnectionStatusChanged($"Connected to {ssid}", ExtendedConnectionState.Connected);
                    StartStabilityMonitoring();
                    return true;
                }

                _consecutiveFailures++;
                RecordConnectionAttempt(ssid, false, attempt);

                if (attempt < MaxRetryAttempts)
                {
                    OnConnectionStatusChanged($"Retrying in {RetryDelaySeconds} seconds...", ExtendedConnectionState.Retrying);
                    await Task.Delay(RetryDelaySeconds * 1000, ct);
                }
            }

            OnConnectionStatusChanged("Connection failed after all retry attempts", ExtendedConnectionState.Failed);
            return false;
        }

        /// <summary>
        /// Start monitoring connection stability
        /// </summary>
        public void StartStabilityMonitoring()
        {
            if (!_isMonitoring && !string.IsNullOrEmpty(_currentSSID))
            {
                _isMonitoring = true;
                _stabilityCheckTimer.Start();
                OnConnectionStatusChanged("Stability monitoring started", ExtendedConnectionState.Monitoring);
            }
        }

        /// <summary>
        /// Stop monitoring connection stability
        /// </summary>
        public void StopStabilityMonitoring()
        {
            if (_isMonitoring)
            {
                _isMonitoring = false;
                _stabilityCheckTimer.Stop();
                OnConnectionStatusChanged("Stability monitoring stopped", ExtendedConnectionState.Idle);
            }
        }

        /// <summary>
        /// Enable automatic reconnection on connection loss
        /// </summary>
        public void EnableAutoReconnect(bool enable = true)
        {
            if (enable && !string.IsNullOrEmpty(_currentSSID) && !string.IsNullOrEmpty(_currentPassword))
            {
                _reconnectTimer.AutoReset = true;
                OnConnectionStatusChanged("Auto-reconnect enabled", ExtendedConnectionState.Monitoring);
            }
            else
            {
                _reconnectTimer.Stop();
                _isReconnecting = false;
                OnConnectionStatusChanged("Auto-reconnect disabled", ExtendedConnectionState.Idle);
            }
        }

        /// <summary>
        /// Get connection statistics
        /// </summary>
        public ExtendedConnectionStatistics GetStatistics()
        {
            int totalAttempts = 0;
            int successfulAttempts = 0;
            double averageAttempts = 0;

            foreach (var attempt in _connectionHistory)
            {
                totalAttempts++;
                if (attempt.Success)
                    successfulAttempts++;
                averageAttempts += attempt.AttemptNumber;
            }

            if (totalAttempts > 0)
                averageAttempts /= totalAttempts;

            var uptime = _lastSuccessfulConnection != DateTime.MinValue
                ? DateTime.UtcNow - _lastSuccessfulConnection
                : TimeSpan.Zero;

            return new ExtendedConnectionStatistics
            {
                TotalAttempts = totalAttempts,
                SuccessfulConnections = successfulAttempts,
                FailedConnections = totalAttempts - successfulAttempts,
                SuccessRate = totalAttempts > 0 ? (double)successfulAttempts / totalAttempts * 100 : 0,
                AverageAttemptsToConnect = averageAttempts,
                CurrentUptime = uptime,
                LastSuccessfulConnection = _lastSuccessfulConnection,
                ConsecutiveFailures = _consecutiveFailures
            };
        }

        private async void OnStabilityCheck(object? sender, ElapsedEventArgs e)
        {
            if (!_isMonitoring || string.IsNullOrEmpty(_currentSSID))
                return;

            try
            {
                var currentSSID = await _wifiManager.GetCurrentSSIDAsync();

                if (!string.Equals(currentSSID, _currentSSID, StringComparison.OrdinalIgnoreCase))
                {
                    OnConnectionStatusChanged($"Connection lost to {_currentSSID}", ExtendedConnectionState.Disconnected);

                    if (!_isReconnecting && !string.IsNullOrEmpty(_currentPassword))
                    {
                        _isReconnecting = true;
                        _reconnectTimer.Start();
                    }
                }
                else
                {
                    var stats = GetStatistics();
                    OnStabilityReportAvailable(new StabilityReportEventArgs
                    {
                        IsStable = true,
                        Uptime = stats.CurrentUptime,
                        SignalQuality = await GetSignalQualityAsync()
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Stability check error: {ex.Message}", ex);
            }
        }

        private async void OnReconnectTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_isReconnecting || string.IsNullOrEmpty(_currentSSID) || string.IsNullOrEmpty(_currentPassword))
                return;

            try
            {
                OnConnectionStatusChanged($"Auto-reconnecting to {_currentSSID}...", ExtendedConnectionState.Reconnecting);

                var success = await ConnectWithRetryAsync(_currentSSID, _currentPassword);

                if (success)
                {
                    _isReconnecting = false;
                    _reconnectTimer.Stop();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Auto-reconnect error: {ex.Message}", ex);
            }
        }

        private async Task<int> GetSignalQualityAsync()
        {
            try
            {
                var networks = await _wifiManager.ScanNetworksAsync();

                foreach (var network in networks)
                {
                    if (string.Equals(network.SSID, _currentSSID, StringComparison.OrdinalIgnoreCase))
                    {
                        return network.SignalStrength;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get signal quality: {ex.Message}", ex);
            }

            return 0;
        }

        private void RecordConnectionAttempt(string ssid, bool success, int attemptNumber)
        {
            var attempt = new ConnectionAttempt
            {
                SSID = ssid,
                Success = success,
                AttemptNumber = attemptNumber,
                Timestamp = DateTime.UtcNow
            };

            _connectionHistory.Enqueue(attempt);

            while (_connectionHistory.Count > MaxHistorySize)
            {
                _connectionHistory.Dequeue();
            }
        }

        private void OnConnectionStatusChanged(string message, ExtendedConnectionState state)
        {
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs
            {
                Message = message,
                State = state,
                SSID = _currentSSID,
                Timestamp = DateTime.UtcNow
            });
        }

        private void OnStabilityReportAvailable(StabilityReportEventArgs args)
        {
            StabilityReportAvailable?.Invoke(this, args);
        }

        public void Dispose()
        {
            StopStabilityMonitoring();
            _stabilityCheckTimer?.Dispose();
            _reconnectTimer?.Dispose();
        }
    }

    // Supporting classes
    public class ConnectionAttempt
    {
        public string SSID { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int AttemptNumber { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ExtendedConnectionStatistics
    {
        public int TotalAttempts { get; set; }
        public int SuccessfulConnections { get; set; }
        public int FailedConnections { get; set; }
        public double SuccessRate { get; set; }
        public double AverageAttemptsToConnect { get; set; }
        public TimeSpan CurrentUptime { get; set; }
        public DateTime LastSuccessfulConnection { get; set; }
        public int ConsecutiveFailures { get; set; }
    }

    public enum ExtendedConnectionState
    {
        Idle,
        Connecting,
        Connected,
        Disconnected,
        Retrying,
        Reconnecting,
        Failed,
        Monitoring
    }

    public class ConnectionStatusEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public ExtendedConnectionState State { get; set; }
        public string? SSID { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class StabilityReportEventArgs : EventArgs
    {
        public bool IsStable { get; set; }
        public TimeSpan Uptime { get; set; }
        public int SignalQuality { get; set; }
    }

}