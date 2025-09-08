using System;

namespace MurtiWifiConnecter
{
    public class WifiNetwork : IEquatable<WifiNetwork>
    {
        public string SSID { get; set; } = string.Empty;
        public int SignalStrength { get; set; }
        public bool IsConnected { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public bool HasConnectedBefore { get; set; }
        public ConnectionHealthScore HealthScore { get; set; } = new();

        public override int GetHashCode() => SSID?.GetHashCode() ?? 0;
        public override bool Equals(object? obj) => Equals(obj as WifiNetwork);
        public bool Equals(WifiNetwork? other) => other != null && 
            string.Equals(SSID, other.SSID, StringComparison.OrdinalIgnoreCase);
            

        public string DisplayText => HasConnectedBefore ? $"{SSID} ★" : SSID;
        
        public string GetHealthScoreDisplay()
        {
            return $"{HealthScore.OverallScore:F1} ({HealthScore.GetScoreDescription()})";
        }
        
        // 拡張メソッド統合
        public string GetSignalQualityText()
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
        
        public SecurityLevel GetSecurityLevel()
        {
            // SSIDからセキュリティタイプを推測（簡易版）
            var ssidLower = SSID.ToLower();
            
            if (ssidLower.Contains("wpa3") || ssidLower.Contains("secure"))
                return SecurityLevel.High;
            else if (ssidLower.Contains("wpa2") || ssidLower.Contains("protected"))
                return SecurityLevel.Medium;
            else if (ssidLower.Contains("wpa") || ssidLower.Contains("secure"))
                return SecurityLevel.Low;
            else if (ssidLower.Contains("open") || ssidLower.Contains("guest"))
                return SecurityLevel.None;
            else
                return SecurityLevel.Unknown;
        }
        
        public ConnectionRecommendation GetConnectionRecommendation()
        {
            var score = 0;
            
            // 信号強度による評価
            if (SignalStrength >= 70) score += 40;
            else if (SignalStrength >= 50) score += 25;
            else if (SignalStrength >= 30) score += 10;
            else score -= 10;
            
            // 履歴による評価
            if (HasConnectedBefore) score += 30;
            
            // 現在の接続状態
            if (IsConnected) return ConnectionRecommendation.AlreadyConnected;
            
            // セキュリティレベルによる評価
            var securityLevel = GetSecurityLevel();
            switch (securityLevel)
            {
                case SecurityLevel.High: score += 20; break;
                case SecurityLevel.Medium: score += 10; break;
                case SecurityLevel.Low: score += 5; break;
                case SecurityLevel.None: score -= 15; break;
                case SecurityLevel.Unknown: score -= 5; break;
            }
            
            return score switch
            {
                >= 80 => ConnectionRecommendation.HighlyRecommended,
                >= 60 => ConnectionRecommendation.Recommended,
                >= 40 => ConnectionRecommendation.Acceptable,
                >= 20 => ConnectionRecommendation.NotRecommended,
                _ => ConnectionRecommendation.NotRecommended
            };
        }
        
        public string GetEstimatedDistance()
        {
            return SignalStrength switch
            {
                >= 80 => "非常に近い (1-5m)",
                >= 60 => "近い (5-15m)",
                >= 40 => "普通 (15-30m)",
                >= 20 => "遠い (30-50m)",
                _ => "非常に遠い (50m+)"
            };
        }
        
        public string GetDisplaySummary()
        {
            var parts = new[]
            {
                GetSignalQualityText(),
                HasConnectedBefore ? "履歴あり" : null,
                IsConnected ? "接続中" : null
            }.Where(p => p != null);
            
            return string.Join(" | ", parts);
        }
    }
    
    public class ConnectionHealthScore
    {
        public double OverallScore { get; set; }
        public double SignalQuality { get; set; }
        public double ConnectionReliability { get; set; }
        public double PerformanceScore { get; set; }
        public double SecurityScore { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        
        public static ConnectionHealthScore Calculate(string ssid, NetworkStats? stats, int currentSignal)
        {
            var score = new ConnectionHealthScore();
            
            // 信号品質スコア (0-100)
            score.SignalQuality = Math.Min(100, Math.Max(0, currentSignal));
            
            if (stats != null)
            {
                // 接続信頼性スコア (0-100)
                score.ConnectionReliability = Math.Min(100, stats.SuccessRate);
                
                // 性能スコア (0-100) - 平均信号強度と安定性
                var stabilityBonus = stats.IsReliable ? 20 : 0;
                score.PerformanceScore = Math.Min(100, stats.AverageSignalStrength + stabilityBonus);
                
                // セキュリティスコア - 基本値70、オープンネットワークは-30
                score.SecurityScore = 70; // デフォルト値
            }
            else
            {
                // 統計データがない場合のデフォルト値
                score.ConnectionReliability = 50;
                score.PerformanceScore = Math.Min(100, currentSignal);
                score.SecurityScore = 70;
            }
            
            // 総合スコア計算 (加重平均)
            score.OverallScore = 
                (score.SignalQuality * 0.3) +
                (score.ConnectionReliability * 0.3) +
                (score.PerformanceScore * 0.25) +
                (score.SecurityScore * 0.15);
                
            return score;
        }
        
        public string GetScoreDescription()
        {
            return OverallScore switch
            {
                >= 90 => "優秀",
                >= 80 => "良好", 
                >= 70 => "普通",
                >= 60 => "やや劣る",
                >= 50 => "劣る",
                _ => "不良"
            };
        }
        
        public string GetHealthIcon()
        {
            return OverallScore switch
            {
                >= 90 => "高",
                >= 80 => "中",
                >= 60 => "低",
                _ => "不良"
            };
        }
    }
    
    public enum SecurityLevel
    {
        None,
        Low,
        Medium,
        High,
        Unknown
    }
    
    public enum ConnectionRecommendation
    {
        AlreadyConnected,
        HighlyRecommended,
        Recommended,
        Acceptable,
        NotRecommended
    }
}
