using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// リアルタイム脅威インテリジェンスマネージャー
    /// </summary>
    public class RealTimeThreatIntelligenceManager
    {
        private readonly ILogger<RealTimeThreatIntelligenceManager> _logger;
        private readonly List<ThreatFeed> _threatFeeds;
        private readonly Dictionary<string, ThreatIndicator> _indicators;

        public RealTimeThreatIntelligenceManager(ILogger<RealTimeThreatIntelligenceManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _threatFeeds = new List<ThreatFeed>();
            _indicators = new Dictionary<string, ThreatIndicator>();
        }

        /// <summary>
        /// 脅威フィードを登録
        /// </summary>
        public async Task<bool> RegisterThreatFeedAsync(string feedName, string feedUrl, ThreatFeedType feedType)
        {
            try
            {
                var feed = new ThreatFeed
                {
                    Name = feedName,
                    Url = feedUrl,
                    Type = feedType,
                    IsActive = true,
                    LastUpdated = DateTime.UtcNow,
                    UpdateIntervalMinutes = 15
                };

                _threatFeeds.Add(feed);

                await _logger.LogInformation($"脅威フィードを登録しました: {feedName}");

                return true;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"脅威フィード登録に失敗しました: {feedName} - {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 脅威インテリジェンスを更新
        /// </summary>
        public async Task<bool> UpdateThreatIntelligenceAsync()
        {
            try
            {
                var newIndicators = new List<ThreatIndicator>();

                foreach (var feed in _threatFeeds.Where(f => f.IsActive))
                {
                    var indicators = await FetchThreatIndicatorsAsync(feed);
                    newIndicators.AddRange(indicators);
                }

                // 重複を除去して追加
                foreach (var indicator in newIndicators)
                {
                    if (!_indicators.ContainsKey(indicator.Id))
                    {
                        _indicators[indicator.Id] = indicator;
                    }
                }

                await _logger.LogInformation($"脅威インテリジェンスを更新しました。インジケーター数: {newIndicators.Count}");

                return true;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"脅威インテリジェンス更新に失敗しました: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// IPアドレスを脅威インテリジェンスでチェック
        /// </summary>
        public async Task<ThreatCheckResult> CheckIPAddressAsync(string ipAddress)
        {
            try
            {
                var indicators = _indicators.Values.Where(i => i.Type == ThreatIndicatorType.IPAddress && i.Value == ipAddress).ToList();

                if (indicators.Any())
                {
                    var result = new ThreatCheckResult
                    {
                        IPAddress = ipAddress,
                        IsThreat = true,
                        ThreatLevel = indicators.Max(i => i.Severity),
                        Indicators = indicators,
                        CheckedAt = DateTime.UtcNow
                    };

                    await _logger.LogWarning($"脅威IPアドレスを検知しました: {ipAddress}");

                    return result;
                }

                return new ThreatCheckResult { IPAddress = ipAddress, IsThreat = false, CheckedAt = DateTime.UtcNow };
            }
            catch (Exception ex)
            {
                await _logger.LogError($"IPアドレスチェックに失敗しました: {ipAddress} - {ex.Message}", ex);
                return new ThreatCheckResult { IPAddress = ipAddress, IsThreat = false, CheckedAt = DateTime.UtcNow };
            }
        }

        private async Task<List<ThreatIndicator>> FetchThreatIndicatorsAsync(ThreatFeed feed)
        {
            // 脅威フィードからのデータ取得シミュレーション
            await Task.Delay(100);

            var indicators = new List<ThreatIndicator>();

            // シミュレーションでインジケーターを生成
            for (int i = 0; i < 5; i++)
            {
                indicators.Add(new ThreatIndicator
                {
                    Id = Guid.NewGuid().ToString(),
                    FeedName = feed.Name,
                    Type = ThreatIndicatorType.IPAddress,
                    Value = $"192.168.1.{i + 100}",
                    Severity = ThreatSeverity.Medium,
                    Description = $"Suspicious activity from {feed.Name}",
                    FirstSeen = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow
                });
            }

            return indicators;
        }
    }

    /// <summary>
    /// 脅威フィード情報
    /// </summary>
    public class ThreatFeed
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public ThreatFeedType Type { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastUpdated { get; set; }
        public int UpdateIntervalMinutes { get; set; }
    }

    /// <summary>
    /// 脅威フィードタイプ
    /// </summary>
    public enum ThreatFeedType
    {
        Malware,
        Phishing,
        C2,
        DarkWeb
    }

    /// <summary>
    /// 脅威インジケーター
    /// </summary>
    public class ThreatIndicator
    {
        public string Id { get; set; } = "";
        public string FeedName { get; set; } = "";
        public ThreatIndicatorType Type { get; set; }
        public string Value { get; set; } = "";
        public ThreatSeverity Severity { get; set; }
        public string Description { get; set; } = "";
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
    }

    /// <summary>
    /// 脅威インジケータータイプ
    /// </summary>
    public enum ThreatIndicatorType
    {
        IPAddress,
        Domain,
        Hash,
        URL
    }

    /// <summary>
    /// 脅威深刻度
    /// </summary>
    public enum ThreatSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// 脅威チェック結果
    /// </summary>
    public class ThreatCheckResult
    {
        public string IPAddress { get; set; } = "";
        public bool IsThreat { get; set; }
        public ThreatSeverity ThreatLevel { get; set; }
        public List<ThreatIndicator> Indicators { get; set; } = new();
        public DateTime CheckedAt { get; set; }
    }
}
