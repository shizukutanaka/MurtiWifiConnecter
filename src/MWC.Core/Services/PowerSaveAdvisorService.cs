using System;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// 省電力 (TWT/rTWT) 分析・助言サービス。
///
/// 学術的背景:
///   - TWT (Target Wake Time, 802.11ax, arXiv 2402.15900):
///     クライアントは Service Period (SP) 外で doze 状態に入りエネルギーを削減。
///   - TASPER (arXiv 2509.26245): TWT スケジューリング最適化で
///     エネルギー最大34%削減を達成。
///   - rTWT (restricted TWT, Wi-Fi 7): リアルタイムトラフィックに
///     専用 SP を確保し低遅延と省電力を両立。
///
/// 本サービスはバッテリー駆動機器向けに、TWT の省電力効果を推定し
/// スキャン頻度等の運用を助言する。
/// </summary>
public sealed class PowerSaveAdvisorService
{
    /// <summary>
    /// ネットワークの省電力能力を分析する。
    /// </summary>
    public PowerSaveProfile Analyze(WifiNetwork network)
    {
        if (network.RestrictedTwt)
            return new PowerSaveProfile(
                Tier:             PowerSaveTier.Advanced,
                SupportsTwt:      true,
                SupportsRtwt:     true,
                EstimatedSavingPercent: 34,   // TASPER の上限値
                Summary:          "rTWT 対応。リアルタイムトラフィックの低遅延と省電力を両立。最大34%のエネルギー削減。");

        if (network.TargetWakeTime)
            return new PowerSaveProfile(
                Tier:             PowerSaveTier.Standard,
                SupportsTwt:      true,
                SupportsRtwt:     false,
                EstimatedSavingPercent: 20,
                Summary:          "TWT 対応。Service Period 外で doze 状態に入りバッテリーを節約。約20%のエネルギー削減。");

        return new PowerSaveProfile(
            Tier:             PowerSaveTier.Legacy,
            SupportsTwt:      false,
            SupportsRtwt:     false,
            EstimatedSavingPercent: 0,
            Summary:          "TWT 非対応。レガシー省電力 (DTIM/PSM) のみ。");
    }

    /// <summary>
    /// バッテリー駆動時の推奨スキャン間隔 (秒) を返す。
    /// 省電力性が高いほど頻繁にスキャンしても影響が少ない。
    /// </summary>
    public int RecommendedScanIntervalSeconds(WifiNetwork connected, bool onBattery)
    {
        if (!onBattery) return 15;   // AC 電源なら短間隔

        var profile = Analyze(connected);
        return profile.Tier switch
        {
            PowerSaveTier.Advanced => 30,   // rTWT で効率的なので中間隔
            PowerSaveTier.Standard => 60,   // TWT でやや抑制
            _                      => 120   // レガシーは長間隔で節電
        };
    }

    /// <summary>
    /// バッテリー残量に応じた省電力モードの推奨。
    /// </summary>
    public PowerMode RecommendPowerMode(int batteryPercent, bool onBattery)
    {
        if (!onBattery) return PowerMode.Performance;

        return batteryPercent switch
        {
            <= 15 => PowerMode.MaxSaving,    // 緊急
            <= 40 => PowerMode.Balanced,
            _     => PowerMode.Performance
        };
    }

    /// <summary>
    /// IoT/低電力機器向けかどうかを判定する。
    /// </summary>
    public bool IsIotFriendly(WifiNetwork network)
        => network.TargetWakeTime || network.RestrictedTwt;
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>省電力プロファイル</summary>
public sealed record PowerSaveProfile(
    PowerSaveTier Tier,
    bool          SupportsTwt,
    bool          SupportsRtwt,
    int           EstimatedSavingPercent,
    string        Summary);

/// <summary>省電力階層</summary>
public enum PowerSaveTier
{
    /// <summary>レガシー (DTIM/PSM のみ)</summary>
    Legacy,
    /// <summary>標準 (TWT)</summary>
    Standard,
    /// <summary>高度 (rTWT)</summary>
    Advanced
}

/// <summary>電源モード</summary>
public enum PowerMode
{
    /// <summary>性能優先 (AC 電源 / 高残量)</summary>
    Performance,
    /// <summary>バランス</summary>
    Balanced,
    /// <summary>最大省電力 (低残量)</summary>
    MaxSaving
}
