using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 統合接続監視システム - ConnectionHealthChecker と ConnectionQualityMonitor を統合
    /// </summary>
    public class ConnectionMonitor : IDisposable
    {
        private readonly ConnectionLogger _logger;
        private readonly Timer _monitorTimer;
        private volatile ConnectionStatus _currentStatus;
        private readonly SemaphoreSlim _monitorLock = new(1, 1);
        
        public event EventHandler<ConnectionStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<ConnectionDegradedEventArgs>? ConnectionDegraded;
        
        public ConnectionStatus CurrentStatus => _currentStatus;
        
        public ConnectionMonitor(ConnectionLogger logger)
        {
            _logger = logger;
            _currentStatus = new ConnectionStatus();
            
            // 30秒間隔で監視
            _monitorTimer = new Timer(MonitorConnection, null, 
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
        }
        
        private async void MonitorConnection(object? state)
        {
            if (!await _monitorLock.WaitAsync(1000))
                return;
                
            try
            {
                var newStatus = await CheckConnectionStatusAsync();
                
                // ステータス変更を通知
                if (HasSignificantChange(newStatus))
                {
                    var previousStatus = _currentStatus;
                    _currentStatus = newStatus;
                    
                    StatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs 
                    { 
                        PreviousStatus = previousStatus, 
                        CurrentStatus = newStatus 
                    });
                    
                    // 品質低下の場合は追加通知
                    if (newStatus.Quality < previousStatus.Quality && 
                        newStatus.Quality <= ConnectionQuality.Poor)
                    {
                        ConnectionDegraded?.Invoke(this, new ConnectionDegradedEventArgs
                        {
                            Status = newStatus,
                            Reason = GetDegradationReason(previousStatus, newStatus)
                        });
                    }
                    
                    LogStatusChange(previousStatus, newStatus);
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionMonitor.MonitorConnection", ex, _logger);
            }
            finally
            {
                _monitorLock.Release();
            }
        }
        
        private async Task<ConnectionStatus> CheckConnectionStatusAsync()
        {
            var status = new ConnectionStatus
            {
                Timestamp = DateTime.Now
            };
            
            try
            {
                // 現在のSSIDを取得
                status.SSID = await GetCurrentSSIDAsync();
                
                if (string.IsNullOrEmpty(status.SSID))
                {
                    status.Quality = ConnectionQuality.Disconnected;
                    status.SignalStrength = 0;
                    status.Latency = TimeSpan.Zero;
                    return status;
                }
                
                // 信号強度を取得
                status.SignalStrength = await GetSignalStrengthAsync(status.SSID);
                
                // レイテンシを測定
                status.Latency = await MeasureLatencyAsync();
                
                // 品質を評価
                status.Quality = EvaluateQuality(status.SignalStrength, status.Latency);
                
                return status;
            }
            catch
            {
                status.Quality = ConnectionQuality.Error;
                return status;
            }
        }
        
        private async Task<string?> GetCurrentSSIDAsync()
        {
            try
            {
                return await OptimizedWifiScanner.GetCurrentSSIDAsync(CancellationToken.None);
            }
            catch
            {
                return null;
            }
        }
        
        private async Task<int> GetSignalStrengthAsync(string ssid)
        {
            try
            {
                var networks = await NetworkUtils.ScanWifiNetworksAsync();
                return networks.TryGetValue(ssid, out var strength) ? strength : 0;
            }
            catch
            {
                return 0;
            }
        }
        
        private async Task<TimeSpan> MeasureLatencyAsync()
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 3000);
                return reply.Status == IPStatus.Success ? 
                    TimeSpan.FromMilliseconds(reply.RoundtripTime) : 
                    TimeSpan.FromMilliseconds(9999);
            }
            catch
            {
                return TimeSpan.FromMilliseconds(9999);
            }
        }
        
        private ConnectionQuality EvaluateQuality(int signalStrength, TimeSpan latency)
        {
            var signalScore = signalStrength switch
            {
                >= 80 => 25,
                >= 60 => 20,
                >= 40 => 15,
                >= 20 => 10,
                _ => 5
            };
            
            var latencyScore = (int)latency.TotalMilliseconds switch
            {
                <= 50 => 25,
                <= 100 => 20,
                <= 200 => 15,
                <= 500 => 10,
                _ => 5
            };
            
            var totalScore = signalScore + latencyScore;
            
            return totalScore switch
            {
                >= 45 => ConnectionQuality.Excellent,
                >= 35 => ConnectionQuality.Good,
                >= 25 => ConnectionQuality.Fair,
                >= 15 => ConnectionQuality.Poor,
                _ => ConnectionQuality.VeryPoor
            };
        }
        
        private bool HasSignificantChange(ConnectionStatus newStatus)
        {
            var current = _currentStatus;
            
            return current.SSID != newStatus.SSID ||
                   current.Quality != newStatus.Quality ||
                   Math.Abs(current.SignalStrength - newStatus.SignalStrength) >= 10 ||
                   Math.Abs((current.Latency - newStatus.Latency).TotalMilliseconds) >= 100;
        }
        
        private string GetDegradationReason(ConnectionStatus previous, ConnectionStatus current)
        {
            if (current.SignalStrength < previous.SignalStrength - 15)
                return "信号強度の低下";
                
            if ((current.Latency - previous.Latency).TotalMilliseconds > 200)
                return "レイテンシの増加";
                
            return "接続品質の低下";
        }
        
        private void LogStatusChange(ConnectionStatus previous, ConnectionStatus current)
        {
            _logger?.Log(ConnectionLogger.LogLevel.Info, "Monitor", 
                $"Status: {current.SSID} - Quality: {previous.Quality} → {current.Quality}, " +
                $"Signal: {previous.SignalStrength}% → {current.SignalStrength}%, " +
                $"Latency: {(int)previous.Latency.TotalMilliseconds}ms → {(int)current.Latency.TotalMilliseconds}ms");
        }
        
        public void Dispose()
        {
            _monitorTimer?.Dispose();
            _monitorLock?.Dispose();
        }
    }
    
    public class ConnectionStatus
    {
        public string SSID { get; set; } = string.Empty;
        public ConnectionQuality Quality { get; set; } = ConnectionQuality.Unknown;
        public int SignalStrength { get; set; }
        public TimeSpan Latency { get; set; }
        public DateTime Timestamp { get; set; }
        
        public string GetQualityDescription()
        {
            return Quality switch
            {
                ConnectionQuality.Excellent => "優秀",
                ConnectionQuality.Good => "良好",
                ConnectionQuality.Fair => "普通",
                ConnectionQuality.Poor => "弱い",
                ConnectionQuality.VeryPoor => "非常に弱い",
                ConnectionQuality.Disconnected => "未接続",
                _ => "不明"
            };
        }
    }
    
    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        public ConnectionStatus PreviousStatus { get; set; } = new();
        public ConnectionStatus CurrentStatus { get; set; } = new();
    }
    
    public class ConnectionDegradedEventArgs : EventArgs
    {
        public ConnectionStatus Status { get; set; } = new();
        public string Reason { get; set; } = string.Empty;
    }
}

// OptimizedWifiScanner の拡張メソッド
namespace MurtiWifiConnecter
{
    public static class OptimizedWifiScannerExtensions
    {
        public static async Task<string?> GetCurrentSSIDAsync(this Type _, CancellationToken cancellationToken)
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "wlan show interfaces",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var timeoutTask = Task.Delay(2000, cancellationToken);
                
                var completedTask = await Task.WhenAny(outputTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    return null;
                }
                
                var output = await outputTask;
                process.WaitForExit(500);
                
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("SSID") && trimmed.Contains(":"))
                    {
                        var parts = trimmed.Split(':');
                        if (parts.Length >= 2)
                        {
                            var ssid = string.Join(":", parts[1..]).Trim();
                            if (!string.IsNullOrWhiteSpace(ssid))
                            {
                                return ssid;
                            }
                        }
                    }
                }
            }
            catch
            {
                // エラーは無視
            }
            
            return null;
        }
    }
}