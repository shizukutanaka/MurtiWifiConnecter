using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 高速WiFi状態チェックサービス - 軽量で実用的
    /// </summary>
    public static class QuickStatusChecker
    {
        /// <summary>
        /// WiFi接続状態の即座チェック
        /// </summary>
        public static async Task<ConnectionStatus> GetQuickStatusAsync()
        {
            var status = new ConnectionStatus
            {
                CheckTime = DateTime.Now
            };

            try
            {
                // 1. ネットワークインターフェース確認（高速）
                var networkIsUp = NetworkInterface.GetIsNetworkAvailable();
                status.NetworkAvailable = networkIsUp;

                if (!networkIsUp)
                {
                    status.StatusText = "ネットワーク無効";
                    status.StatusLevel = StatusLevel.Error;
                    return status;
                }

                // 2. WiFi SSID確認（中速）
                var currentSSID = await NetworkUtils.GetCurrentConnectedSSIDAsync();
                status.ConnectedSSID = currentSSID;

                if (string.IsNullOrEmpty(currentSSID))
                {
                    status.StatusText = "WiFi未接続";
                    status.StatusLevel = StatusLevel.Warning;
                    return status;
                }

                // 3. インターネット接続確認（低速だが重要）
                var hasInternet = await TestInternetConnectionInternalAsync();
                status.HasInternet = hasInternet;

                if (hasInternet)
                {
                    status.StatusText = $"接続中: {currentSSID}";
                    status.StatusLevel = StatusLevel.Good;
                }
                else
                {
                    status.StatusText = $"制限あり: {currentSSID}";
                    status.StatusLevel = StatusLevel.Warning;
                }

                return status;
            }
            catch (Exception ex)
            {
                status.StatusText = $"確認エラー: {ex.Message}";
                status.StatusLevel = StatusLevel.Error;
                SimpleLoggingService.LogError("Quick status check failed", ex);
                return status;
            }
        }

        /// <summary>
        /// 高速インターネット接続テスト（パブリック版）
        /// </summary>
        public static async Task<bool> TestInternetConnectionAsync()
        {
            return await TestInternetConnectionInternalAsync();
        }

        /// <summary>
        /// 高速インターネット接続テスト（内部版）
        /// </summary>
        private static async Task<bool> TestInternetConnectionInternalAsync()
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 接続品質の評価
        /// </summary>
        public static async Task<ConnectionQuality> AssessConnectionQualityAsync()
        {
            var quality = new ConnectionQuality
            {
                TestTime = DateTime.Now
            };

            try
            {
                // WiFi信号強度取得
                var networks = await NetworkUtils.ScanWifiNetworksAsync();
                var currentSSID = await NetworkUtils.GetCurrentConnectedSSIDAsync();

                if (!string.IsNullOrEmpty(currentSSID) && networks.ContainsKey(currentSSID))
                {
                    quality.SignalStrength = networks[currentSSID];
                }

                // レイテンシテスト
                using var ping = new Ping();
                var pingResults = new List<long>();

                for (int i = 0; i < 3; i++)
                {
                    var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        pingResults.Add(reply.RoundtripTime);
                    }
                }

                if (pingResults.Count > 0)
                {
                    quality.AverageLatency = pingResults.Sum() / pingResults.Count;
                    quality.HasValidData = true;

                    // 品質評価
                    quality.OverallScore = CalculateQualityScore(quality.SignalStrength, quality.AverageLatency);
                }

                return quality;
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Connection quality assessment failed", ex);
                return quality;
            }
        }

        private static int CalculateQualityScore(int signalStrength, long latency)
        {
            var signalScore = Math.Max(0, signalStrength); // 0-100
            var latencyScore = Math.Max(0, 100 - (latency / 2)); // 200ms = 0点, 0ms = 100点

            return (int)((signalScore * 0.6) + (latencyScore * 0.4));
        }

        /// <summary>
        /// システムの推奨アクション
        /// </summary>
        public static string GetRecommendedAction(ConnectionStatus status, ConnectionQuality quality = null)
        {
            if (!status.NetworkAvailable)
                return "WiFiアダプターを確認してください";

            if (string.IsNullOrEmpty(status.ConnectedSSID))
                return "利用可能なWiFiネットワークに接続してください";

            if (!status.HasInternet)
                return "ルーターの再起動またはネットワーク設定を確認してください";

            if (quality != null && quality.HasValidData)
            {
                if (quality.OverallScore < 30)
                    return "信号が弱いため、ルーターに近づくか他のネットワークを試してください";
                
                if (quality.AverageLatency > 200)
                    return "レイテンシが高いため、他のネットワークへの切り替えを検討してください";
            }

            return "接続は良好です";
        }
    }

    public class ConnectionStatus
    {
        public DateTime CheckTime { get; set; }
        public bool NetworkAvailable { get; set; }
        public string ConnectedSSID { get; set; }
        public bool HasInternet { get; set; }
        public string StatusText { get; set; }
        public StatusLevel StatusLevel { get; set; }
    }

    public class ConnectionQuality
    {
        public DateTime TestTime { get; set; }
        public int SignalStrength { get; set; }
        public long AverageLatency { get; set; }
        public int OverallScore { get; set; }
        public bool HasValidData { get; set; }
    }

    public enum StatusLevel
    {
        Good,
        Warning, 
        Error
    }
}