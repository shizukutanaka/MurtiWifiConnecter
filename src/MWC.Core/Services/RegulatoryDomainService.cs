using System.Collections.Frozen;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MWC.Core.Services;

/// <summary>
/// Wi-Fi 規制ドメイン — 国別の使用可能チャネルを提供。
/// 主に 6GHz 帯で国ごとに使用可能チャネルが大きく異なるため。
///
/// 設計:
///   - ゼロ外部依存 (静的テーブル)
///   - 6GHz の PSC (Preferred Scanning Channel) を区別
///   - 国コード → チャネルセットの O(1) 解決
///   - 未知の国コードは「最も制限的」にフォールバック
/// </summary>
public sealed class RegulatoryDomainService
{
    // ── 6GHz チャネル定義 ───────────────────────────────────────────
    // IEEE 802.11ax-2021 Annex E より
    // PSC (Preferred Scanning Channel): チャネル 5, 21, 37, 53, 69, 85, 101, 117, 133, 149, 165, 181, 197, 213, 229
    private static readonly int[] All6GHzChannels = Enumerable.Range(0, 59)
        .Select(i => i * 4 + 1)   // 1, 5, 9, 13, … 233
        .Where(ch => ch <= 233)
        .ToArray();

    private static readonly HashSet<int> PscChannels = new()
        { 5, 21, 37, 53, 69, 85, 101, 117, 133, 149, 165, 181, 197, 213, 229 };

