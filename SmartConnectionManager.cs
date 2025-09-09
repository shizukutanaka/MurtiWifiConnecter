using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    // スマートWiFi接続管理（実用的な自動切り替え・最適化）
    public class SmartConnectionManager : IDisposable
    {
        private readonly ConnectionHistory _history;
        private readonly ConnectionStatistics _stats;
        private readonly ConnectionLogger _logger;
        private readonly Timer _optimizationTimer;
        private bool _disposed;
        private volatile bool _isOptimizing;
        
        public bool AutoSwitchEnabled { get; set; } = true;
        public int SignalThresholdForSwitch { get; set; } = 20; // 信号差がこれ以上あると切り替え検討
        public TimeSpan OptimizationInterval { get; set; } = TimeSpan.FromMinutes(5);
        
        public event EventHandler<ConnectionSwitchEventArgs>? ConnectionSwitchRecommended;
        public event EventHandler<ConnectionSwitchEventArgs>? ConnectionSwitched;
        
        public SmartConnectionManager(ConnectionHistory history, ConnectionStatistics stats, ConnectionLogger logger)
        {
            _history = history;
            _stats = stats;
            _logger = logger;
            
            // 5分間隔で最適化チェック
            _optimizationTimer = new Timer(PerformOptimization, null, OptimizationInterval, OptimizationInterval);
        }
        
        private async void PerformOptimization(object? state)
        {
            if (_disposed || _isOptimizing) return;
            
            _isOptimizing = true;
            try
            {
                await OptimizeConnectionAsync();
            }
            finally
            {
                _isOptimizing = false;
            }
        }
        
        public async Task<ConnectionRecommendation?> AnalyzeAndRecommendSwitchAsync()
        {
            try
            {
                // 現在の接続状況を取得
                var currentSSID = await FastWifiConnector.GetCurrentConnectedSSIDAsync();
                if (string.IsNullOrEmpty(currentSSID))
                    return null;
                
                var currentHealth = await _healthChecker.PerformHealthCheckAsync();
                
                // 履歴のあるネットワークから候補を検索
                var historicalNetworks = _history.GetRecentNetworks(10);
                var availableNetworks = await NetworkUtils.ScanWifiNetworksAsync();
                
                var candidates = new List<SwitchCandidate>();
                
                foreach (var historicalSSID in historicalNetworks)
                {
                    if (historicalSSID.Equals(currentSSID, StringComparison.OrdinalIgnoreCase))
                        continue;
                        
                    if (availableNetworks.TryGetValue(historicalSSID, out var signal))
                    {
                        var historyEntry = _history.GetEntry(historicalSSID);
                        var quality = _stats.AnalyzeNetworkQuality(historicalSSID, signal, true, historyEntry?.ConnectionCount ?? 0);
                        
                        candidates.Add(new SwitchCandidate
                        {
                            SSID = historicalSSID,
                            SignalStrength = signal,
                            SuccessRate = historyEntry?.GetSuccessRate() ?? 0.5,
                            QualityScore = quality.Score,
                            LastConnected = historyEntry?.LastConnectionTime ?? DateTime.MinValue
                        });
                    }
                }
                
                // 最適な候補を選択
                var bestCandidate = SelectBestCandidate(candidates, currentHealth);
                if (bestCandidate == null)
                    return null;
                
                // 切り替えを推奨するかどうか判定
                var shouldRecommend = ShouldRecommendSwitch(currentHealth, bestCandidate);
                if (!shouldRecommend)
                    return null;
                
                return new ConnectionRecommendation
                {
                    CurrentSSID = currentSSID,
                    RecommendedSSID = bestCandidate.SSID,
                    Reason = GenerateSwitchReason(currentHealth, bestCandidate),
                    Priority = CalculatePriority(currentHealth, bestCandidate),
                    EstimatedBenefit = CalculateEstimatedBenefit(currentHealth, bestCandidate)
                };
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SmartConnectionManager.AnalyzeSwitch", ex, _logger);
                return null;
            }
        }
        
        public async Task<bool> ExecuteSmartSwitchAsync(string targetSSID, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.Log(ConnectionLogger.LogLevel.Info, "SmartConnection", 
                    $"スマート切り替え実行: {targetSSID}");
                
                // まず現在の接続を切断
                var disconnectResult = await FastWifiConnector.DisconnectAsync(cancellationToken);
                if (!disconnectResult)
                {
                    _logger.Log(ConnectionLogger.LogLevel.Warning, "SmartConnection", "切断に失敗");
                    return false;
                }
                
                // 短い待機後に新しいネットワークに接続
                await Task.Delay(QuickSettingsManager.Constants.ConnectionDelayMs, cancellationToken);
                
                // 履歴からパスワードを取得
                var historyEntry = _history.GetEntry(targetSSID);
                var password = historyEntry?.LastUsedPassword ?? "";
                
                var connectResult = await FastWifiConnector.ConnectAsync(targetSSID, password, cancellationToken);
                
                if (connectResult.Success)
                {
                    _history.AddSuccessfulConnection(targetSSID);
                    _logger.Log(ConnectionLogger.LogLevel.Info, "SmartConnection", 
                        $"スマート切り替え成功: {targetSSID}");
                    
                    ConnectionSwitched?.Invoke(this, new ConnectionSwitchEventArgs
                    {
                        Recommendation = new ConnectionRecommendation { RecommendedSSID = targetSSID }
                    });
                    
                    return true;
                }
                else
                {
                    _logger.Log(ConnectionLogger.LogLevel.Warning, "SmartConnection", 
                        $"スマート切り替え失敗: {targetSSID} - {connectResult.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SmartConnectionManager.ExecuteSmartSwitch", ex, _logger);
                return false;
            }
        }
        
        private async Task OptimizeConnectionAsync()
        {
            try
            {
                var currentHealth = await _healthChecker.PerformHealthCheckAsync();
                
                // 接続品質が一定レベル以下の場合のみ最適化を検討
                if (currentHealth.Quality >= ConnectionQuality.Good)
                    return;
                
                var recommendation = await AnalyzeAndRecommendSwitchAsync();
                if (recommendation != null && recommendation.Priority >= SwitchPriority.Medium)
                {
                    _logger.Log(ConnectionLogger.LogLevel.Info, "SmartConnection", 
                        $"自動最適化を実行: {recommendation.RecommendedSSID}");
                    
                    await ExecuteSmartSwitchAsync(recommendation.RecommendedSSID);
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SmartConnectionManager.OptimizeConnection", ex, _logger);
            }
        }
        
        private static SwitchCandidate? SelectBestCandidate(List<SwitchCandidate> candidates, ConnectionHealthStatus currentHealth)
        {
            if (!candidates.Any()) return null;
            
            // スコアリング: 信号強度 + 成功率 + 品質 - 古さ
            return candidates
                .Select(c => new
                {
                    Candidate = c,
                    Score = (c.SignalStrength * 0.4) + 
                           (c.SuccessRate * 100 * 0.3) + 
                           (c.QualityScore * 0.2) + 
                           (GetRecencyScore(c.LastConnected) * 0.1)
                })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault()?.Candidate;
        }
        
        private static double GetRecencyScore(DateTime lastConnected)
        {
            var daysSince = (DateTime.Now - lastConnected).TotalDays;
            return daysSince switch
            {
                < 1 => 100,
                < 7 => 80,
                < 30 => 60,
                _ => 40
            };
        }
        
        private bool ShouldRecommendSwitch(ConnectionHealthStatus currentHealth, SwitchCandidate candidate)
        {
            // 現在の品質が良好以上なら切り替えしない
            if (currentHealth.Quality >= ConnectionQuality.Good)
                return false;
            
            // 候補の信号強度が現在より大幅に良い場合
            if (candidate.SignalStrength - currentHealth.SignalStrength >= SignalThresholdForSwitch)
                return true;
            
            // 現在の接続が不良で、候補が成功率が高い場合
            if (currentHealth.Quality <= ConnectionQuality.Poor && candidate.SuccessRate >= 0.8)
                return true;
            
            return false;
        }
        
        private static string GenerateSwitchReason(ConnectionHealthStatus currentHealth, SwitchCandidate candidate)
        {
            var reasons = new List<string>();
            
            if (candidate.SignalStrength > currentHealth.SignalStrength + 20)
                reasons.Add("より強い信号");
            
            if (candidate.SuccessRate >= 0.9)
                reasons.Add("高い接続成功率");
            
            if (currentHealth.Quality <= ConnectionQuality.Poor)
                reasons.Add("現在の接続品質が不良");
            
            return reasons.Any() ? string.Join(", ", reasons) : "総合的な品質改善";
        }
        
        private static SwitchPriority CalculatePriority(ConnectionHealthStatus currentHealth, SwitchCandidate candidate)
        {
            var signalImprovement = candidate.SignalStrength - currentHealth.SignalStrength;
            
            if (currentHealth.Quality <= ConnectionQuality.Poor && signalImprovement >= 30)
                return SwitchPriority.High;
            
            if (currentHealth.Quality <= ConnectionQuality.Fair && signalImprovement >= 20)
                return SwitchPriority.Medium;
            
            return SwitchPriority.Low;
        }
        
        private static int CalculateEstimatedBenefit(ConnectionHealthStatus currentHealth, SwitchCandidate candidate)
        {
            var benefit = 0;
            
            // 信号強度改善による利益
            var signalImprovement = candidate.SignalStrength - currentHealth.SignalStrength;
            benefit += signalImprovement;
            
            // 成功率による利益
            if (candidate.SuccessRate >= 0.9) benefit += 20;
            else if (candidate.SuccessRate >= 0.8) benefit += 10;
            
            // 品質スコアによる利益
            benefit += (int)(candidate.QualityScore * 0.3);
            
            return Math.Max(0, benefit);
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _optimizationTimer?.Dispose();
                _healthChecker?.Dispose();
            }
        }
    }
    
    public class SwitchCandidate
    {
        public string SSID { get; set; } = string.Empty;
        public int SignalStrength { get; set; }
        public double SuccessRate { get; set; }
        public double QualityScore { get; set; }
        public DateTime LastConnected { get; set; }
    }
    
    public class ConnectionRecommendation
    {
        public string CurrentSSID { get; set; } = string.Empty;
        public string RecommendedSSID { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public SwitchPriority Priority { get; set; }
        public int EstimatedBenefit { get; set; }
        
        public string GetRecommendationText()
        {
            return $"{RecommendedSSID}への切り替えを推奨 (理由: {Reason}, 改善度: {EstimatedBenefit}%)";
        }
    }
    
    public class ConnectionSwitchEventArgs : EventArgs
    {
        public ConnectionRecommendation Recommendation { get; set; } = new();
    }
    
    public enum SwitchPriority
    {
        Low,
        Medium,
        High
    }
}