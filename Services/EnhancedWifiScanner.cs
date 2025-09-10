using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 強化WiFiスキャナー - 実用的で詳細な情報を提供
    /// </summary>
    public class EnhancedWifiScanner : IDisposable
    {
        private readonly SemaphoreSlim _scanLock = new(1, 1);
        private readonly Dictionary<string, WifiNetworkInfo> _networkCache = new();
        private readonly NetworkPriorityManager _priorityManager;
        private DateTime _lastScan = DateTime.MinValue;
        private bool _disposed = false;

        public EnhancedWifiScanner(NetworkPriorityManager priorityManager = null)
        {
            _priorityManager = priorityManager ?? new NetworkPriorityManager();
        }

        /// <summary>
        /// 詳細なWiFiネットワークスキャン
        /// </summary>
        public async Task<List<WifiNetworkInfo>> ScanWithDetailsAsync(CancellationToken cancellationToken = default)
        {
            await _scanLock.WaitAsync(cancellationToken);
            try
            {
                SimpleLoggingService.LogInfo("Starting enhanced WiFi scan...");
                var startTime = DateTime.Now;

                // 基本スキャン実行
                var basicNetworks = await NetworkUtils.ScanWifiNetworksAsync(cancellationToken);
                var currentSSID = await NetworkUtils.GetCurrentConnectedSSIDAsync(cancellationToken);
                
                var detailedNetworks = new List<WifiNetworkInfo>();

                foreach (var network in basicNetworks)
                {
                    var networkInfo = await EnhanceNetworkInfoAsync(network.Key, network.Value, currentSSID);
                    detailedNetworks.Add(networkInfo);
                    
                    // キャッシュ更新
                    _networkCache[network.Key] = networkInfo;
                }

                // 優先度でソート
                detailedNetworks = detailedNetworks
                    .OrderByDescending(n => n.IsConnected)
                    .ThenByDescending(n => n.Priority)
                    .ThenByDescending(n => n.SignalStrength)
                    .ThenBy(n => n.SSID)
                    .ToList();

                var duration = DateTime.Now - startTime;
                SimpleLoggingService.LogInfo($"Enhanced scan completed: {detailedNetworks.Count} networks in {duration.TotalMilliseconds:F0}ms");

                _lastScan = DateTime.Now;
                return detailedNetworks;
            }
            finally
            {
                _scanLock.Release();
            }
        }

        /// <summary>
        /// キャッシュされたネットワーク情報を取得（高速）
        /// </summary>
        public List<WifiNetworkInfo> GetCachedNetworks()
        {
            var networks = _networkCache.Values.ToList();
            
            // 古いデータの場合は警告
            if ((DateTime.Now - _lastScan).TotalMinutes > 5)
            {
                foreach (var network in networks)
                {
                    network.IsStale = true;
                }
            }

            return networks
                .OrderByDescending(n => n.IsConnected)
                .ThenByDescending(n => n.Priority)
                .ThenByDescending(n => n.SignalStrength)
                .ToList();
        }

        /// <summary>
        /// 特定ネットワークの詳細情報を取得
        /// </summary>
        public async Task<WifiNetworkInfo> GetNetworkDetailsAsync(string ssid, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return null;

            // キャッシュチェック
            if (_networkCache.TryGetValue(ssid, out var cached) && 
                (DateTime.Now - cached.LastUpdated).TotalMinutes < 2)
            {
                return cached;
            }

            // 新しい情報を取得
            var networks = await NetworkUtils.ScanWifiNetworksAsync(cancellationToken);
            if (networks.TryGetValue(ssid, out var signalStrength))
            {
                var currentSSID = await NetworkUtils.GetCurrentConnectedSSIDAsync(cancellationToken);
                var networkInfo = await EnhanceNetworkInfoAsync(ssid, signalStrength, currentSSID);
                _networkCache[ssid] = networkInfo;
                return networkInfo;
            }

            return null;
        }

        /// <summary>
        /// 接続推奨ネットワークを取得
        /// </summary>
        public async Task<WifiNetworkInfo> GetRecommendedNetworkAsync(CancellationToken cancellationToken = default)
        {
            var networks = await ScanWithDetailsAsync(cancellationToken);
            
            var available = networks
                .Where(n => !n.IsConnected && n.SignalStrength > 20)
                .ToList();

            if (!available.Any())
                return null;

            // 優先度とシグナル強度の組み合わせで最適選択
            return available
                .OrderByDescending(n => n.Priority > 0 ? n.Priority * 100 + n.SignalStrength : n.SignalStrength)
                .First();
        }

        /// <summary>
        /// ネットワーク情報の拡張
        /// </summary>
        private async Task<WifiNetworkInfo> EnhanceNetworkInfoAsync(string ssid, int signalStrength, string currentSSID)
        {
            var info = new WifiNetworkInfo
            {
                SSID = ssid,
                SignalStrength = signalStrength,
                IsConnected = string.Equals(ssid, currentSSID, StringComparison.OrdinalIgnoreCase),
                LastUpdated = DateTime.Now
            };

            // 優先度取得
            try
            {
                info.Priority = await _priorityManager.GetPriorityAsync(ssid);
            }
            catch
            {
                info.Priority = 0;
            }

            // セキュリティレベル推定（基本的な推定）
            info.SecurityLevel = EstimateSecurityLevel(ssid);

            // 品質評価
            info.Quality = CalculateNetworkQuality(info);

            // 推奨度計算
            info.Recommendation = CalculateRecommendation(info);

            return info;
        }

        private SecurityLevel EstimateSecurityLevel(string ssid)
        {
            var lower = ssid.ToLowerInvariant();
            
            if (lower.Contains("open") || lower.Contains("free") || lower.Contains("guest"))
                return SecurityLevel.None;
            
            if (lower.Contains("wpa3"))
                return SecurityLevel.High;
            
            if (lower.Contains("wpa2") || lower.Contains("secure"))
                return SecurityLevel.Medium;
            
            if (lower.Contains("wpa"))
                return SecurityLevel.Low;

            return SecurityLevel.Unknown;
        }

        private int CalculateNetworkQuality(WifiNetworkInfo info)
        {
            var score = info.SignalStrength; // ベース: シグナル強度 (0-100)

            // セキュリティボーナス
            switch (info.SecurityLevel)
            {
                case SecurityLevel.High: score += 10; break;
                case SecurityLevel.Medium: score += 5; break;
                case SecurityLevel.None: score -= 15; break;
            }

            // 優先度ボーナス
            if (info.Priority > 0)
                score += Math.Min(20, info.Priority / 5);

            // 接続中ボーナス
            if (info.IsConnected)
                score += 25;

            return Math.Max(0, Math.Min(100, score));
        }

        private string CalculateRecommendation(WifiNetworkInfo info)
        {
            if (info.IsConnected)
                return "接続中";

            var score = info.Quality;

            if (score >= 80)
                return "強く推奨";
            else if (score >= 60)
                return "推奨";
            else if (score >= 40)
                return "使用可能";
            else if (score >= 20)
                return "信号弱い";
            else
                return "非推奨";
        }

        /// <summary>
        /// スキャン統計の取得
        /// </summary>
        public ScanStatistics GetScanStatistics()
        {
            return new ScanStatistics
            {
                LastScanTime = _lastScan,
                CachedNetworkCount = _networkCache.Count,
                IsCacheStale = (DateTime.Now - _lastScan).TotalMinutes > 5
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _scanLock?.Dispose();
            _priorityManager?.Dispose();
        }
    }

    public class WifiNetworkInfo
    {
        public string SSID { get; set; }
        public int SignalStrength { get; set; }
        public bool IsConnected { get; set; }
        public int Priority { get; set; }
        public SecurityLevel SecurityLevel { get; set; }
        public int Quality { get; set; }
        public string Recommendation { get; set; }
        public DateTime LastUpdated { get; set; }
        public bool IsStale { get; set; }

        public string GetSignalDescription()
        {
            return SignalStrength switch
            {
                >= 80 => "優秀",
                >= 60 => "良好", 
                >= 40 => "普通",
                >= 20 => "弱い",
                _ => "非常に弱い"
            };
        }

        public string GetQualityDescription()
        {
            return Quality switch
            {
                >= 80 => "優秀",
                >= 60 => "良好",
                >= 40 => "普通", 
                >= 20 => "低品質",
                _ => "不良"
            };
        }
    }

    public class ScanStatistics
    {
        public DateTime LastScanTime { get; set; }
        public int CachedNetworkCount { get; set; }
        public bool IsCacheStale { get; set; }
    }
}