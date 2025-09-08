using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 接続回復マネージャー
    /// </summary>
    public class ConnectionRecoveryManager : IDisposable
    {
        private readonly ConnectionStatistics _connectionStats;
        private readonly ConnectionLogger _connectionLogger;
        private readonly Dictionary<string, RecoveryState> _recoveryStates = new();
        private readonly Timer _monitoringTimer;
        private readonly SemaphoreSlim _recoverySemaphore = new(1, 1);
        private bool _disposed;

        public event EventHandler<RecoveryEventArgs>? RecoveryStarted;
        public event EventHandler<RecoveryEventArgs>? RecoveryCompleted;
        public event EventHandler<RecoveryEventArgs>? RecoveryFailed;

        public bool AutoRecoveryEnabled { get; set; } = true;
        public int MaxRetryAttempts { get; set; } = 3;
        private const int MonitoringIntervalMs = 30000; // 30秒

        public ConnectionRecoveryManager(ConnectionStatistics connectionStats, ConnectionLogger connectionLogger)
        {
            _connectionStats = connectionStats ?? throw new ArgumentNullException(nameof(connectionStats));
            _connectionLogger = connectionLogger ?? throw new ArgumentNullException(nameof(connectionLogger));
            
            _monitoringTimer = new Timer(MonitorConnectivity, null, MonitoringIntervalMs, MonitoringIntervalMs);
        }

        /// <summary>
        /// 接続回復を試行
        /// </summary>
        public async Task<RecoveryResult> AttemptRecoveryAsync(string ssid, string password, CancellationToken cancellationToken = default)
        {
            if (!AutoRecoveryEnabled || string.IsNullOrWhiteSpace(ssid))
                return new RecoveryResult { Success = false, Message = "自動復旧が無効または無効なSSID" };

            if (!await _recoverySemaphore.WaitAsync(100, cancellationToken))
                return new RecoveryResult { Success = false, Message = "他の復旧処理が実行中です" };

            try
            {
                var recoveryState = GetOrCreateRecoveryState(ssid);
                if (recoveryState.IsRecovering)
                    return new RecoveryResult { Success = false, Message = "既に復旧処理中です" };

                OnRecoveryStarted(new RecoveryEventArgs { SSID = ssid });
                recoveryState.IsRecovering = true;
                recoveryState.LastAttempt = DateTime.Now;

                var result = await PerformRecoveryAsync(ssid, password, cancellationToken);
                
                recoveryState.IsRecovering = false;
                
                if (result.Success)
                {
                    recoveryState.ConsecutiveFailures = 0;
                    OnRecoveryCompleted(new RecoveryEventArgs { SSID = ssid, Success = true });
                }
                else
                {
                    recoveryState.ConsecutiveFailures++;
                    OnRecoveryFailed(new RecoveryEventArgs { SSID = ssid, Message = result.Message });
                }

                return result;
            }
            finally
            {
                _recoverySemaphore.Release();
            }
        }

        private async Task<RecoveryResult> PerformRecoveryAsync(string ssid, string password, CancellationToken cancellationToken)
        {
            var attempts = 0;
            
            while (attempts < MaxRetryAttempts && !cancellationToken.IsCancellationRequested)
            {
                attempts++;
                
                try
                {
                    // 接続を試行
                    var connectionResult = await FastWifiConnector.ConnectAsync(ssid, password, cancellationToken);
                    
                    if (connectionResult.Success)
                    {
                        _connectionLogger.Log(ConnectionLogger.LogLevel.Info, "Recovery", 
                            $"ネットワーク {ssid} への接続を回復しました（試行回数: {attempts}）");
                        
                        return new RecoveryResult 
                        { 
                            Success = true, 
                            Message = $"接続回復成功（試行回数: {attempts}）",
                            AttemptsCount = attempts
                        };
                    }
                    
                    if (attempts < MaxRetryAttempts)
                    {
                        // エクスポネンシャルバックオフ
                        var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempts), 30));
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError($"ConnectionRecovery.Attempt{attempts}", ex, _connectionLogger);
                    
                    if (attempts >= MaxRetryAttempts)
                    {
                        return new RecoveryResult 
                        { 
                            Success = false, 
                            Message = $"回復失敗: {ex.Message}",
                            AttemptsCount = attempts
                        };
                    }
                }
            }
            
            return new RecoveryResult 
            { 
                Success = false, 
                Message = "最大試行回数に達しました",
                AttemptsCount = attempts
            };
        }

        private async void MonitorConnectivity(object? state)
        {
            if (!AutoRecoveryEnabled || _disposed)
                return;

            try
            {
                await PerformIntelligentMonitoringAsync();
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionMonitor", ex, _connectionLogger);
            }
        }

        /// <summary>
        /// 智能监控连接状态并执行自动操作
        /// </summary>
        private async Task PerformIntelligentMonitoringAsync()
        {
            var currentSSID = NetworkUtils.GetCurrentConnectedSSID();
            
            if (string.IsNullOrEmpty(currentSSID))
            {
                // 接続が失われた場合の処理
                await HandleConnectionLossAsync();
            }
            else
            {
                // 接続品質を監視
                await MonitorConnectionQualityAsync(currentSSID);
            }
        }

        /// <summary>
        /// 接続喪失時の処理
        /// </summary>
        private async Task HandleConnectionLossAsync()
        {
            _connectionLogger.Log(ConnectionLogger.LogLevel.Warning, "Monitor", "接続が失われました");
            
            try
            {
                // 最近成功したネットワークを取得
                var candidates = await GetRecentSuccessfulNetworksAsync();
                if (!candidates.Any())
                    return;

                // 優先度でソート
                var sortedCandidates = candidates
                    .OrderByDescending(c => c.Priority)
                    .ToList();

                // 自動再接続を試行
                foreach (var candidate in sortedCandidates)
                {
                    var result = await AttemptNetworkSwitchAsync(candidate.SSID, candidate.Password);
                    if (result)
                    {
                        _connectionLogger.Log(ConnectionLogger.LogLevel.Info, "AutoReconnect", 
                            $"自動的に {candidate.SSID} に再接続しました");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("HandleConnectionLoss", ex, _connectionLogger);
            }
        }

        /// <summary>
        /// 接続品質を監視し、必要に応じてより良いネットワークに切り替え
        /// </summary>
        private async Task MonitorConnectionQualityAsync(string currentSSID)
        {
            try
            {
                var currentSignal = NetworkUtils.GetSignalStrength(currentSSID);
                if (currentSignal < 30) // 信号強度が30%未満の場合
                {
                    _connectionLogger.Log(ConnectionLogger.LogLevel.Warning, "QualityMonitor", 
                        $"信号強度が低下: {currentSignal}%");
                    
                    // より良いネットワークを探す
                    var betterNetworks = await FindBetterNetworksAsync(currentSSID, currentSignal);
                    if (betterNetworks.Any())
                    {
                        var bestNetwork = betterNetworks.First();
                        var switched = await AttemptNetworkSwitchAsync(bestNetwork.SSID, bestNetwork.Password);
                        
                        if (switched)
                        {
                            _connectionLogger.Log(ConnectionLogger.LogLevel.Info, "NetworkSwitch", 
                                $"より良いネットワーク {bestNetwork.SSID} に切り替えました");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MonitorConnectionQuality", ex, _connectionLogger);
            }
        }

        private RecoveryState GetOrCreateRecoveryState(string ssid)
        {
            if (!_recoveryStates.TryGetValue(ssid, out var state))
            {
                state = new RecoveryState { SSID = ssid };
                _recoveryStates[ssid] = state;
            }
            return state;
        }

        private void OnRecoveryStarted(RecoveryEventArgs e) => RecoveryStarted?.Invoke(this, e);
        private void OnRecoveryCompleted(RecoveryEventArgs e) => RecoveryCompleted?.Invoke(this, e);
        private void OnRecoveryFailed(RecoveryEventArgs e) => RecoveryFailed?.Invoke(this, e);

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _monitoringTimer?.Dispose();
            _recoverySemaphore?.Dispose();
        }

        /// <summary>
        /// 最近成功したネットワークの一覧を取得
        /// </summary>
        private async Task<List<RecoveryCandidate>> GetRecentSuccessfulNetworksAsync()
        {
            var candidates = new List<RecoveryCandidate>();
            
            try
            {
                // 接続履歴から最近24時間以内に成功したネットワークを取得
                var recentStats = await _connectionLogger.GetConnectionStatisticsAsync(DateTime.Now.AddHours(-24));
                
                foreach (var stat in recentStats)
                {
                    if (stat.Value > 0) // 成功した接続がある
                    {
                        // パスワードは WifiProfileManager から取得（暗号化されたものを復号）
                        var password = await WifiProfileManager.GetSavedPasswordAsync(stat.Key);
                        if (!string.IsNullOrEmpty(password))
                        {
                            candidates.Add(new RecoveryCandidate
                            {
                                SSID = stat.Key,
                                Password = password,
                                Priority = CalculateRecoveryPriority(stat.Key, stat.Value)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("GetRecentSuccessfulNetworks", ex, _connectionLogger);
            }
            
            return candidates;
        }

        /// <summary>
        /// 現在のネットワークよりも良いネットワークを探す
        /// </summary>
        private async Task<List<RecoveryCandidate>> FindBetterNetworksAsync(string currentSSID, int currentSignal)
        {
            var betterNetworks = new List<RecoveryCandidate>();
            
            try
            {
                var availableNetworks = await FastWifiConnector.ScanNetworksAsync();
                var savedProfiles = await WifiProfileManager.GetSavedProfilesAsync();
                
                foreach (var network in availableNetworks)
                {
                    if (network.SSID != currentSSID && 
                        network.SignalStrength > currentSignal + 15 && // 15%以上改善される場合
                        savedProfiles.Any(p => p.SSID == network.SSID))
                    {
                        var password = await WifiProfileManager.GetSavedPasswordAsync(network.SSID);
                        if (!string.IsNullOrEmpty(password))
                        {
                            betterNetworks.Add(new RecoveryCandidate
                            {
                                SSID = network.SSID,
                                Password = password,
                                Priority = CalculateRecoveryPriority(network.SSID, network.SignalStrength)
                            });
                        }
                    }
                }
                
                return betterNetworks.OrderByDescending(n => n.Priority).ToList();
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("FindBetterNetworks", ex, _connectionLogger);
                return betterNetworks;
            }
        }

        /// <summary>
        /// ネットワーク切り替えを試行
        /// </summary>
        private async Task<bool> AttemptNetworkSwitchAsync(string ssid, string password)
        {
            try
            {
                var result = await FastWifiConnector.ConnectAsync(ssid, password, CancellationToken.None);
                return result.Success;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("AttemptNetworkSwitch", ex, _connectionLogger);
                return false;
            }
        }

        /// <summary>
        /// 回復優先度を計算
        /// </summary>
        private double CalculateRecoveryPriority(string ssid, int value)
        {
            // 基本優先度は成功回数やシグナル強度に基づく
            var priority = value * 0.3; // 成功回数の重み
            
            // 最後の使用時間も考慮
            if (_recoveryStates.TryGetValue(ssid, out var state))
            {
                var hoursSinceLastUse = (DateTime.Now - state.LastAttempt).TotalHours;
                priority += Math.Max(0, 24 - hoursSinceLastUse) * 0.1; // 最近使用した場合に優先度アップ
                priority -= state.ConsecutiveFailures * 0.2; // 失敗回数で優先度ダウン
            }
            
            return Math.Max(0, priority);
        }

        // 内部クラス
        private class RecoveryState
        {
            public string SSID { get; set; } = string.Empty;
            public bool IsRecovering { get; set; }
            public int ConsecutiveFailures { get; set; }
            public DateTime LastAttempt { get; set; }
        }
        
        private class RecoveryCandidate
        {
            public string SSID { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public double Priority { get; set; }
        }
    }

    /// <summary>
    /// 回復結果
    /// </summary>
    public class RecoveryResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int AttemptsCount { get; set; }
    }

    /// <summary>
    /// 回復イベント引数
    /// </summary>
    public class RecoveryEventArgs : EventArgs
    {
        public string SSID { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Message { get; set; }
    }
}