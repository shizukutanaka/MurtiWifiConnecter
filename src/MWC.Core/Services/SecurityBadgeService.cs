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
        AuthMethod.WPA3SAE or AuthMethod.WPA3Enterprise192
            => new SecurityBadge("最高セキュリティ", SecurityLevel.Excellent, "WPA3"),
        AuthMethod.WPA3Transition
            => new SecurityBadge("高セキュリティ", SecurityLevel.Good, "WPA3/2"),
        AuthMethod.WPA2PSK or AuthMethod.WPA2Enterprise or AuthMethod.WPA3Enterprise
            => new SecurityBadge("セキュリティ保護あり", SecurityLevel.Good, "WPA2"),
        AuthMethod.OWE
            => new SecurityBadge("暗号化あり", SecurityLevel.Fair, "OWE"),
        AuthMethod.WPAPSK
            => new SecurityBadge("古い暗号化", SecurityLevel.Weak, "WPA"),
        AuthMethod.WEP
            => new SecurityBadge("非推奨", SecurityLevel.Danger, "WEP"),
        AuthMethod.Open
            => new SecurityBadge("暗号化なし", SecurityLevel.Danger, "Open"),
        _ => new SecurityBadge("不明", SecurityLevel.Weak, auth.ToString())
    };

    /// <summary>信号強度を Apple "Signal bars" 風の言葉に変換</summary>
    public static string GetSignalLabel(int quality) => quality switch
    {
        >= 80 => "優良",
        >= 60 => "良好",
        >= 35 => "普通",
        > 0   => "弱い",
        _     => "圏外"
    };

    /// <summary>PHY を一般向けラベルに変換(Wi-Fi世代を前面に)</summary>
    public static string GetPhyFriendlyLabel(PhyType phy) => phy switch
    {
        PhyType.Dot11be => "Wi-Fi 7 (最新)",
        PhyType.Dot11ax => "Wi-Fi 6/6E",
        PhyType.Dot11ac => "Wi-Fi 5",
        PhyType.Dot11n  => "Wi-Fi 4",
        PhyType.Dot11g  => "Wi-Fi 3 (古い)",
        PhyType.Dot11b  => "Wi-Fi 1 (非常に古い)",
        _ => "不明"
    };

    /// <summary>チャンネル帯域幅を人間語に</summary>
    public static string GetWidthLabel(int mhz) => mhz switch
    {
        320 => "320 MHz (最大速度)",
        160 => "160 MHz (高速)",
        80  => "80 MHz (標準)",
        40  => "40 MHz",
        20  => "20 MHz (標準)",
        0   => "",
        _   => $"{mhz} MHz"
    };
}

public enum SecurityLevel { Excellent, Good, Fair, Weak, Danger }

public readonly record struct SecurityBadge(
    string Label,          // 表示テキスト
    SecurityLevel Level,   // 色判定
    string TechLabel       // ツールチップ用技術名
);