    // ── 規制テーブル ─────────────────────────────────────────────────
    // (国コード 2文字: ISO 3166-1 alpha-2)
    // .NET 9: FrozenDictionary でルックアップ性能を最大化(読み取り専用の静的データに最適)
    private static readonly FrozenDictionary<string, RegulatoryRegion> Regions =
        new Dictionary<string, RegulatoryRegion>(StringComparer.OrdinalIgnoreCase)
    {
        // 米国: 全 6GHz 帯 (5.925–7.125 GHz) + 6E 対応
        ["US"] = new("US", "United States", Band6GHzMode.FullBand,
            lowPowerIndoor: true, veryLowPower: true, standardPower: true),

        // EU 全体 (一例として DE/FR/GB も同値)
        ["EU"] = new("EU", "European Union", Band6GHzMode.LowerHalf,
            lowPowerIndoor: true, veryLowPower: true, standardPower: false),
        ["DE"] = new("DE", "Germany",        Band6GHzMode.LowerHalf, true,  true,  false),
        ["FR"] = new("FR", "France",         Band6GHzMode.LowerHalf, true,  true,  false),
        ["GB"] = new("GB", "United Kingdom", Band6GHzMode.LowerHalf, true,  true,  false),
        ["IT"] = new("IT", "Italy",          Band6GHzMode.LowerHalf, true,  true,  false),
        ["ES"] = new("ES", "Spain",          Band6GHzMode.LowerHalf, true,  true,  false),
        ["NL"] = new("NL", "Netherlands",    Band6GHzMode.LowerHalf, true,  true,  false),
        ["SE"] = new("SE", "Sweden",         Band6GHzMode.LowerHalf, true,  true,  false),
        ["NO"] = new("NO", "Norway",         Band6GHzMode.LowerHalf, true,  true,  false),
        ["CH"] = new("CH", "Switzerland",    Band6GHzMode.LowerHalf, true,  true,  false),

        // 日本: 6GHz 全帯域 (2022年 Wi-Fi 6E 解禁)
        ["JP"] = new("JP", "Japan",          Band6GHzMode.FullBand,  true,  false, false),

        // 韓国
        ["KR"] = new("KR", "South Korea",   Band6GHzMode.FullBand,  true,  false, false),

        // 中国: 現時点で 6GHz 未認可 → 5GHz のみ
        ["CN"] = new("CN", "China",          Band6GHzMode.None,      false, false, false),

        // オーストラリア: 全帯域
        ["AU"] = new("AU", "Australia",      Band6GHzMode.FullBand,  true,  true,  true),

        // カナダ: 米国と同等
        ["CA"] = new("CA", "Canada",         Band6GHzMode.FullBand,  true,  true,  true),

        // ブラジル
        ["BR"] = new("BR", "Brazil",         Band6GHzMode.FullBand,  true,  true,  true),

        // インド: 6GHz 未認可
        ["IN"] = new("IN", "India",          Band6GHzMode.None,      false, false, false),

        // ロシア: 6GHz 未認可
        ["RU"] = new("RU", "Russia",         Band6GHzMode.None,      false, false, false),

        // サウジアラビア: Lower Half
        ["SA"] = new("SA", "Saudi Arabia",   Band6GHzMode.LowerHalf, true,  false, false),

        // UAE: Lower Half
        ["AE"] = new("AE", "UAE",            Band6GHzMode.LowerHalf, true,  false, false),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// 現在のシステムロケールから規制ドメインを推定。
    /// </summary>
    public RegulatoryRegion DetectCurrentRegion()
    {
        var region = RegionInfo.CurrentRegion.TwoLetterISORegionName;
        return GetRegion(region);
    }

    /// <summary>
    /// 国コード (ISO 3166-1 alpha-2) から規制ドメインを取得。
    /// 未知の国は None (最も制限的) にフォールバック。
    /// </summary>
    /// <summary>ISO 3166-1 alpha-2 国コードから規制ドメイン情報を取得する。未知の国は None (最制限)。</summary>
    public RegulatoryRegion GetRegion(string countryCode)
        => Regions.TryGetValue(countryCode, out var r)
            ? r
            : new(countryCode, $"Unknown ({countryCode})", Band6GHzMode.None, false, false, false);

    /// <summary>
    /// 国コードの規制ドメインで使用可能な 6GHz チャネル一覧。
    /// </summary>
    public IReadOnlyList<ChannelInfo> GetAvailable6GHzChannels(string countryCode)
    {
        var region = GetRegion(countryCode);
        return GetAvailable6GHzChannels(region);
    }

    /// <summary>
    /// 規制ドメインに対応した 6GHz チャネル一覧(周波数・PSC・帯域幅情報付き)。
    /// </summary>
    public IReadOnlyList<ChannelInfo> GetAvailable6GHzChannels(RegulatoryRegion region)
    {
        if (region.Mode == Band6GHzMode.None) return Array.Empty<ChannelInfo>();

        // Lower Half: チャネル 1–93 (5.925–6.425 GHz)
        // Full Band:  チャネル 1–233 (5.925–7.125 GHz)
        int maxChannel = region.Mode == Band6GHzMode.LowerHalf ? 93 : 233;

        return All6GHzChannels
            .Where(ch => ch <= maxChannel)
            .Select(ch => new ChannelInfo(
                Channel:       ch,
                FrequencyMhz:  5950 + ch * 5,          // 6GHz: ch1=5955MHz (SixGhzChannelHelper と一致)
                IsPsc:         PscChannels.Contains(ch),
                MaxWidthMhz:   MaxChannelWidth(ch, maxChannel)))
            .ToList();
    }

    /// <summary>
    /// 指定チャネルがスキャン対象の国で使用合法かどうか。
    /// </summary>
    public bool IsChannelLegal(int channel, string countryCode)
    {
        var region = GetRegion(countryCode);
        if (region.Mode == Band6GHzMode.None) return false;
        int maxCh = region.Mode == Band6GHzMode.LowerHalf ? 93 : 233;
        return All6GHzChannels.Contains(channel) && channel <= maxCh;
    }

    /// <summary>
    /// PSC (Preferred Scanning Channel) かどうか。
    /// Wi-Fi 6E デバイスはパッシブスキャン時にまず PSC を聴取する。
    /// </summary>
    public bool IsPreferredScanChannel(int channel) => PscChannels.Contains(channel);

    /// <summary>
    /// 全規制ドメイン一覧。
    /// </summary>
    public IReadOnlyCollection<RegulatoryRegion> AllRegions
        => (IReadOnlyCollection<RegulatoryRegion>)Regions.Values;

    // ── Private ───────────────────────────────────────────────────────

    private static int MaxChannelWidth(int channel, int maxChannel)
    {
        // 6GHz チャネルは 4 刻み (1, 5, 9 …)。N MHz ブロックは (N/20) sub-ch からなり、
        // 先頭 ch から最後 sub-ch までのスパンは 4*(N/20 - 1)。
        // 例: 320MHz = 16 sub-ch → スパン = 4*15 = 60 → 最後 sub-ch = channel + 60
        if (channel + 60 <= maxChannel) return 320;
        if (channel + 28 <= maxChannel) return 160;
        if (channel + 12 <= maxChannel) return 80;
        if (channel + 4  <= maxChannel) return 40;
        return 20;
    }
}

// ── データ型 ───────────────────────────────────────────────────────────

/// <summary>6GHz 帯の利用可否モード</summary>
public enum Band6GHzMode
{
    /// <summary>6GHz 未対応/未認可</summary>
    None,
    /// <summary>Lower half のみ (5.925–6.425 GHz, ch 1–93)</summary>
    LowerHalf,
    /// <summary>全帯域 (5.925–7.125 GHz, ch 1–233)</summary>
    FullBand
}

/// <summary>国別規制ドメイン情報</summary>
public sealed record RegulatoryRegion(
    string       CountryCode,
    string       CountryName,
    Band6GHzMode Mode,
    bool         LowPowerIndoor,  // LPI (室内低出力)
    bool         VeryLowPower,    // VLP (超低出力)
    bool         StandardPower    // SP (標準出力, 米国/豪州等)
)
{
    /// <summary>6GHz を何らかの形で使用可能か</summary>
    public bool Has6GHz => Mode != Band6GHzMode.None;
}

/// <summary>個別チャネル情報</summary>
public sealed record ChannelInfo(
    int  Channel,
    int  FrequencyMhz,
    bool IsPsc,
    int  MaxWidthMhz)
{
    /// <summary>周波数 GHz 表示用</summary>
    public double FrequencyGHz => FrequencyMhz / 1000.0;
}
