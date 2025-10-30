using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 拡張された速度テスト機能を提供するクラス
    /// 統計情報、履歴保存、自動テスト機能を追加
    /// </summary>
    public class EnhancedSpeedTest : IDisposable
    {
        private readonly SpeedTest _speedTest;
        private readonly List<SpeedTestHistoryEntry> _history = new();
        private readonly object _lockObject = new();
        private System.Timers.Timer? _autoTestTimer;

        public event EventHandler<SpeedTestCompletedEventArgs>? SpeedTestCompleted;
        public event EventHandler<SpeedTestHistoryEventArgs>? HistoryUpdated;

        // 設定
        private const int MaxHistoryEntries = 1000;
        private const int AutoTestIntervalMinutes = 15;

        public EnhancedSpeedTest()
        {
            _speedTest = new SpeedTest();
        }

        /// <summary>
        /// 速度テストを実行し、履歴に保存する
        /// </summary>
        public async Task<SpeedTestResult> PerformSpeedTestAsync(CancellationToken ct = default)
        {
            var result = await _speedTest.TestDownloadSpeedAsync(ct);

            // 履歴に追加
            var historyEntry = new SpeedTestHistoryEntry
            {
                Timestamp = DateTime.Now,
                DownloadSpeed = result.DownloadSpeed,
                UploadSpeed = result.UploadSpeed,
                Success = result.Success,
                Message = result.Message
            };

            lock (_lockObject)
            {
                _history.Add(historyEntry);

                // 履歴を制限内に収める
                if (_history.Count > MaxHistoryEntries)
                {
                    _history.RemoveRange(0, _history.Count - MaxHistoryEntries);
                }
            }

            // 統計を計算
            var stats = CalculateStatistics();

            // イベントを発行
            SpeedTestCompleted?.Invoke(this, new SpeedTestCompletedEventArgs
            {
                Result = result,
                Statistics = stats,
                Timestamp = DateTime.Now
            });

            HistoryUpdated?.Invoke(this, new SpeedTestHistoryEventArgs
            {
                NewEntry = historyEntry,
                CurrentStatistics = stats
            });

            // ログに記録
            await Logger.LogInfo($"速度テスト完了: {result.DownloadSpeed:F2} Mbps (DL), {result.UploadSpeed:F2} Mbps (UL)",
                "EnhancedSpeedTest", new Dictionary<string, object>
                {
                    ["download"] = result.DownloadSpeed,
                    ["upload"] = result.UploadSpeed,
                    ["success"] = result.Success
                });

            return result;
        }

        /// <summary>
        /// 自動速度テストを開始する
        /// </summary>
        public void StartAutoSpeedTest()
        {
            if (_autoTestTimer != null) return;

            _autoTestTimer = new System.Timers.Timer(AutoTestIntervalMinutes * 60 * 1000);
            _autoTestTimer.Elapsed += async (s, e) => await PerformSpeedTestAsync();
            _autoTestTimer.Start();

            Logger.LogInfo($"自動速度テストを開始しました (間隔: {AutoTestIntervalMinutes}分)",
                "EnhancedSpeedTest");
        }

        /// <summary>
        /// 自動速度テストを停止する
        /// </summary>
        public void StopAutoSpeedTest()
        {
            if (_autoTestTimer == null) return;

            _autoTestTimer.Stop();
            _autoTestTimer.Dispose();
            _autoTestTimer = null;

            Logger.LogInfo("自動速度テストを停止しました", "EnhancedSpeedTest");
        }

        /// <summary>
        /// 速度テスト履歴を取得する
        /// </summary>
        public List<SpeedTestHistoryEntry> GetHistory(int count = 100)
        {
            lock (_lockObject)
            {
                return _history
                    .OrderByDescending(h => h.Timestamp)
                    .Take(count)
                    .ToList();
            }
        }

        /// <summary>
        /// 統計情報を計算する
        /// </summary>
        private SpeedTestStatistics CalculateStatistics()
        {
            lock (_lockObject)
            {
                if (!_history.Any())
                    return new SpeedTestStatistics();

                var recentEntries = _history
                    .Where(h => h.Timestamp > DateTime.Now.AddHours(-24))
                    .Where(h => h.Success)
                    .ToList();

                if (!recentEntries.Any())
                    return new SpeedTestStatistics();

                return new SpeedTestStatistics
                {
                    AverageDownloadSpeed = recentEntries.Average(h => h.DownloadSpeed),
                    AverageUploadSpeed = recentEntries.Average(h => h.UploadSpeed),
                    MaxDownloadSpeed = recentEntries.Max(h => h.DownloadSpeed),
                    MaxUploadSpeed = recentEntries.Max(h => h.UploadSpeed),
                    MinDownloadSpeed = recentEntries.Min(h => h.DownloadSpeed),
                    MinUploadSpeed = recentEntries.Min(h => h.UploadSpeed),
                    TestCount = recentEntries.Count,
                    SuccessRate = (double)_history.Count(h => h.Success) / _history.Count * 100,
                    LastTestTime = _history.Max(h => h.Timestamp)
                };
            }
        }

        /// <summary>
        /// 履歴をクリアする
        /// </summary>
        public void ClearHistory()
        {
            lock (_lockObject)
            {
                _history.Clear();
            }

            Logger.LogInfo("速度テスト履歴をクリアしました", "EnhancedSpeedTest");
        }

        /// <summary>
        /// 接続性テストを実行する
        /// </summary>
        public async Task<ConnectivityTestResult> TestConnectivityAsync(CancellationToken ct = default)
        {
            return await _speedTest.TestConnectivityAsync(ct);
        }

        /// <summary>
        /// ネットワーク統計を取得する
        /// </summary>
        public NetworkStatistics GetNetworkStatistics()
        {
            return _speedTest.GetNetworkStatistics();
        }

        public void Dispose()
        {
            StopAutoSpeedTest();
        }
    }

    // 新しいデータ構造
    public class SpeedTestHistoryEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double DownloadSpeed { get; set; }
        public double UploadSpeed { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    public class SpeedTestStatistics
    {
        public double AverageDownloadSpeed { get; set; }
        public double AverageUploadSpeed { get; set; }
        public double MaxDownloadSpeed { get; set; }
        public double MaxUploadSpeed { get; set; }
        public double MinDownloadSpeed { get; set; }
        public double MinUploadSpeed { get; set; }
        public int TestCount { get; set; }
        public double SuccessRate { get; set; }
        public DateTime LastTestTime { get; set; }
    }

    public class SpeedTestCompletedEventArgs : EventArgs
    {
        public SpeedTestResult Result { get; set; } = new();
        public SpeedTestStatistics Statistics { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class SpeedTestHistoryEventArgs : EventArgs
    {
        public SpeedTestHistoryEntry NewEntry { get; set; } = new();
        public SpeedTestStatistics CurrentStatistics { get; set; } = new();
    }
}
