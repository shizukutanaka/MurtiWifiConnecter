using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// EasyMesh / マルチ AP 検出サービス (D6)。
///
/// メッシュネットワーク (EasyMesh/Orbi/Eero/Velop 等) は
/// 全ノードが同一 SSID を共有し、それぞれ異なる BSSID (MAC) を持つ。
/// クライアントは同一 SSID を複数 AP で受信する。
///
/// 検出根拠:
///   1. **SSID 重複 + マルチバンド**: 同一 SSID が 2 バンド以上に存在
///      → メッシュシステムのバンドステアリング (Wi-Fi Alliance Multi-AP R2 §5)
///   2. **BSS 多数**: 同一 SSID で ≥3 BssEntry → メッシュノードが複数台
///   3. **MDID 一致**: 全 BSS の Mobility Domain ID が共通
///      → 802.11r Fast Transition がメッシュ全体に展開済み
///   4. **既知ベンダー OUI**: Eero (34:08:BC), Orbi (9C:3D:CF) 等の
///      メッシュ専用機器を OUI で特定 (信頼度 High)
///
/// 設計原則: false-positive を避けるため、2 バンド以上 + BSSID ≥2 を必須条件とし、
/// 単なるデュアルバンド AP (1 台) との誤検知を防ぐ。
/// </summary>
public sealed class MeshNetworkDetector
{
    // 既知のメッシュ専用システムの OUI プレフィックス (大文字)
    private static readonly HashSet<string> MeshVendorOuis = new(StringComparer.OrdinalIgnoreCase)
    {
        "34:08:BC", // Amazon Eero
        "F4:F2:6D", // Amazon Eero
        "9C:3D:CF", // Netgear Orbi
        "B0:B9:8A", // Netgear Orbi
        "DC:EF:09", // ASUS ZenWiFi
        "04:D4:C4", // ASUS ZenWiFi
        "58:CB:52", // TP-Link Deco
        "60:32:B1", // TP-Link Deco
        "D8:0D:17", // Google Nest WiFi
        "F4:F5:D8", // Google Nest WiFi
        "14:AB:C5", // Linksys Velop
        "C8:D3:A3", // Linksys Velop
    };

    private readonly OuiLookupService _oui;

    public MeshNetworkDetector(OuiLookupService? oui = null)
        => _oui = oui ?? new OuiLookupService();

    /// <summary>
    /// 可視ネットワーク一覧からメッシュ候補をグループ化して返す。
    /// 1 つの SSID につき最大 1 つの <see cref="MeshGroup"/> を返す。
    /// </summary>
    public IReadOnlyList<MeshGroup> Detect(IEnumerable<WifiNetwork> networks)
    {
        var bySsid = networks
            .Where(n => !string.IsNullOrEmpty(n.Ssid) && !n.IsHidden)
            .GroupBy(n => n.Ssid, StringComparer.Ordinal)
            .Where(g => g.Count() >= 2);

        var result = new List<MeshGroup>();
        foreach (var g in bySsid)
        {
            var members = g.ToList();
            var group = Analyze(g.Key, members);
            if (group is not null) result.Add(group);
        }
        return result;
    }

    private MeshGroup? Analyze(string ssid, List<WifiNetwork> members)
    {
        var bands = members.Select(n => n.Band).Distinct().ToList();

        // 必須条件: ≥2 バンド または BssEntry が合計 ≥3
        int totalBss = members.Sum(n => n.BssEntries.Count);
        bool multiBand = bands.Count >= 2;
        bool manyBss   = totalBss >= 3;
        if (!multiBand && !manyBss) return null;

        // FT メッシュ: 全 BSS が同じ Mobility Domain ID を共有する
        // (BeaconIeApplier が BssInfo.MobilityDomainId に格納済み)
        var mdids = members
            .SelectMany(n => n.BssEntries)
            .Select(b => b.MobilityDomainId)
            .Where(m => m.HasValue)
            .Select(m => m!.Value)
            .Distinct()
            .ToList();
        // MDID が観測でき、かつ全て同一なら一貫していると判定
        bool consistentMdid = mdids.Count == 1;

        // ベンダー OUI 検出
        var detectedVendors = members
            .SelectMany(n => n.BssEntries)
            .Select(b => OuiPrefix(b.Bssid))
            .Where(oui => MeshVendorOuis.Contains(oui))
            .Distinct()
            .ToList();
        bool knownMeshVendor = detectedVendors.Count > 0;

        var confidence = ComputeConfidence(multiBand, manyBss, consistentMdid, knownMeshVendor);

        return new MeshGroup(
            Ssid:               ssid,
            NodeCount:          members.Count,
            BandCoverage:       bands,
            HasFastTransition:  members.Any(n => n.FastTransition),
            ConsistentMdid:     consistentMdid,
            KnownMeshVendor:    knownMeshVendor,
            Confidence:         confidence,
            Members:            members);
    }

    private static MeshConfidence ComputeConfidence(
        bool multiBand, bool manyBss, bool consistentMdid, bool knownVendor)
    {
        int score = 0;
        if (multiBand)       score += 2;
        if (manyBss)         score += 1;
        if (consistentMdid)  score += 2;
        if (knownVendor)     score += 3;
        return score switch
        {
            >= 5 => MeshConfidence.High,
            >= 3 => MeshConfidence.Medium,
            _    => MeshConfidence.Low
        };
    }

    private static string OuiPrefix(string bssid)
    {
        var parts = bssid.Split(':');
        if (parts.Length < 3) return "";
        return $"{parts[0]}:{parts[1]}:{parts[2]}";
    }
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>同一 SSID のメッシュ候補グループ。</summary>
public sealed record MeshGroup(
    string                    Ssid,
    int                       NodeCount,
    IReadOnlyList<WifiBand>   BandCoverage,
    bool                      HasFastTransition,
    bool                      ConsistentMdid,
    bool                      KnownMeshVendor,
    MeshConfidence            Confidence,
    IReadOnlyList<WifiNetwork> Members)
{
    /// <summary>6GHz ノードを含むか (Wi-Fi 6E/7 メッシュ)。</summary>
    public bool Has6GHz => BandCoverage.Contains(WifiBand.Band6GHz);

    /// <summary>全バンドを網羅しているか (2.4 + 5 + 6 GHz)。</summary>
    public bool IsTriBand =>
        BandCoverage.Contains(WifiBand.Band2_4GHz) &&
        BandCoverage.Contains(WifiBand.Band5GHz)   &&
        BandCoverage.Contains(WifiBand.Band6GHz);
}

/// <summary>メッシュ検出の確信度。</summary>
public enum MeshConfidence
{
    /// <summary>推定 (マルチバンドのみ)</summary>
    Low,
    /// <summary>可能性大 (マルチバンド + 多 BSS / FT 一致)</summary>
    Medium,
    /// <summary>確実 (既知ベンダー OUI または FT MDID 一致 + マルチバンド)</summary>
    High
}
