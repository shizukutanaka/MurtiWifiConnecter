using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;

namespace MurtiWifiConnecter.Core.Security
{
    /// <summary>
    /// リアルタイム脅威インテリジェンス統合システム
    /// 外部脅威フィードと連携して脅威を検知・評価する
    /// </summary>
    public class ThreatIntelligenceManager
    {
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, ThreatFeed> _threatFeeds = new();
        private readonly List<ThreatIndicator> _activeThreats = new();
        private readonly object _lockObject = new();
        private readonly TimeSpan _feedUpdateInterval = TimeSpan.FromHours(1);

        public ThreatIntelligenceManager()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            InitializeDefaultFeeds();
        }

        /// <summary>
        /// デフォルトの脅威フィードを初期化する
        /// </summary>
        private void InitializeDefaultFeeds()
        {
            // 実際の実装では、信頼できる脅威インテリジェンスプロバイダーと連携
            _threatFeeds["malware-domains"] = new ThreatFeed
            {
                Name = "Malware Domains",
                Url = "https://example-threat-feed.com/malware-domains",
                Type = ThreatFeedType.Domain,
                UpdateInterval = _feedUpdateInterval,
                LastUpdated = DateTime.MinValue,
                IsEnabled = true
            };

            _threatFeeds["phishing-urls"] = new ThreatFeed
            {
                Name = "Phishing URLs",
                Url = "https://example-threat-feed.com/phishing-urls",
                Type = ThreatFeedType.URL,
                UpdateInterval = _feedUpdateInterval,
                LastUpdated = DateTime.MinValue,
                IsEnabled = true
            };

            _threatFeeds["malicious-ips"] = new ThreatFeed
            {
                Name = "Malicious IPs",
                Url = "https://example-threat-feed.com/malicious-ips",
                Type = ThreatFeedType.IP,
                UpdateInterval = _feedUpdateInterval,
                LastUpdated = DateTime.MinValue,
                IsEnabled = true
            };
        }

        /// <summary>
        /// すべての脅威フィードを更新する
        /// </summary>
        public async Task UpdateAllFeedsAsync()
        {
            var tasks = _threatFeeds.Values
                .Where(feed => feed.IsEnabled && DateTime.UtcNow - feed.LastUpdated > feed.UpdateInterval)
                .Select(UpdateFeedAsync);

            await Task.WhenAll(tasks);

            // アクティブな脅威を更新
            await UpdateActiveThreatsAsync();
        }

        /// <summary>
        /// 指定された脅威フィードを更新する
        /// </summary>
        private async Task UpdateFeedAsync(ThreatFeed feed)
        {
            try
            {
                var response = await _httpClient.GetAsync(feed.Url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var indicators = ParseThreatIndicators(content, feed.Type);

                lock (_lockObject)
                {
                    feed.LastUpdated = DateTime.UtcNow;
                    feed.LastError = null;

                    // フィードから取得した脅威指標を処理
                    foreach (var indicator in indicators)
                    {
                        ProcessThreatIndicator(indicator);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_lockObject)
                {
                    feed.LastError = ex.Message;
                }
            }
        }

        /// <summary>
        /// 脅威指標を解析する
        /// </summary>
        private List<ThreatIndicator> ParseThreatIndicators(string content, ThreatFeedType type)
        {
            var indicators = new List<ThreatIndicator>();

            try
            {
                switch (type)
                {
                    case ThreatFeedType.Domain:
                        indicators.AddRange(ParseDomainIndicators(content));
                        break;
                    case ThreatFeedType.IP:
                        indicators.AddRange(ParseIPIndicators(content));
                        break;
                    case ThreatFeedType.URL:
                        indicators.AddRange(ParseURLIndicators(content));
                        break;
                    case ThreatFeedType.Hash:
                        indicators.AddRange(ParseHashIndicators(content));
                        break;
                }
            }
            catch (Exception ex)
            {
                // パースエラーはログに記録
                Logger.LogWarning($"Failed to parse threat indicators: {ex.Message}", "ThreatIntelligenceManager");
            }

            return indicators;
        }

        private List<ThreatIndicator> ParseDomainIndicators(string content)
        {
            var indicators = new List<ThreatIndicator>();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines.Take(1000)) // 制限付きで処理
            {
                var domain = line.Trim();
                if (!string.IsNullOrEmpty(domain) && IsValidDomain(domain))
                {
                    indicators.Add(new ThreatIndicator
                    {
                        Type = "MaliciousDomain",
                        Value = domain,
                        Severity = ThreatSeverity.High,
                        Source = "ThreatFeed"
                    });
                }
            }

            return indicators;
        }

        private List<ThreatIndicator> ParseIPIndicators(string content)
        {
            var indicators = new List<ThreatIndicator>();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines.Take(1000))
            {
                var ip = line.Trim();
                if (!string.IsNullOrEmpty(ip) && IsValidIP(ip))
                {
                    indicators.Add(new ThreatIndicator
                    {
                        Type = "MaliciousIP",
                        Value = ip,
                        Severity = ThreatSeverity.Critical,
                        Source = "ThreatFeed"
                    });
                }
            }

            return indicators;
        }

