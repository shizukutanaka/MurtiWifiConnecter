using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// WiFiセキュリティ分析とホットスポット検知
    /// </summary>
    public static class SecurityAnalyzer
    {
        private static readonly HashSet<string> SuspiciousSSIDs = new()
        {
            "free wifi", "free internet", "public wifi", "guest", "open",
            "linksys", "netgear", "dlink", "asus", "tplink", "buffalo",
            "android", "iphone", "samsung", "huawei", "xiaomi"
        };
        
        private static readonly HashSet<string> CommonDefaultSSIDs = new()
        {
            "linksys", "netgear", "dlink", "asus-", "tp-link", "buffalo-",
            "elecom-", "nec-", "softbank", "au_wifi", "docomo"
        };
        
        /// <summary>
        /// ネットワークのセキュリティ分析
        /// </summary>
        public static async Task<SecurityAnalysisResult> AnalyzeNetworkSecurityAsync(
            List<WifiNetwork> networks, 
            CancellationToken cancellationToken = default)
        {
            var result = new SecurityAnalysisResult
            {
                AnalysisTime = DateTime.Now,
                NetworkAnalyses = new List<NetworkSecurityInfo>()
            };
            
            foreach (var network in networks)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                var analysis = AnalyzeNetworkSecurity(network);
                result.NetworkAnalyses.Add(analysis);
            }
            
            // 統計計算
            var totalNetworks = result.NetworkAnalyses.Count;
            result.SecureNetworks = result.NetworkAnalyses.Count(n => n.SecurityLevel == SecurityLevel.High);
            result.VulnerableNetworks = result.NetworkAnalyses.Count(n => n.SecurityLevel == SecurityLevel.Low);
            result.SuspiciousNetworks = result.NetworkAnalyses.Count(n => n.ThreatLevel > ThreatLevel.Low);
            
            // 全体的な脅威評価
            var avgThreatLevel = result.NetworkAnalyses.Any() ? 
                result.NetworkAnalyses.Average(n => (int)n.ThreatLevel) : 0;
                
            result.OverallThreatLevel = avgThreatLevel switch
            {
                >= 2.5 => ThreatLevel.High,
                >= 1.5 => ThreatLevel.Medium,
                _ => ThreatLevel.Low
            };
            
            result.Recommendations = GenerateSecurityRecommendations(result);
            
            await Task.CompletedTask; // 非同期対応
            return result;
        }
        
        /// <summary>
        /// 個別ネットワークのセキュリティ分析
        /// </summary>
        public static NetworkSecurityInfo AnalyzeNetworkSecurity(WifiNetwork network)
        {
            var info = new NetworkSecurityInfo
            {
                SSID = network.SSID,
                SignalStrength = network.SignalStrength
            };
            
            var riskFactors = new List<string>();
            var warningFlags = new List<string>();
            
            // 1. SSID分析
            AnalyzeSSID(network.SSID, riskFactors, warningFlags);
            
            // 2. セキュリティ方式推定（実際のWiFiスキャンではより詳細）
            var securityType = EstimateSecurityType(network);
            info.SecurityType = securityType;
            
            // 3. セキュリティレベル判定
            info.SecurityLevel = DetermineSecurityLevel(securityType, riskFactors);
            
            // 4. 脅威レベル判定
            info.ThreatLevel = DetermineThreatLevel(riskFactors, warningFlags);
            
            // 5. ホットスポット検知
            info.IsLikelyHotspot = DetectHotspot(network.SSID, network.SignalStrength);
            
            // 6. 推奨事項生成
            info.RiskFactors = riskFactors;
            info.Warnings = warningFlags;
            info.Recommendations = GenerateNetworkRecommendations(info, riskFactors, warningFlags);
            
            return info;
        }
        
        /// <summary>
        /// パスワード強度分析
        /// </summary>
        public static PasswordSecurityInfo AnalyzePasswordSecurity(string password)
        {
            var info = new PasswordSecurityInfo
            {
                Password = password,
                Length = password.Length
            };
            
            var score = 0;
            var issues = new List<string>();
            var suggestions = new List<string>();
            
            // 長さチェック
            if (password.Length >= 12)
                score += 25;
            else if (password.Length >= 8)
                score += 15;
            else
                issues.Add("パスワードが短すぎます");
            
            // 文字種類チェック
            if (Regex.IsMatch(password, @"[a-z]")) score += 10;
            else issues.Add("小文字が含まれていません");
            
            if (Regex.IsMatch(password, @"[A-Z]")) score += 10;
            else issues.Add("大文字が含まれていません");
            
            if (Regex.IsMatch(password, @"[0-9]")) score += 10;
            else issues.Add("数字が含まれていません");
            
            if (Regex.IsMatch(password, @"[^a-zA-Z0-9]")) score += 15;
            else suggestions.Add("記号を追加すると安全性が向上します");
            
            // パターン分析
            if (Regex.IsMatch(password, @"^[a-zA-Z]+$"))
                issues.Add("文字のみのパスワードは推測されやすいです");
                
            if (Regex.IsMatch(password, @"^[0-9]+$"))
                issues.Add("数字のみのパスワードは非常に危険です");
                
            if (Regex.IsMatch(password, @"(.)\1{2,}"))
                issues.Add("同じ文字の連続は避けてください");
                
            if (Regex.IsMatch(password, @"(abc|123|qwe|password|admin)", RegexOptions.IgnoreCase))
                issues.Add("一般的なパターンが含まれています");
            
            // 辞書攻撃耐性（簡易）
            var commonWords = new[] { "password", "123456", "admin", "user", "wifi", "internet" };
            if (commonWords.Any(word => password.ToLower().Contains(word)))
                issues.Add("一般的な単語が含まれています");
            
            info.Score = Math.Min(100, score);
            info.Strength = info.Score switch
            {
                >= 80 => PasswordStrength.VeryStrong,
                >= 60 => PasswordStrength.Strong,
                >= 40 => PasswordStrength.Medium,
                >= 20 => PasswordStrength.Weak,
                _ => PasswordStrength.VeryWeak
            };
            
            info.Issues = issues;
            info.Suggestions = suggestions;
            
            return info;
        }
        
        private static void AnalyzeSSID(string ssid, List<string> riskFactors, List<string> warningFlags)
        {
            var lowerSSID = ssid.ToLowerInvariant();
            
            // 疑わしいSSID名
            foreach (var suspicious in SuspiciousSSIDs)
            {
                if (lowerSSID.Contains(suspicious))
                {
                    riskFactors.Add($"疑わしいSSID名: {suspicious}");
                    warningFlags.Add("フィッシングネットワークの可能性");
                }
            }
            
            // デフォルトSSID名
            foreach (var defaultSSID in CommonDefaultSSIDs)
            {
                if (lowerSSID.StartsWith(defaultSSID))
                {
                    riskFactors.Add("デフォルトSSID名を使用");
                    warningFlags.Add("設定が初期状態の可能性");
                }
            }
            
            // 文字化けや不正な文字
            if (Regex.IsMatch(ssid, @"[^\x20-\x7E\u3000-\u9FAF]"))
            {
                riskFactors.Add("特殊文字や文字化けを含むSSID");
                warningFlags.Add("偽装ネットワークの可能性");
            }
            
            // 短すぎるSSID
            if (ssid.Length < 3)
            {
                riskFactors.Add("異常に短いSSID");
            }
            
            // 企業・店舗のなりすまし検知
            var brandNames = new[] { "starbucks", "mcdonalds", "7eleven", "lawson", "familymart" };
            if (brandNames.Any(brand => lowerSSID.Contains(brand)))
            {
                warningFlags.Add("企業ブランドになりすましの可能性");
            }
        }
        
        private static string EstimateSecurityType(WifiNetwork network)
        {
            // 実際のWiFiスキャンではより詳細な情報が取得できるが、
            // ここでは簡易的に推定
            if (network.SSID.ToLower().Contains("open") || network.SSID.ToLower().Contains("free"))
            {
                return "Open (セキュリティなし)";
            }
            
            // 一般的なネットワークはWPA2/WPA3と仮定
            return "WPA2/WPA3 (推定)";
        }
        
        private static SecurityLevel DetermineSecurityLevel(string securityType, List<string> riskFactors)
        {
            if (securityType.Contains("Open"))
                return SecurityLevel.None;
                
            if (riskFactors.Count >= 3)
                return SecurityLevel.Low;
            else if (riskFactors.Count >= 1)
                return SecurityLevel.Medium;
            else
                return SecurityLevel.High;
        }
        
        private static ThreatLevel DetermineThreatLevel(List<string> riskFactors, List<string> warningFlags)
        {
            var threatScore = riskFactors.Count + warningFlags.Count * 2;
            
            return threatScore switch
            {
                >= 5 => ThreatLevel.High,
                >= 2 => ThreatLevel.Medium,
                _ => ThreatLevel.Low
            };
        }
        
        private static bool DetectHotspot(string ssid, int signalStrength)
        {
            var lowerSSID = ssid.ToLowerInvariant();
            
            // 一般的なホットスポットの特徴
            var hotspotIndicators = new[]
            {
                "free", "public", "guest", "wifi", "internet",
                "cafe", "restaurant", "hotel", "shop", "store"
            };
            
            var hasHotspotIndicator = hotspotIndicators.Any(indicator => lowerSSID.Contains(indicator));
            var hasStrongSignal = signalStrength > 70; // 強い信号（近距離）
            
            return hasHotspotIndicator && hasStrongSignal;
        }
        
        private static List<string> GenerateNetworkRecommendations(
            NetworkSecurityInfo info, 
            List<string> riskFactors, 
            List<string> warningFlags)
        {
            var recommendations = new List<string>();
            
            if (info.SecurityLevel == SecurityLevel.None)
            {
                recommendations.Add("このネットワークは暗号化されていません。機密情報の送受信は避けてください");
                recommendations.Add("VPNを使用して通信を保護することを強く推奨します");
            }
            
            if (info.SecurityLevel == SecurityLevel.Low)
            {
                recommendations.Add("このネットワークはセキュリティリスクがあります");
                recommendations.Add("可能であれば他の安全なネットワークを使用してください");
            }
            
            if (info.IsLikelyHotspot)
            {
                recommendations.Add("公衆無線LANの可能性があります。個人情報の入力は控えてください");
                recommendations.Add("HTTPSサイトの使用を徹底してください");
            }
            
            if (warningFlags.Any(w => w.Contains("フィッシング")))
            {
                recommendations.Add("偽装ネットワークの可能性があります。接続前に管理者に確認してください");
            }
            
            return recommendations;
        }
        
        private static List<string> GenerateSecurityRecommendations(SecurityAnalysisResult result)
        {
            var recommendations = new List<string>();
            
            if (result.VulnerableNetworks > 0)
            {
                recommendations.Add($"{result.VulnerableNetworks}個の脆弱なネットワークが検出されました");
            }
            
            if (result.SuspiciousNetworks > 0)
            {
                recommendations.Add($"{result.SuspiciousNetworks}個の疑わしいネットワークが検出されました");
            }
            
            if (result.OverallThreatLevel >= ThreatLevel.Medium)
            {
                recommendations.Add("周辺に複数のリスクのあるネットワークが存在します");
                recommendations.Add("信頼できるネットワークのみを使用することを推奨します");
            }
            
            recommendations.Add("定期的にネットワークセキュリティを確認してください");
            
            return recommendations;
        }
    }
    
    // データクラス群
    public class SecurityAnalysisResult
    {
        public DateTime AnalysisTime { get; set; }
        public List<NetworkSecurityInfo> NetworkAnalyses { get; set; } = new();
        public int SecureNetworks { get; set; }
        public int VulnerableNetworks { get; set; }
        public int SuspiciousNetworks { get; set; }
        public ThreatLevel OverallThreatLevel { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }
    
    public class NetworkSecurityInfo
    {
        public string SSID { get; set; } = string.Empty;
        public int SignalStrength { get; set; }
        public string SecurityType { get; set; } = string.Empty;
        public SecurityLevel SecurityLevel { get; set; }
        public ThreatLevel ThreatLevel { get; set; }
        public bool IsLikelyHotspot { get; set; }
        public List<string> RiskFactors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        
        public string GetSecurityDescription()
        {
            return SecurityLevel switch
            {
                SecurityLevel.High => "安全",
                SecurityLevel.Medium => "注意",
                SecurityLevel.Low => "危険",
                SecurityLevel.None => "暗号化なし",
                _ => "不明"
            };
        }
        
        public string GetThreatDescription()
        {
            return ThreatLevel switch
            {
                ThreatLevel.Low => "低リスク",
                ThreatLevel.Medium => "中リスク",
                ThreatLevel.High => "高リスク",
                _ => "不明"
            };
        }
    }
    
    public class PasswordSecurityInfo
    {
        public string Password { get; set; } = string.Empty;
        public int Length { get; set; }
        public int Score { get; set; }
        public PasswordStrength Strength { get; set; }
        public List<string> Issues { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();
        
        public string GetStrengthDescription()
        {
            return Strength switch
            {
                PasswordStrength.VeryStrong => "非常に強い",
                PasswordStrength.Strong => "強い",
                PasswordStrength.Medium => "普通",
                PasswordStrength.Weak => "弱い",
                PasswordStrength.VeryWeak => "非常に弱い",
                _ => "不明"
            };
        }
    }
    
    public enum SecurityLevel
    {
        None,    // セキュリティなし
        Low,     // 低セキュリティ
        Medium,  // 中セキュリティ
        High     // 高セキュリティ
    }
    
    public enum ThreatLevel
    {
        Low,     // 低脅威
        Medium,  // 中脅威
        High     // 高脅威
    }
    
    public enum PasswordStrength
    {
        VeryWeak,
        Weak,
        Medium,
        Strong,
        VeryStrong
    }
}