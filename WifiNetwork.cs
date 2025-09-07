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
            
        public string SignalQuality => SignalStrength switch
        {
            >= 80 => "優秀",
            >= 60 => "良好", 
            >= 40 => "普通",
            >= 20 => "弱い",
            _ => "非常に弱い"
        };

        public string DisplayText => HasConnectedBefore ? $"{SSID} ★" : SSID;
        
        public string GetHealthScoreDisplay()
        {
            return $"{HealthScore.OverallScore:F1} ({HealthScore.GetScoreDescription()})";
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
                >= 90 => "🟢",
                >= 80 => "🟡",
                >= 60 => "🟠",
                _ => "🔴"
            };
        }
    }
}
