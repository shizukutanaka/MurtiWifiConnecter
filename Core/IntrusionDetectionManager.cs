using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 侵入検知システムを管理するクラス
    /// WiFiネットワーク上の不正アクセス試行をリアルタイム検知
    /// </summary>
    public class IntrusionDetectionManager
    {
        private readonly ILogger<IntrusionDetectionManager> _logger;
        private readonly Dictionary<string, IntrusionDetectionRule> _rules;
        private readonly List<IntrusionEvent> _recentEvents;

        public IntrusionDetectionManager(ILogger<IntrusionDetectionManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rules = new Dictionary<string, IntrusionDetectionRule>();
            _recentEvents = new List<IntrusionEvent>();
        }

        /// <summary>
        /// 侵入検知ルールを追加
        /// </summary>
        public async Task<bool> AddDetectionRuleAsync(string ruleName, IntrusionDetectionRule rule)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ruleName))
                    throw new ArgumentException("ルール名は必須です", nameof(ruleName));

                if (_rules.ContainsKey(ruleName))
                    throw new InvalidOperationException($"ルール '{ruleName}' は既に存在します");

                _rules[ruleName] = rule;

                await _logger.LogInformation("侵入検知ルールを追加しました", ruleName, new Dictionary<string, object>
                {
                    ["ruleName"] = ruleName,
                    ["ruleType"] = rule.RuleType.ToString(),
                    ["severity"] = rule.Severity.ToString()
                });

                return true;
            }
            catch (Exception ex)
            {
                await _logger.LogError("侵入検知ルールの追加に失敗しました", ruleName, ex);
                return false;
            }
        }

        /// <summary>
        /// ネットワークトラフィックを監視して侵入を検知
        /// </summary>
        public async Task<List<IntrusionEvent>> MonitorNetworkAsync()
        {
            var events = new List<IntrusionEvent>();

            try
            {
                // ネットワークインターフェースを監視
                var wifiInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                ni.OperationalStatus == OperationalStatus.Up);

                foreach (var wifiInterface in wifiInterfaces)
                {
                    var interfaceEvents = await AnalyzeInterfaceTrafficAsync(wifiInterface);
                    events.AddRange(interfaceEvents);
                }

                // 検知されたイベントを記録
                foreach (var @event in events)
                {
                    _recentEvents.Add(@event);

                    await _logger.LogWarning("侵入検知イベントが発生しました", @event.SourceIp, new Dictionary<string, object>
                    {
                        ["eventType"] = @event.EventType.ToString(),
                        ["severity"] = @event.Severity.ToString(),
                        ["sourceIp"] = @event.SourceIp,
                        ["description"] = @event.Description
                    });
                }

                // 古いイベントをクリーンアップ（直近100件のみ保持）
                if (_recentEvents.Count > 100)
                {
                    _recentEvents.RemoveRange(0, _recentEvents.Count - 100);
                }
            }
            catch (Exception ex)
            {
                await _logger.LogError("ネットワーク監視中にエラーが発生しました", ex);
            }

            return events;
        }

        /// <summary>
        /// 最近の侵入検知イベントを取得
        /// </summary>
        public IReadOnlyList<IntrusionEvent> GetRecentEvents(int maxCount = 50)
        {
            return _recentEvents
                .OrderByDescending(e => e.Timestamp)
                .Take(maxCount)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// 指定されたインターフェースのトラフィックを分析
        /// </summary>
        private async Task<List<IntrusionEvent>> AnalyzeInterfaceTrafficAsync(NetworkInterface wifiInterface)
        {
            var events = new List<IntrusionEvent>();

            try
            {
                // 実際の実装では、パケットキャプチャやログ分析を行う
                // ここではシミュレーション

                // シミュレーション：不正アクセス試行をランダムで検出
                var suspiciousActivityDetected = new Random().Next(100) < 10; // 10%の確率で検出

                if (suspiciousActivityDetected)
                {
                    var eventType = (IntrusionEventType)new Random().Next(1, 5); // ランダムなイベントタイプ

                    var @event = new IntrusionEvent
                    {
                        EventType = eventType,
                        Severity = GetSeverityForEventType(eventType),
                        SourceIp = GenerateRandomIpAddress(),
                        DestinationIp = wifiInterface.GetIPProperties().UnicastAddresses
                            .FirstOrDefault(ip => ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? "unknown",
                        Description = GetEventDescription(eventType),
                        Timestamp = DateTime.UtcNow,
                        InterfaceName = wifiInterface.Name,
                        RuleTriggered = "SuspiciousActivityRule"
                    };

                    events.Add(@event);
                }

                // 各ルールに対してチェックを実行
                foreach (var rule in _rules.Values)
                {
                    var ruleEvents = await CheckRuleAsync(rule, wifiInterface);
                    events.AddRange(ruleEvents);
                }
            }
            catch (Exception ex)
            {
                await _logger.LogWarning($"インターフェース '{wifiInterface.Name}' の分析に失敗しました", ex.Message);
            }

            return events;
        }

        /// <summary>
        /// 指定されたルールに対してチェックを実行
        /// </summary>
        private async Task<List<IntrusionEvent>> CheckRuleAsync(IntrusionDetectionRule rule, NetworkInterface wifiInterface)
        {
            var events = new List<IntrusionEvent>();

            try
            {
                // 実際の実装では、ルールの条件に基づいてチェックを実行
                // ここではシミュレーション

                var ruleTriggered = false;

                switch (rule.RuleType)
                {
                    case IntrusionDetectionRuleType.UnusualLoginAttempts:
                        ruleTriggered = await CheckUnusualLoginAttemptsAsync(rule, wifiInterface);
                        break;

                    case IntrusionDetectionRuleType.PortScanning:
                        ruleTriggered = await CheckPortScanningAsync(rule, wifiInterface);
                        break;

                    case IntrusionDetectionRuleType.DosAttack:
                        ruleTriggered = await CheckDosAttackAsync(rule, wifiInterface);
                        break;

                    case IntrusionDetectionRuleType.RogueAccessPoint:
                        ruleTriggered = await CheckRogueAccessPointAsync(rule, wifiInterface);
                        break;
                }

                if (ruleTriggered)
                {
                    var @event = new IntrusionEvent
                    {
                        EventType = GetEventTypeFromRule(rule.RuleType),
                        Severity = rule.Severity,
                        SourceIp = "unknown",
                        DestinationIp = wifiInterface.GetIPProperties().UnicastAddresses
                            .FirstOrDefault(ip => ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? "unknown",
                        Description = $"ルール '{rule.Name}' がトリガーされました",
                        Timestamp = DateTime.UtcNow,
                        InterfaceName = wifiInterface.Name,
                        RuleTriggered = rule.Name
                    };

                    events.Add(@event);
                }
            }
            catch (Exception ex)
            {
                await _logger.LogWarning($"ルール '{rule.Name}' のチェックに失敗しました", ex.Message);
            }

            return events;
        }

        private async Task<bool> CheckUnusualLoginAttemptsAsync(IntrusionDetectionRule rule, NetworkInterface wifiInterface)
        {
            // 実際の実装では、認証ログを分析
            await Task.Delay(10); // シミュレーション
            return new Random().Next(100) < 5; // 5%の確率で検出
        }

        private async Task<bool> CheckPortScanningAsync(IntrusionDetectionRule rule, NetworkInterface wifiInterface)
        {
            // 実際の実装では、ポートスキャン検知ツールを使用
            await Task.Delay(10); // シミュレーション
            return new Random().Next(100) < 3; // 3%の確率で検出
        }

        private async Task<bool> CheckDosAttackAsync(IntrusionDetectionRule rule, NetworkInterface wifiInterface)
        {
            // 実際の実装では、トラフィックパターンを分析
            await Task.Delay(10); // シミュレーション
            return new Random().Next(100) < 2; // 2%の確率で検出
        }

        private async Task<bool> CheckRogueAccessPointAsync(IntrusionDetectionRule rule, NetworkInterface wifiInterface)
        {
            // 実際の実装では、周囲のアクセスポイントをスキャン
            await Task.Delay(10); // シミュレーション
            return new Random().Next(100) < 1; // 1%の確率で検出
        }

        private string GenerateRandomIpAddress()
        {
            var random = new Random();
            return $"{random.Next(1, 255)}.{random.Next(0, 255)}.{random.Next(0, 255)}.{random.Next(1, 255)}";
        }

        private IntrusionSeverity GetSeverityForEventType(IntrusionEventType eventType)
        {
            return eventType switch
            {
                IntrusionEventType.PortScanning => IntrusionSeverity.High,
                IntrusionEventType.DosAttack => IntrusionSeverity.Critical,
                IntrusionEventType.RogueAccessPoint => IntrusionSeverity.High,
                _ => IntrusionSeverity.Medium
            };
        }

        private string GetEventDescription(IntrusionEventType eventType)
        {
            return eventType switch
            {
                IntrusionEventType.UnusualLoginAttempts => "異常なログイン試行が検出されました",
                IntrusionEventType.PortScanning => "ポートスキャンが検出されました",
                IntrusionEventType.DosAttack => "DoS攻撃が検出されました",
                IntrusionEventType.RogueAccessPoint => "不正アクセスポイントが検出されました",
                _ => "不審な活動が検出されました"
            };
        }

        private IntrusionEventType GetEventTypeFromRule(IntrusionDetectionRuleType ruleType)
        {
            return ruleType switch
            {
                IntrusionDetectionRuleType.UnusualLoginAttempts => IntrusionEventType.UnusualLoginAttempts,
                IntrusionDetectionRuleType.PortScanning => IntrusionEventType.PortScanning,
                IntrusionDetectionRuleType.DosAttack => IntrusionEventType.DosAttack,
                IntrusionDetectionRuleType.RogueAccessPoint => IntrusionEventType.RogueAccessPoint,
                _ => IntrusionEventType.Other
            };
        }
    }

    /// <summary>
    /// 侵入検知ルール
    /// </summary>
    public class IntrusionDetectionRule
    {
        public string Name { get; set; } = "";
        public IntrusionDetectionRuleType RuleType { get; set; }
        public IntrusionSeverity Severity { get; set; }
        public bool IsEnabled { get; set; } = true;
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    /// <summary>
    /// 侵入検知イベント
    /// </summary>
    public class IntrusionEvent
    {
        public IntrusionEventType EventType { get; set; }
        public IntrusionSeverity Severity { get; set; }
        public string SourceIp { get; set; } = "";
        public string DestinationIp { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string InterfaceName { get; set; } = "";
        public string RuleTriggered { get; set; } = "";
    }

    /// <summary>
    /// 侵入検知ルールタイプ
    /// </summary>
    public enum IntrusionDetectionRuleType
    {
        UnusualLoginAttempts,
        PortScanning,
        DosAttack,
        RogueAccessPoint
    }

    /// <summary>
    /// 侵入イベントタイプ
    /// </summary>
    public enum IntrusionEventType
    {
        UnusualLoginAttempts,
        PortScanning,
        DosAttack,
        RogueAccessPoint,
        Other
    }

    /// <summary>
    /// 侵入の深刻度
    /// </summary>
    public enum IntrusionSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
}
