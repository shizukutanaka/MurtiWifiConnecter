using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// リアルタイムWiFi分析機能を提供するクラス
    /// 電波強度、チャンネル干渉、周囲ネットワークの監視
    /// </summary>
    public class WifiAnalyzer : IDisposable
    {
        private readonly System.Timers.Timer _analysisTimer;
        private readonly List<NetworkSignalData> _signalHistory = new();
        private readonly object _lockObject = new();
        private bool _isRunning;

        public event EventHandler<NetworkAnalysisEventArgs>? AnalysisUpdated;
        public event EventHandler<ChannelInterferenceEventArgs>? InterferenceDetected;

        // 設定
        private const int AnalysisIntervalMs = 5000; // 5秒間隔
        private const int HistoryRetentionMinutes = 60; // 1時間保持
        private const double InterferenceThreshold = -60; // dBm（信号強度がこれより弱いと干渉検知）

        public WifiAnalyzer()
        {
            _analysisTimer = new System.Timers.Timer(AnalysisIntervalMs);
            _analysisTimer.Elapsed += async (s, e) => await PerformAnalysisAsync();
        }

        /// <summary>
        /// 分析を開始する
        /// </summary>
        public void StartAnalysis()
        {
            if (_isRunning) return;

            _isRunning = true;
            _analysisTimer.Start();

            Logger.LogInfo("WiFi分析を開始しました", "WifiAnalyzer", new Dictionary<string, object>
            {
                ["interval"] = AnalysisIntervalMs,
                ["retention"] = HistoryRetentionMinutes
            });
        }

        /// <summary>
        /// 分析を停止する
        /// </summary>
        public void StopAnalysis()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _analysisTimer.Stop();

            Logger.LogInfo("WiFi分析を停止しました", "WifiAnalyzer");
        }

        /// <summary>
        /// 現在のネットワーク分析を実行する
        /// </summary>
        private async Task PerformAnalysisAsync()
        {
            try
            {
                var currentNetworks = await GetCurrentNetworksAsync();
                var analysis = AnalyzeNetworks(currentNetworks);

                lock (_lockObject)
                {
                    // 履歴に追加
                    _signalHistory.AddRange(analysis.NetworkData);

                    // 古いデータを削除
                    var cutoffTime = DateTime.Now.AddMinutes(-HistoryRetentionMinutes);
                    _signalHistory.RemoveAll(data => data.Timestamp < cutoffTime);
                }

                // イベントを発行
                AnalysisUpdated?.Invoke(this, new NetworkAnalysisEventArgs
                {
                    Analysis = analysis,
                    Timestamp = DateTime.Now
                });

                // 干渉検知
                if (analysis.InterferenceLevel > InterferenceThreshold)
                {
                    InterferenceDetected?.Invoke(this, new ChannelInterferenceEventArgs
                    {
                        InterferenceLevel = analysis.InterferenceLevel,
                        AffectedChannels = analysis.AffectedChannels,
                        Recommendation = GenerateRecommendation(analysis)
                    });
                }
            }
            catch (Exception ex)
            {
                await Logger.LogError(ex, "WifiAnalyzer", "分析実行中にエラーが発生しました");
            }
        }

        /// <summary>
        /// 現在のネットワーク情報を取得する
        /// </summary>
        private async Task<List<NetworkInfo>> GetCurrentNetworksAsync()
        {
            // 実際の実装ではnetshコマンドやWindows APIを使用
            var networks = new List<NetworkInfo>();

            try
            {
                using var process = new System.Diagnostics.Process();
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show networks mode=bssid",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();

                // 出力のパース（簡易版）
                var lines = output.Split('\n');
                NetworkInfo? currentNetwork = null;

                foreach (var line in lines)
                {
                    if (line.Contains("SSID"))
                    {
                        if (currentNetwork != null)
                            networks.Add(currentNetwork);

                        currentNetwork = new NetworkInfo
                        {
                            Ssid = line.Split(':').Last().Trim(),
                            Timestamp = DateTime.Now
                        };
                    }
                    else if (line.Contains("Signal") && currentNetwork != null)
                    {
                        var signalStr = line.Split(':').Last().Trim().Replace("%", "");
                        if (int.TryParse(signalStr, out var signal))
                        {
                            currentNetwork.SignalStrength = signal;
                        }
                    }
                    else if (line.Contains("Channel") && currentNetwork != null)
                    {
                        var channelStr = line.Split(':').Last().Trim();
                        if (int.TryParse(channelStr, out var channel))
                        {
                            currentNetwork.Channel = channel;
                        }
                    }
                }

                if (currentNetwork != null)
                    networks.Add(currentNetwork);
            }
            catch (Exception ex)
            {
                await Logger.LogError(ex, "WifiAnalyzer", "ネットワーク情報の取得に失敗しました");
            }

            return networks;
        }

        /// <summary>
        /// ネットワーク情報を分析する
        /// </summary>
        private NetworkAnalysisResult AnalyzeNetworks(List<NetworkInfo> networks)
        {
            var result = new NetworkAnalysisResult();

            if (!networks.Any())
                return result;

            // 信号強度の分析
            result.NetworkData = networks.Select(n => new NetworkSignalData
            {
                Ssid = n.Ssid,
                SignalStrength = n.SignalStrength,
                Channel = n.Channel,
                Timestamp = n.Timestamp
            }).ToList();

            // 平均信号強度
            result.AverageSignalStrength = result.NetworkData.Average(n => n.SignalStrength);

            // チャンネル干渉の分析
            var channelGroups = result.NetworkData.GroupBy(n => n.Channel);
            result.AffectedChannels = channelGroups
                .Where(g => g.Count() > 1)
                .Select(g => new AffectedChannelInfo
                {
                    Channel = g.Key,
                    NetworkCount = g.Count(),
                    AverageSignal = g.Average(n => n.SignalStrength)
                }).ToList();

            // 干渉レベルの計算（簡易版）
            result.InterferenceLevel = result.AffectedChannels.Any() ?
                result.AffectedChannels.Max(c => c.AverageSignal) : 0;

            return result;
        }

        /// <summary>
        /// 改善提案を生成する
        /// </summary>
        private string GenerateRecommendation(NetworkAnalysisResult analysis)
        {
            var recommendations = new List<string>();

            if (analysis.AverageSignalStrength < 50)
            {
                recommendations.Add("信号強度が低いネットワークが検出されました。アクセスポイントに近づくことを検討してください。");
            }

            if (analysis.AffectedChannels.Count > 2)
            {
                recommendations.Add("チャンネル干渉が検出されました。アクセスポイントのチャンネルを変更することを検討してください。");
            }

            if (analysis.NetworkData.Any(n => n.SignalStrength < 20))
            {
                recommendations.Add("非常に弱い信号のネットワークが検出されました。接続を避けることを検討してください。");
            }

            return recommendations.Any() ?
                string.Join(" ", recommendations) :
                "ネットワーク状況は良好です。";
        }

        /// <summary>
        /// 信号強度履歴を取得する
        /// </summary>
        public List<NetworkSignalData> GetSignalHistory(string ssid = null)
        {
            lock (_lockObject)
            {
                return ssid == null ?
                    _signalHistory.ToList() :
                    _signalHistory.Where(h => h.Ssid == ssid).ToList();
            }
        }

        public void Dispose()
        {
            StopAnalysis();
            _analysisTimer.Dispose();
        }
    }

    // データ構造定義
    public class NetworkInfo
    {
        public string Ssid { get; set; } = "";
        public int SignalStrength { get; set; }
        public int Channel { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class NetworkSignalData
    {
        public string Ssid { get; set; } = "";
        public int SignalStrength { get; set; }
        public int Channel { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class NetworkAnalysisResult
    {
        public List<NetworkSignalData> NetworkData { get; set; } = new();
        public double AverageSignalStrength { get; set; }
        public List<AffectedChannelInfo> AffectedChannels { get; set; } = new();
        public double InterferenceLevel { get; set; }
    }

    public class AffectedChannelInfo
    {
        public int Channel { get; set; }
        public int NetworkCount { get; set; }
        public double AverageSignal { get; set; }
    }

    public class NetworkAnalysisEventArgs : EventArgs
    {
        public NetworkAnalysisResult Analysis { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class ChannelInterferenceEventArgs : EventArgs
    {
        public double InterferenceLevel { get; set; }
        public List<AffectedChannelInfo> AffectedChannels { get; set; } = new();
        public string Recommendation { get; set; } = "";
    }
}
