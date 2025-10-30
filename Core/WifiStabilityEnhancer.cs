using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi接続安定性の向上を管理するクラス
    /// 自動再接続と予測的なネットワーク切り替え機能を提供
    /// </summary>
    public class WifiStabilityEnhancer
    {
        private readonly ILogger<WifiStabilityEnhancer> _logger;
        private readonly IWifiManager _wifiManager;
        private readonly ConnectionStabilityMonitor _stabilityMonitor;
        private readonly WifiConnectionOptimizer _connectionOptimizer;

        private Timer? _autoReconnectTimer;
        private Timer? _stabilityCheckTimer;
        private CancellationTokenSource? _monitoringCts;

        private bool _isMonitoring;
        private string? _currentNetwork;
        private DateTime _lastConnectionAttempt;

        // 設定値
        private const int AutoReconnectIntervalSeconds = 30;
        private const int StabilityCheckIntervalSeconds = 15;
        private const int MaxReconnectAttempts = 3;
        private const int MinConnectionIntervalSeconds = 60;

        public WifiStabilityEnhancer(
            ILogger<WifiStabilityEnhancer> logger,
            IWifiManager wifiManager,
            ConnectionStabilityMonitor stabilityMonitor,
            WifiConnectionOptimizer connectionOptimizer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _wifiManager = wifiManager ?? throw new ArgumentNullException(nameof(wifiManager));
            _stabilityMonitor = stabilityMonitor ?? throw new ArgumentNullException(nameof(stabilityMonitor));
            _connectionOptimizer = connectionOptimizer ?? throw new ArgumentNullException(nameof(connectionOptimizer));
        }

        /// <summary>
        /// 安定性監視を開始
        /// </summary>
        public async Task StartStabilityMonitoringAsync()
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;
            _monitoringCts = new CancellationTokenSource();

            // 現在のネットワークを取得
            _currentNetwork = await _wifiManager.GetCurrentSSIDAsync();

            // 安定性チェックタイマーを開始
            _stabilityCheckTimer = new Timer(
                StabilityCheckCallback,
                null,
                TimeSpan.FromSeconds(StabilityCheckIntervalSeconds),
                TimeSpan.FromSeconds(StabilityCheckIntervalSeconds));

            // 自動再接続タイマーを開始
            _autoReconnectTimer = new Timer(
                AutoReconnectCallback,
                null,
                TimeSpan.FromSeconds(AutoReconnectIntervalSeconds),
                TimeSpan.FromSeconds(AutoReconnectIntervalSeconds));

            await _logger.LogInformation("WiFi安定性監視を開始しました", new Dictionary<string, object>
            {
                ["currentNetwork"] = _currentNetwork ?? "unknown"
            });
        }

        /// <summary>
        /// 安定性監視を停止
        /// </summary>
        public async Task StopStabilityMonitoringAsync()
        {
            if (!_isMonitoring)
                return;

            _isMonitoring = false;

            _monitoringCts?.Cancel();
            _stabilityCheckTimer?.Dispose();
            _autoReconnectTimer?.Dispose();

            await _logger.LogInformation("WiFi安定性監視を停止しました");
        }

        /// <summary>
        /// 接続を強制的にテストして安定性を確認
        /// </summary>
        public async Task<ConnectionStabilityReport> TestConnectionStabilityAsync()
        {
            try
            {
                var currentNetwork = await _wifiManager.GetCurrentSSIDAsync();
                if (string.IsNullOrEmpty(currentNetwork))
                {
                    return new ConnectionStabilityReport
                    {
                        IsStable = false,
                        Reason = "ネットワークに接続されていません",
                        Recommendation = "ネットワークに接続してください"
                    };
                }

                // ネットワークの現在の状態を取得
                var networks = await _wifiManager.ScanNetworksAsync();
                var currentNetworkInfo = networks.FirstOrDefault(n => n.Ssid == currentNetwork);

                if (currentNetworkInfo == null)
                {
                    return new ConnectionStabilityReport
                    {
                        IsStable = false,
                        Reason = "現在のネットワークが見つかりません",
                        Recommendation = "ネットワークスキャンを実行してください"
                    };
                }

                // シグナル強度チェック
                if (currentNetworkInfo.SignalStrength < 30)
                {
                    return new ConnectionStabilityReport
                    {
                        IsStable = false,
                        Reason = "シグナル強度が弱いです",
                        SignalStrength = currentNetworkInfo.SignalStrength,
                        Recommendation = "アクセスポイントに近づくか、より良い場所に移動してください"
                    };
                }

                // 安定性メトリクスを取得
                var metrics = _stabilityMonitor.GetCurrentMetrics();
                if (metrics != null)
                {
                    // パケット損失率チェック
                    if (metrics.PacketLossRate > 5.0)
                    {
                        return new ConnectionStabilityReport
                        {
                            IsStable = false,
                            Reason = "パケット損失率が高いです",
                            PacketLossRate = metrics.PacketLossRate,
                            Recommendation = "ネットワークの干渉を減らすか、ルーターを再起動してください"
                        };
                    }

                    // レイテンシチェック
                    if (metrics.AverageLatency > 200.0)
                    {
                        return new ConnectionStabilityReport
                        {
                            IsStable = false,
                            Reason = "レイテンシが高すぎます",
                            AverageLatency = metrics.AverageLatency,
                            Recommendation = "ネットワーク負荷を減らすか、より速いネットワークに切り替えてください"
                        };
                    }
                }

                return new ConnectionStabilityReport
                {
                    IsStable = true,
                    Reason = "接続が安定しています",
                    SignalStrength = currentNetworkInfo.SignalStrength,
                    Recommendation = "現在のネットワークを維持してください"
                };
            }
            catch (Exception ex)
            {
                await _logger.LogError("接続安定性テスト中にエラーが発生しました", ex);

                return new ConnectionStabilityReport
                {
                    IsStable = false,
                    Reason = $"テスト中にエラーが発生しました: {ex.Message}",
                    Recommendation = "ネットワーク設定を確認してください"
                };
            }
        }

        /// <summary>
        /// より良いネットワークへの自動切り替えを試行
        /// </summary>
        public async Task<NetworkSwitchResult> AttemptSmartNetworkSwitchAsync()
        {
            try
            {
                // 現在のネットワークの安定性をチェック
                var stabilityReport = await TestConnectionStabilityAsync();
                if (stabilityReport.IsStable)
                {
                    return new NetworkSwitchResult
                    {
                        Switched = false,
                        Reason = "現在のネットワークが安定しています"
                    };
                }

                // 利用可能なネットワークを取得
                var availableNetworks = await _wifiManager.ScanNetworksAsync();
                availableNetworks = availableNetworks
                    .Where(n => !string.IsNullOrEmpty(n.Ssid) && n.Ssid != _currentNetwork)
                    .ToList();

                if (!availableNetworks.Any())
                {
                    return new NetworkSwitchResult
                    {
                        Switched = false,
                        Reason = "代替ネットワークが見つかりません"
                    };
                }

                // 最適なネットワークを選択
                var optimizedChoice = await _connectionOptimizer.SelectOptimalNetworkAsync(availableNetworks);

                if (optimizedChoice.SelectedNetwork == null)
                {
                    return new NetworkSwitchResult
                    {
                        Switched = false,
                        Reason = "最適な代替ネットワークが見つかりません"
                    };
                }

                // ネットワーク切り替えを試行
                var switchAttempt = await AttemptNetworkSwitchAsync(optimizedChoice.SelectedNetwork);

                return new NetworkSwitchResult
                {
                    Switched = switchAttempt,
                    NewNetwork = switchAttempt ? optimizedChoice.SelectedNetwork.Ssid : null,
                    Reason = switchAttempt ? "より良いネットワークに切り替えました" : "ネットワーク切り替えに失敗しました",
                    QualityScore = optimizedChoice.QualityScore
                };
            }
            catch (Exception ex)
            {
                await _logger.LogError("スマートネットワーク切り替え中にエラーが発生しました", ex);

                return new NetworkSwitchResult
                {
                    Switched = false,
                    Reason = $"切り替え中にエラーが発生しました: {ex.Message}"
                };
            }
        }

        private async Task<bool> AttemptNetworkSwitchAsync(WifiNetwork targetNetwork)
        {
            try
            {
                // 現在の接続を切断
                await _wifiManager.DisconnectAsync();

                // ターゲットネットワークに接続
                // 注意: パスワードが必要な場合は適切に処理する
                var connected = await _wifiManager.ConnectAsync(targetNetwork.Ssid, ""); // パスワードは別途取得が必要

                if (connected)
                {
                    _currentNetwork = targetNetwork.Ssid;
                    _lastConnectionAttempt = DateTime.UtcNow;

                    await _logger.LogInformation("ネットワーク切り替えに成功しました", new Dictionary<string, object>
                    {
                        ["oldNetwork"] = _currentNetwork,
                        ["newNetwork"] = targetNetwork.Ssid
                    });
                }

                return connected;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"ネットワーク切り替えに失敗しました: {targetNetwork.Ssid}", ex);
                return false;
            }
        }

        private async void StabilityCheckCallback(object? state)
        {
            if (!_isMonitoring || _monitoringCts?.IsCancellationRequested == true)
                return;

            try
            {
                var stabilityReport = await TestConnectionStabilityAsync();

                if (!stabilityReport.IsStable && ShouldAttemptReconnect())
                {
                    await _logger.LogWarning("接続が不安定です。再接続を試行します", new Dictionary<string, object>
                    {
                        ["reason"] = stabilityReport.Reason ?? "不明",
                        ["recommendation"] = stabilityReport.Recommendation ?? "なし"
                    });

                    // 現在のネットワークに再接続を試行
                    if (_currentNetwork != null)
                    {
                        await _wifiManager.ConnectAsync(_currentNetwork, ""); // パスワードは別途取得が必要
                        _lastConnectionAttempt = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger.LogError("安定性チェック中にエラーが発生しました", ex);
            }
        }

        private async void AutoReconnectCallback(object? state)
        {
            if (!_isMonitoring || _monitoringCts?.IsCancellationRequested == true)
                return;

            try
            {
                var currentNetwork = await _wifiManager.GetCurrentSSIDAsync();

                // 接続が切れている場合のみ再接続を試行
                if (string.IsNullOrEmpty(currentNetwork) && _currentNetwork != null && ShouldAttemptReconnect())
                {
                    await _logger.LogInformation("ネットワーク接続が切れています。再接続を試行します", new Dictionary<string, object>
                    {
                        ["targetNetwork"] = _currentNetwork
                    });

                    await _wifiManager.ConnectAsync(_currentNetwork, ""); // パスワードは別途取得が必要
                    _lastConnectionAttempt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                await _logger.LogError("自動再接続中にエラーが発生しました", ex);
            }
        }

        private bool ShouldAttemptReconnect()
        {
            return (DateTime.UtcNow - _lastConnectionAttempt).TotalSeconds >= MinConnectionIntervalSeconds;
        }

        private async Task<string?> GetCurrentNetworkName()
        {
            try
            {
                return await _wifiManager.GetCurrentSSIDAsync();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 接続安定性レポート
    /// </summary>
    public class ConnectionStabilityReport
    {
        public bool IsStable { get; set; }
        public string? Reason { get; set; }
        public string? Recommendation { get; set; }
        public int? SignalStrength { get; set; }
        public double? PacketLossRate { get; set; }
        public double? AverageLatency { get; set; }
    }

    /// <summary>
    /// ネットワーク切り替え結果
    /// </summary>
    public class NetworkSwitchResult
    {
        public bool Switched { get; set; }
        public string? NewNetwork { get; set; }
        public string? Reason { get; set; }
        public NetworkQualityScore? QualityScore { get; set; }
    }
}
