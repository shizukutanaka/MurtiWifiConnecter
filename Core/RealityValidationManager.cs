using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// リアル性検証マネージャー - 操作の正当性と安全性を検証
    /// </summary>
    public static class RealityValidationManager
    {
        private static readonly Dictionary<string, ValidationRule> _validationRules = new();
        private static readonly List<ValidationHistory> _validationHistory = new();
        private static readonly object _historyLock = new();

        static RealityValidationManager()
        {
            InitializeDefaultRules();
        }

        /// <summary>
        /// 操作のリアル性を検証
        /// </summary>
        public static async Task<ValidationResult> ValidateOperationRealityAsync(
            string operationType,
            Dictionary<string, object> context,
            string userId = null)
        {
            var validationId = Guid.NewGuid().ToString();
            var startTime = DateTime.UtcNow;

            try
            {
                // 基本的な検証ルールを取得
                if (!_validationRules.TryGetValue(operationType, out var rule))
                {
                    rule = ValidationRule.Default;
                }

                // コンテキスト検証
                var contextValidation = await ValidateContextAsync(context, rule);

                // ユーザーパターン検証
                var patternValidation = await ValidateUserPatternsAsync(operationType, userId, context);

                // 時間ベース検証
                var timeValidation = ValidateTimePatterns(context);

                // リスクスコア計算
                var riskScore = CalculateRiskScore(contextValidation, patternValidation, timeValidation);

                var result = new ValidationResult
                {
                    ValidationId = validationId,
                    OperationType = operationType,
                    IsValid = riskScore < rule.MaxRiskScore,
                    RiskScore = riskScore,
                    Confidence = CalculateConfidence(contextValidation, patternValidation, timeValidation),
                    ValidationTime = DateTime.UtcNow - startTime,
                    Warnings = CollectWarnings(contextValidation, patternValidation, timeValidation)
                };

                // 履歴に記録
                RecordValidationHistory(result, context);

                return result;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Reality validation failed: {ex.Message}", nameof(RealityValidationManager), null, ex);

                return new ValidationResult
                {
                    ValidationId = validationId,
                    OperationType = operationType,
                    IsValid = false,
                    RiskScore = 1.0,
                    Confidence = 0.0,
                    ValidationTime = DateTime.UtcNow - startTime,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 高度な検証 - 複数の検証レイヤーを組み合わせ
        /// </summary>
        public static async Task<AdvancedValidationResult> ValidateOperationAdvancedAsync(
            string operationType,
            Dictionary<string, object> context,
            string userId = null,
            ValidationLevel level = ValidationLevel.Standard)
        {
            var basicResult = await ValidateOperationRealityAsync(operationType, context, userId);

            var advancedResult = new AdvancedValidationResult
            {
                BasicValidation = basicResult,
                ValidationLevel = level
            };

            // 高度な検証レベルに応じて追加チェック
            if (level >= ValidationLevel.Enhanced)
            {
                advancedResult.AnomalyDetection = await DetectAnomaliesAsync(operationType, context);
                advancedResult.PatternAnalysis = await AnalyzePatternsAsync(operationType, context, userId);
            }

            if (level >= ValidationLevel.Comprehensive)
            {
                advancedResult.ThreatIntelligence = await CheckThreatIntelligenceAsync(context);
                advancedResult.BehavioralAnalysis = await AnalyzeBehavioralPatternsAsync(userId, context);
            }

            // 総合的な妥当性判定
            advancedResult.OverallValidity = CalculateOverallValidity(advancedResult);

            return advancedResult;
        }

        /// <summary>
        /// 検証ルールをカスタマイズ
        /// </summary>
        public static void AddValidationRule(string operationType, ValidationRule rule)
        {
            _validationRules[operationType] = rule;
        }

        /// <summary>
        /// 検証履歴を取得
        /// </summary>
        public static IReadOnlyList<ValidationHistory> GetValidationHistory(int maxEntries = 100)
        {
            lock (_historyLock)
            {
                return _validationHistory
                    .OrderByDescending(h => h.Timestamp)
                    .Take(maxEntries)
                    .ToList();
            }
        }

        private static void InitializeDefaultRules()
        {
            // ネットワーク接続の検証ルール
            _validationRules["network_connect"] = new ValidationRule
            {
                OperationType = "network_connect",
                MaxRiskScore = 0.7,
                RequiredContextKeys = new[] { "ssid", "password_length" },
                MaxFrequencyPerMinute = 10,
                MaxFrequencyPerHour = 100
            };

            // 設定変更の検証ルール
            _validationRules["config_change"] = new ValidationRule
            {
                OperationType = "config_change",
                MaxRiskScore = 0.5,
                RequiredContextKeys = new[] { "setting_key", "old_value", "new_value" },
                MaxFrequencyPerMinute = 5,
                MaxFrequencyPerHour = 50
            };

            // ファイル操作の検証ルール
            _validationRules["file_operation"] = new ValidationRule
            {
                OperationType = "file_operation",
                MaxRiskScore = 0.6,
                RequiredContextKeys = new[] { "file_path", "operation_type" },
                MaxFrequencyPerMinute = 20,
                MaxFrequencyPerHour = 200
            };
        }

        private static async Task<ContextValidationResult> ValidateContextAsync(
            Dictionary<string, object> context,
            ValidationRule rule)
        {
            var result = new ContextValidationResult();

            // 必須キーのチェック
            foreach (var requiredKey in rule.RequiredContextKeys)
            {
                if (!context.ContainsKey(requiredKey))
                {
                    result.MissingKeys.Add(requiredKey);
                    result.Score += 0.3;
                }
            }

            // 値の妥当性チェック
            foreach (var kvp in context)
            {
                var validityScore = ValidateContextValue(kvp.Key, kvp.Value);
                result.ValueScores[kvp.Key] = validityScore;
                result.Score += validityScore * 0.1;
            }

            // SSIDの特殊チェック
            if (context.TryGetValue("ssid", out var ssidObj) && ssidObj is string ssid)
            {
                if (IsSuspiciousSsid(ssid))
                {
                    result.Score += 0.4;
                    result.SuspiciousElements.Add($"Suspicious SSID pattern: {ssid}");
                }
            }

            // パスワード強度のチェック
            if (context.TryGetValue("password_length", out var pwdLenObj) && pwdLenObj is int pwdLen)
            {
                if (pwdLen < 8)
                {
                    result.Score += 0.2;
                    result.WeakElements.Add("Password too short");
                }
            }

            result.IsValid = result.Score < 0.5;
            return await Task.FromResult(result);
        }

        private static async Task<PatternValidationResult> ValidateUserPatternsAsync(
            string operationType,
            string userId,
            Dictionary<string, object> context)
        {
            var result = new PatternValidationResult();

            // ユーザーの過去行動パターンを分析
            var userHistory = GetUserHistory(userId, operationType, TimeSpan.FromHours(24));

            if (userHistory.Count > 0)
            {
                // 頻度チェック
                var recentOperations = userHistory.Count(h =>
                    (DateTime.UtcNow - h.Timestamp) < TimeSpan.FromMinutes(5));

                if (recentOperations > 5)
                {
                    result.Score += 0.3;
                    result.Anomalies.Add($"High frequency: {recentOperations} operations in 5 minutes");
                }

                // 時間パターンチェック
                var currentHour = DateTime.UtcNow.Hour;
                var historicalHours = userHistory.Select(h => h.Timestamp.Hour).ToList();
                var averageHour = historicalHours.Average();

                if (Math.Abs(currentHour - averageHour) > 6)
                {
                    result.Score += 0.2;
                    result.Anomalies.Add($"Unusual time: operation at {currentHour}, usually at {averageHour:F1}");
                }
            }

            result.IsValid = result.Score < 0.4;
            return await Task.FromResult(result);
        }

        private static TimeValidationResult ValidateTimePatterns(Dictionary<string, object> context)
        {
            var result = new TimeValidationResult();
            var now = DateTime.UtcNow;

            // 営業時間外のチェック
            if (now.Hour < 6 || now.Hour > 22)
            {
                result.Score += 0.1;
                result.TimeAnomalies.Add("Operation outside business hours");
            }

            // 週末のチェック
            if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            {
                result.Score += 0.05;
                result.TimeAnomalies.Add("Operation on weekend");
            }

            result.IsValid = result.Score < 0.2;
            return result;
        }

        private static double CalculateRiskScore(
            ContextValidationResult context,
            PatternValidationResult pattern,
            TimeValidationResult time)
        {
            return Math.Min(1.0, context.Score * 0.4 + pattern.Score * 0.4 + time.Score * 0.2);
        }

        private static double CalculateConfidence(
            ContextValidationResult context,
            PatternValidationResult pattern,
            TimeValidationResult time)
        {
            var validChecks = new[] { context.IsValid, pattern.IsValid, time.IsValid };
            return validChecks.Count(v => v) / 3.0;
        }

        private static List<string> CollectWarnings(
            ContextValidationResult context,
            PatternValidationResult pattern,
            TimeValidationResult time)
        {
            var warnings = new List<string>();

            warnings.AddRange(context.SuspiciousElements);
            warnings.AddRange(context.WeakElements);
            warnings.AddRange(pattern.Anomalies);
            warnings.AddRange(time.TimeAnomalies);

            return warnings;
        }

        private static async Task<AnomalyDetectionResult> DetectAnomaliesAsync(
            string operationType,
            Dictionary<string, object> context)
        {
            // 異常検知アルゴリズム（簡易実装）
            var result = new AnomalyDetectionResult();

            // 統計的異常検知
            var operationStats = GetOperationStatistics(operationType, TimeSpan.FromDays(7));

            if (context.TryGetValue("operation_frequency", out var freqObj) && freqObj is int frequency)
            {
                if (frequency > operationStats.AverageFrequency * 3)
                {
                    result.Anomalies.Add($"Frequency anomaly: {frequency} vs average {operationStats.AverageFrequency}");
                    result.Score += 0.3;
                }
            }

            result.IsAnomalous = result.Score > 0.2;
            return await Task.FromResult(result);
        }

        private static async Task<PatternAnalysisResult> AnalyzePatternsAsync(
            string operationType,
            Dictionary<string, object> context,
            string userId)
        {
            var result = new PatternAnalysisResult();

            // パターン分析（機械学習ベースの簡易実装）
            var patterns = AnalyzeHistoricalPatterns(operationType, userId);

            if (patterns.UnusualPatterns.Count > 0)
            {
                result.Patterns.AddRange(patterns.UnusualPatterns);
                result.Confidence = patterns.Confidence;
            }

            return await Task.FromResult(result);
        }

        private static async Task<ThreatIntelligenceResult> CheckThreatIntelligenceAsync(
            Dictionary<string, object> context)
        {
            var result = new ThreatIntelligenceResult();

            // 脅威インテリジェンスチェック
            if (context.TryGetValue("ip_address", out var ipObj) && ipObj is string ip)
            {
                if (IsKnownThreatIp(ip))
                {
                    result.Threats.Add($"IP {ip} is associated with known threats");
                    result.ThreatLevel = ThreatLevel.High;
                }
            }

            if (context.TryGetValue("user_agent", out var uaObj) && uaObj is string ua)
            {
                if (IsSuspiciousUserAgent(ua))
                {
                    result.Threats.Add($"Suspicious user agent: {ua}");
                    result.ThreatLevel = ThreatLevel.Medium;
                }
            }

            return await Task.FromResult(result);
        }

        private static async Task<BehavioralAnalysisResult> AnalyzeBehavioralPatternsAsync(
            string userId,
            Dictionary<string, object> context)
        {
            var result = new BehavioralAnalysisResult();

            // 行動パターン分析
            var behaviorPatterns = GetUserBehaviorPatterns(userId);

            if (behaviorPatterns.Deviations.Count > 0)
            {
                result.Deviations.AddRange(behaviorPatterns.Deviations);
                result.RiskLevel = behaviorPatterns.RiskLevel;
            }

            return await Task.FromResult(result);
        }

        private static double CalculateOverallValidity(AdvancedValidationResult advancedResult)
        {
            var scores = new List<double>();

            if (advancedResult.BasicValidation != null)
                scores.Add(advancedResult.BasicValidation.RiskScore);

            if (advancedResult.AnomalyDetection != null)
                scores.Add(advancedResult.AnomalyDetection.Score);

            if (advancedResult.PatternAnalysis != null)
                scores.Add(1.0 - advancedResult.PatternAnalysis.Confidence);

            if (advancedResult.ThreatIntelligence != null)
                scores.Add((int)advancedResult.ThreatIntelligence.ThreatLevel / 10.0);

            return scores.Count > 0 ? scores.Average() : 0.5;
        }

        // ヘルパーメソッド
        private static double ValidateContextValue(string key, object value)
        {
            // 値の妥当性チェック（簡易実装）
            if (value == null) return 0.1;
            if (value is string str && string.IsNullOrWhiteSpace(str)) return 0.2;
            return 0.0;
        }

        private static bool IsSuspiciousSsid(string ssid)
        {
            // 疑わしいSSIDパターンのチェック
            var suspiciousPatterns = new[] { "admin", "root", "hack", "test123", "default" };
            return suspiciousPatterns.Any(pattern =>
                ssid.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private static List<ValidationHistory> GetUserHistory(string userId, string operationType, TimeSpan timeWindow)
        {
            // 実際の実装ではデータベースやキャッシュから取得
            return new List<ValidationHistory>();
        }

        private static OperationStatistics GetOperationStatistics(string operationType, TimeSpan timeWindow)
        {
            // 統計情報の取得（簡易実装）
            return new OperationStatistics
            {
                AverageFrequency = 10,
                MaxFrequency = 50
            };
        }

        private static HistoricalPatterns AnalyzeHistoricalPatterns(string operationType, string userId)
        {
            // 履歴パターン分析（簡易実装）
            return new HistoricalPatterns
            {
                UnusualPatterns = new List<string>(),
                Confidence = 0.8
            };
        }

        private static bool IsKnownThreatIp(string ip)
        {
            // 脅威IPチェック（実際の実装では脅威インテリジェンスDBを使用）
            return false;
        }

        private static bool IsSuspiciousUserAgent(string userAgent)
        {
            // 疑わしいUser-Agentチェック
            return false;
        }

        private static UserBehaviorPatterns GetUserBehaviorPatterns(string userId)
        {
            // ユーザーの行動パターン取得（簡易実装）
            return new UserBehaviorPatterns
            {
                Deviations = new List<string>(),
                RiskLevel = 0.1
            };
        }

        private static void RecordValidationHistory(ValidationResult result, Dictionary<string, object> context)
        {
            var history = new ValidationHistory
            {
                ValidationId = result.ValidationId,
                OperationType = result.OperationType,
                Timestamp = DateTime.UtcNow,
                IsValid = result.IsValid,
                RiskScore = result.RiskScore,
                Confidence = result.Confidence,
                ContextSnapshot = new Dictionary<string, object>(context)
            };

            lock (_historyLock)
            {
                _validationHistory.Add(history);

                // 履歴サイズを制限（最新1000件）
                if (_validationHistory.Count > 1000)
                {
                    _validationHistory.RemoveRange(0, _validationHistory.Count - 1000);
                }
            }
        }
    }

    /// <summary>
    /// 検証結果
    /// </summary>
    public class ValidationResult
    {
        public string ValidationId { get; set; }
        public string OperationType { get; set; }
        public bool IsValid { get; set; }
        public double RiskScore { get; set; }
        public double Confidence { get; set; }
        public TimeSpan ValidationTime { get; set; }
        public List<string> Warnings { get; set; } = new();
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 高度な検証結果
    /// </summary>
    public class AdvancedValidationResult
    {
        public ValidationResult BasicValidation { get; set; }
        public ValidationLevel ValidationLevel { get; set; }
        public AnomalyDetectionResult AnomalyDetection { get; set; }
        public PatternAnalysisResult PatternAnalysis { get; set; }
        public ThreatIntelligenceResult ThreatIntelligence { get; set; }
        public BehavioralAnalysisResult BehavioralAnalysis { get; set; }
        public double OverallValidity { get; set; }
    }

    /// <summary>
    /// 検証レベル
    /// </summary>
    public enum ValidationLevel
    {
        Basic,
        Standard,
        Enhanced,
        Comprehensive
    }

    /// <summary>
    /// 検証ルール
    /// </summary>
    public class ValidationRule
    {
        public static readonly ValidationRule Default = new()
        {
            OperationType = "default",
            MaxRiskScore = 0.5,
            RequiredContextKeys = Array.Empty<string>(),
            MaxFrequencyPerMinute = 10,
            MaxFrequencyPerHour = 100
        };

        public string OperationType { get; set; }
        public double MaxRiskScore { get; set; }
        public string[] RequiredContextKeys { get; set; }
        public int MaxFrequencyPerMinute { get; set; }
        public int MaxFrequencyPerHour { get; set; }
    }

    // 各種検証結果クラス
    public class ContextValidationResult
    {
        public double Score { get; set; }
        public bool IsValid { get; set; }
        public List<string> MissingKeys { get; set; } = new();
        public Dictionary<string, double> ValueScores { get; set; } = new();
        public List<string> SuspiciousElements { get; set; } = new();
        public List<string> WeakElements { get; set; } = new();
    }

    public class PatternValidationResult
    {
        public double Score { get; set; }
        public bool IsValid { get; set; }
        public List<string> Anomalies { get; set; } = new();
    }

    public class TimeValidationResult
    {
        public double Score { get; set; }
        public bool IsValid { get; set; }
        public List<string> TimeAnomalies { get; set; } = new();
    }

    public class AnomalyDetectionResult
    {
        public double Score { get; set; }
        public bool IsAnomalous { get; set; }
        public List<string> Anomalies { get; set; } = new();
    }

    public class PatternAnalysisResult
    {
        public List<string> Patterns { get; set; } = new();
        public double Confidence { get; set; }
    }

    public class ThreatIntelligenceResult
    {
        public ThreatLevel ThreatLevel { get; set; }
        public List<string> Threats { get; set; } = new();
    }

    public enum ThreatLevel
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }

    public class BehavioralAnalysisResult
    {
        public List<string> Deviations { get; set; } = new();
        public double RiskLevel { get; set; }
    }

    public class ValidationHistory
    {
        public string ValidationId { get; set; }
        public string OperationType { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsValid { get; set; }
        public double RiskScore { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, object> ContextSnapshot { get; set; }
    }

    // ヘルパークラス
    public class OperationStatistics
    {
        public double AverageFrequency { get; set; }
        public double MaxFrequency { get; set; }
    }

    public class HistoricalPatterns
    {
        public List<string> UnusualPatterns { get; set; } = new();
        public double Confidence { get; set; }
    }

    public class UserBehaviorPatterns
    {
        public List<string> Deviations { get; set; } = new();
        public double RiskLevel { get; set; }
    }
}