        private List<ThreatIndicator> ParseURLIndicators(string content)
        {
            var indicators = new List<ThreatIndicator>();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines.Take(1000))
            {
                var url = line.Trim();
                if (!string.IsNullOrEmpty(url) && IsValidURL(url))
                {
                    indicators.Add(new ThreatIndicator
                    {
                        Type = "PhishingURL",
                        Value = url,
                        Severity = ThreatSeverity.High,
                        Source = "ThreatFeed"
                    });
                }
            }

            return indicators;
        }

        private List<ThreatIndicator> ParseHashIndicators(string content)
        {
            var indicators = new List<ThreatIndicator>();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines.Take(1000))
            {
                var hash = line.Trim();
                if (!string.IsNullOrEmpty(hash) && IsValidHash(hash))
                {
                    indicators.Add(new ThreatIndicator
                    {
                        Type = "MaliciousHash",
                        Value = hash,
                        Severity = ThreatSeverity.Critical,
                        Source = "ThreatFeed"
                    });
                }
            }

            return indicators;
        }

        /// <summary>
        /// 脅威指標を処理する
        /// </summary>
        private void ProcessThreatIndicator(ThreatIndicator indicator)
        {
            // 重複チェック
            var existingIndex = _activeThreats.FindIndex(t =>
                t.Type == indicator.Type && t.Value == indicator.Value);

            if (existingIndex >= 0)
            {
                // 既存の脅威指標を更新
                _activeThreats[existingIndex] = indicator;
            }
            else
            {
                // 新しい脅威指標を追加
                _activeThreats.Add(indicator);
            }
        }

        /// <summary>
        /// アクティブな脅威を更新する
        /// </summary>
        private async Task UpdateActiveThreatsAsync()
        {
            lock (_lockObject)
            {
                // 古い脅威を削除（24時間以上経過したもの）
                _activeThreats.RemoveAll(t =>
                    DateTime.UtcNow - t.DiscoveredUtc > TimeSpan.FromHours(24));

                // 高リスクの脅威を優先的に保持
                if (_activeThreats.Count > 10000)
                {
                    _activeThreats.Sort((a, b) => b.Severity.CompareTo(a.Severity));
                    _activeThreats.RemoveRange(5000, _activeThreats.Count - 5000);
                }
            }
        }

        /// <summary>
        /// 指定された値が脅威指標に該当するかをチェックする
        /// </summary>
        public bool IsThreat(string value)
        {
            lock (_lockObject)
            {
                return _activeThreats.Any(t => t.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// 指定された値に関連する脅威指標を取得する
        /// </summary>
        public List<ThreatIndicator> GetThreatsForValue(string value)
        {
            lock (_lockObject)
            {
                return _activeThreats.Where(t =>
                    t.Value.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                    value.Contains(t.Value, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        /// <summary>
        /// 脅威フィードの統計情報を取得する
        /// </summary>
        public ThreatIntelligenceStats GetStats()
        {
            lock (_lockObject)
            {
                return new ThreatIntelligenceStats
                {
                    TotalFeeds = _threatFeeds.Count,
                    ActiveFeeds = _threatFeeds.Count(f => f.IsEnabled),
                    TotalThreats = _activeThreats.Count,
                    CriticalThreats = _activeThreats.Count(t => t.Severity == ThreatSeverity.Critical),
                    HighThreats = _activeThreats.Count(t => t.Severity == ThreatSeverity.High),
                    LastUpdated = _activeThreats.Any() ? _activeThreats.Max(t => t.DiscoveredUtc) : DateTime.MinValue
                };
            }
        }

        private bool IsValidDomain(string domain)
        {
            return domain.Contains('.') && domain.Length <= 253 &&
                   !domain.StartsWith('.') && !domain.EndsWith('.');
        }

        private bool IsValidIP(string ip)
        {
            return System.Net.IPAddress.TryParse(ip, out _);
        }

        private bool IsValidURL(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out _);
        }

        private bool IsValidHash(string hash)
        {
            return (hash.Length == 32 || hash.Length == 40 || hash.Length == 64) &&
                   hash.All(c => char.IsLetterOrDigit(c));
        }

        /// <summary>
        /// 脅威フィード情報
        /// </summary>
        private class ThreatFeed
        {
            public string Name { get; set; } = "";
            public string Url { get; set; } = "";
            public ThreatFeedType Type { get; set; }
            public TimeSpan UpdateInterval { get; set; }
            public DateTime LastUpdated { get; set; }
            public string? LastError { get; set; }
            public bool IsEnabled { get; set; }
        }

        /// <summary>
        /// 脅威フィードタイプ
        /// </summary>
        private enum ThreatFeedType
        {
            Domain,
            IP,
            URL,
            Hash
        }

        /// <summary>
        /// 脅威インテリジェンス統計情報
        /// </summary>
        public class ThreatIntelligenceStats
        {
            public int TotalFeeds { get; set; }
            public int ActiveFeeds { get; set; }
            public int TotalThreats { get; set; }
            public int CriticalThreats { get; set; }
            public int HighThreats { get; set; }
            public DateTime LastUpdated { get; set; }
        }
    }
}
