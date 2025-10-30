using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 自動ポリシー適用機能を提供するクラス
    /// ネットワーク状況に応じた自動設定変更機能
    /// </summary>
    public class PolicyAutomationEngine : IDisposable
    {
        private readonly WifiAnalyzer _wifiAnalyzer;
        private readonly EnhancedSpeedTest _speedTest;
        private readonly ZeroTrustEvaluator _zeroTrustEvaluator;
        private readonly System.Timers.Timer _monitoringTimer;

        private bool _isRunning;
        private readonly Dictionary<string, PolicyRule> _activeRules = new();

        public event EventHandler<PolicyAppliedEventArgs>? PolicyApplied;
        public event EventHandler<PolicyTriggeredEventArgs>? PolicyTriggered;

        // 設定
        private const int MonitoringIntervalSeconds = 30;

        public PolicyAutomationEngine()
        {
            _wifiAnalyzer = new WifiAnalyzer();
            _speedTest = new EnhancedSpeedTest();
            _zeroTrustEvaluator = new ZeroTrustEvaluator();

            _monitoringTimer = new System.Timers.Timer(MonitoringIntervalSeconds * 1000);
            _monitoringTimer.Elapsed += async (s, e) => await MonitorAndApplyPoliciesAsync();

            InitializeDefaultRules();
        }

        /// <summary>
        /// ポリシー自動適用を開始する
        /// </summary>
        public void StartAutomation()
        {
            if (_isRunning) return;

            _isRunning = true;
            _wifiAnalyzer.StartAnalysis();
            _speedTest.StartAutoSpeedTest();
            _monitoringTimer.Start();

            Logger.LogInfo("ポリシー自動適用を開始しました", "PolicyAutomationEngine");
        }

        /// <summary>
        /// ポリシー自動適用を停止する
        /// </summary>
        public void StopAutomation()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _wifiAnalyzer.StopAnalysis();
            _speedTest.StopAutoSpeedTest();
            _monitoringTimer.Stop();

            Logger.LogInfo("ポリシー自動適用を停止しました", "PolicyAutomationEngine");
        }

        /// <summary>
        /// ポリシールールを追加する
        /// </summary>
        public void AddRule(PolicyRule rule)
        {
            _activeRules[rule.Id] = rule;

            Logger.LogInfo($"ポリシールールを追加しました: {rule.Name}", "PolicyAutomationEngine",
                new Dictionary<string, object>
                {
                    ["ruleId"] = rule.Id,
                    ["ruleName"] = rule.Name
                });
        }

        /// <summary>
        /// ポリシールールを削除する
        /// </summary>
        public void RemoveRule(string ruleId)
        {
            if (_activeRules.Remove(ruleId))
            {
                Logger.LogInfo($"ポリシールールを削除しました: {ruleId}", "PolicyAutomationEngine");
            }
        }

        /// <summary>
        /// ポリシーを監視・適用する
        /// </summary>
        private async Task MonitorAndApplyPoliciesAsync()
        {
            try
            {
                var context = await GatherNetworkContextAsync();

                foreach (var rule in _activeRules.Values)
                {
                    if (await EvaluateRuleAsync(rule, context))
                    {
                        await ApplyRuleAsync(rule, context);

                        PolicyTriggered?.Invoke(this, new PolicyTriggeredEventArgs
                        {
                            Rule = rule,
                            Context = context,
                            TriggerTime = DateTime.Now
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogError(ex, "PolicyAutomationEngine", "ポリシー監視中にエラーが発生しました");
            }
        }

        /// <summary>
        /// ネットワーク状況を収集する
        /// </summary>
        private async Task<NetworkContext> GatherNetworkContextAsync()
        {
            var context = new NetworkContext();

            // WiFi分析情報
            var analysis = _wifiAnalyzer.GetSignalHistory().LastOrDefault();
            if (analysis != null)
            {
                context.SignalStrength = analysis.SignalStrength;
                context.Channel = analysis.Channel;
            }

            // 速度テスト情報
            var speedStats = await _speedTest.GetNetworkStatistics();
            context.CurrentSpeed = speedStats.CurrentSpeed;

            // 接続情報（実際の実装では現在の接続情報を取得）
            context.IsConnected = true; // 仮実装
            context.ConnectedSsid = "CurrentNetwork"; // 仮実装

            return context;
        }

        /// <summary>
        /// ポリシールールを評価する
        /// </summary>
        private async Task<bool> EvaluateRuleAsync(PolicyRule rule, NetworkContext context)
        {
            foreach (var condition in rule.Conditions)
            {
                if (!await EvaluateConditionAsync(condition, context))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 条件を評価する
        /// </summary>
        private async Task<bool> EvaluateConditionAsync(PolicyCondition condition, NetworkContext context)
        {
            var actualValue = GetContextValue(context, condition.Property);

            return condition.Operator switch
            {
                ComparisonOperator.LessThan => Convert.ToDouble(actualValue) < condition.Value,
                ComparisonOperator.GreaterThan => Convert.ToDouble(actualValue) > condition.Value,
                ComparisonOperator.Equal => actualValue?.ToString() == condition.Value?.ToString(),
                ComparisonOperator.Contains => actualValue?.ToString()?.Contains(condition.Value?.ToString() ?? "") ?? false,
                _ => false
            };
        }

        /// <summary>
        /// ポリシールールを適用する
        /// </summary>
        private async Task ApplyRuleAsync(PolicyRule rule, NetworkContext context)
        {
            foreach (var action in rule.Actions)
            {
                await ExecuteActionAsync(action, context);
            }

            PolicyApplied?.Invoke(this, new PolicyAppliedEventArgs
            {
                Rule = rule,
                Context = context,
                AppliedTime = DateTime.Now,
                Actions = rule.Actions
            });

            await Logger.LogInfo($"ポリシールールを適用しました: {rule.Name}", "PolicyAutomationEngine",
                new Dictionary<string, object>
                {
                    ["ruleId"] = rule.Id,
                    ["ruleName"] = rule.Name,
                    ["actionCount"] = rule.Actions.Count
                });
        }

        /// <summary>
        /// アクションを実行する
        /// </summary>
        private async Task ExecuteActionAsync(PolicyAction action, NetworkContext context)
        {
            switch (action.Type)
            {
                case PolicyActionType.Log:
                    await Logger.LogInfo($"ポリシーアクション実行: {action.Parameters["message"]}",
                        "PolicyAutomationEngine");
                    break;

                case PolicyActionType.SetConfig:
                    // 設定変更（実際の実装ではConfigManagerを使用）
                    await Logger.LogInfo($"設定変更アクション実行: {action.Parameters["key"]} = {action.Parameters["value"]}",
                        "PolicyAutomationEngine");
                    break;

                case PolicyActionType.Notify:
                    // 通知（実際の実装では通知システムを使用）
                    await Logger.LogInfo($"通知アクション実行: {action.Parameters["title"]}",
                        "PolicyAutomationEngine");
                    break;
            }
        }

        /// <summary>
        /// コンテキストから値を取得する
        /// </summary>
        private object GetContextValue(NetworkContext context, string property)
        {
            return property switch
            {
                "SignalStrength" => context.SignalStrength,
                "Channel" => context.Channel,
                "CurrentSpeed" => context.CurrentSpeed,
                "IsConnected" => context.IsConnected,
                "ConnectedSsid" => context.ConnectedSsid,
                _ => null
            };
        }

        /// <summary>
        /// デフォルトのポリシールールを初期化する
        /// </summary>
        private void InitializeDefaultRules()
        {
            // 低信号強度時のポリシー
            var lowSignalRule = new PolicyRule
            {
                Id = "low-signal-policy",
                Name = "低信号強度時の自動調整",
                Description = "信号強度が低い場合に自動的に設定を調整",
                Conditions = new List<PolicyCondition>
                {
                    new PolicyCondition
                    {
                        Property = "SignalStrength",
                        Operator = ComparisonOperator.LessThan,
                        Value = 30
                    }
                },
                Actions = new List<PolicyAction>
                {
                    new PolicyAction
                    {
                        Type = PolicyActionType.Log,
                        Parameters = new Dictionary<string, string>
                        {
                            ["message"] = "低信号強度を検知しました。接続品質が低下する可能性があります。"
                        }
                    }
                }
            };

            AddRule(lowSignalRule);

            // 速度低下時のポリシー
            var slowSpeedRule = new PolicyRule
            {
                Id = "slow-speed-policy",
                Name = "速度低下時の自動診断",
                Description = "速度が低下した場合に自動的に診断を実行",
                Conditions = new List<PolicyCondition>
                {
                    new PolicyCondition
                    {
                        Property = "CurrentSpeed",
                        Operator = ComparisonOperator.LessThan,
                        Value = 5 // 5Mbps以下
                    }
                },
                Actions = new List<PolicyAction>
                {
                    new PolicyAction
                    {
                        Type = PolicyActionType.Log,
                        Parameters = new Dictionary<string, string>
                        {
                            ["message"] = "速度低下を検知しました。ネットワーク診断を実行してください。"
                        }
                    }
                }
            };

            AddRule(slowSpeedRule);
        }

        public void Dispose()
        {
            StopAutomation();
            _wifiAnalyzer.Dispose();
            _speedTest.Dispose();
            _monitoringTimer.Dispose();
        }
    }

    // データ構造定義
    public class NetworkContext
    {
        public int SignalStrength { get; set; }
        public int Channel { get; set; }
        public double CurrentSpeed { get; set; }
        public bool IsConnected { get; set; }
        public string ConnectedSsid { get; set; } = "";
    }

    public class PolicyRule
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<PolicyCondition> Conditions { get; set; } = new();
        public List<PolicyAction> Actions { get; set; } = new();
    }

    public class PolicyCondition
    {
        public string Property { get; set; } = "";
        public ComparisonOperator Operator { get; set; }
        public object Value { get; set; } = new();
    }

    public class PolicyAction
    {
        public PolicyActionType Type { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    public enum ComparisonOperator
    {
        LessThan,
        GreaterThan,
        Equal,
        Contains
    }

    public enum PolicyActionType
    {
        Log,
        SetConfig,
        Notify,
        Disconnect,
        Reconnect
    }

    public class PolicyAppliedEventArgs : EventArgs
    {
        public PolicyRule Rule { get; set; } = new();
        public NetworkContext Context { get; set; } = new();
        public DateTime AppliedTime { get; set; } = DateTime.Now;
        public List<PolicyAction> Actions { get; set; } = new();
    }

    public class PolicyTriggeredEventArgs : EventArgs
    {
        public PolicyRule Rule { get; set; } = new();
        public NetworkContext Context { get; set; } = new();
        public DateTime TriggerTime { get; set; } = DateTime.Now;
    }
}
