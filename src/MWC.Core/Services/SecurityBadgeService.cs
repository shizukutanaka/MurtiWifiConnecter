using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// 技術用語を人間が理解できる言葉に変換する。
///
/// Apple HIG "Clarity" 原則:
///   "WPA3SAE" は専門家以外に意味を持たない。
///   "最高セキュリティ" は誰でも理解できる。
///
/// 設計: Pure static — テスト容易、依存ゼロ。
/// </summary>
public static class SecurityBadgeService
{
    public static SecurityBadge GetBadge(AuthMethod auth) => auth switch
    {
        AuthMethod.WPA3SAE or AuthMethod.WPA3Enterprise192 or AuthMethod.WPA3Enterprise
            => new SecurityBadge("Maximum Security", SecurityLevel.Excellent, "WPA3"),
        AuthMethod.WPA3Transition
            => new SecurityBadge("High Security", SecurityLevel.Good, "WPA3/2"),
        AuthMethod.WPA2PSK or AuthMethod.WPA2Enterprise
            => new SecurityBadge("Secured", SecurityLevel.Good, "WPA2"),
        AuthMethod.OWE
            => new SecurityBadge("Encrypted", SecurityLevel.Fair, "OWE"),
        AuthMethod.WPAPSK
            => new SecurityBadge("Legacy Encryption", SecurityLevel.Weak, "WPA"),
        AuthMethod.WEP
            => new SecurityBadge("Deprecated", SecurityLevel.Danger, "WEP"),
        AuthMethod.Open
            => new SecurityBadge("No Encryption", SecurityLevel.Danger, "Open"),
        _ => new SecurityBadge("Unknown", SecurityLevel.Weak, auth.ToString())
    };

    /// <summary>信号強度を Apple "Signal bars" 風の言葉に変換</summary>
    public static string GetSignalLabel(int quality) => quality switch
    {
        >= 80 => "Excellent",
        >= 60 => "Good",
        >= 35 => "Fair",
        > 0   => "Weak",
        _     => "None"
    };

    /// <summary>PHY を一般向けラベルに変換(Wi-Fi世代を前面に)</summary>
    public static string GetPhyFriendlyLabel(PhyType phy) => phy switch
    {
        PhyType.Dot11be => "Wi-Fi 7 (Latest)",
        PhyType.Dot11ax => "Wi-Fi 6/6E",
        PhyType.Dot11ac => "Wi-Fi 5",
        PhyType.Dot11n  => "Wi-Fi 4",
        PhyType.Dot11g  => "Wi-Fi 3 (Legacy)",
        PhyType.Dot11b  => "Wi-Fi 1 (Very Old)",
        _ => "Unknown"
    };

}

public enum SecurityLevel { Excellent, Good, Fair, Weak, Danger }

public readonly record struct SecurityBadge(
    string Label,          // 表示テキスト
    SecurityLevel Level,   // 色判定
    string TechLabel       // ツールチップ用技術名
);
