using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// Evil Twin / Rogue AP 検出サービス。
///
/// 学術的背景:
///   Liu &amp; Papadimitratos, "Position-based Rogue Access Point Detection"
///   (KTH, arXiv 2406.01927): AP の位置は固定であり、Evil Twin (同一 SSID で
///   なりすます偽 AP) は正規 AP と異なる物理的特徴を持つ。
///   攻撃者は MAC/SSID を複製できるが、物理層の特徴 (信号源の位置・強度パターン) は
///   完全には模倣しづらい。
///
/// 本サービスはクライアント側で観測可能な特徴のみを用いる:
///   - 同一 SSID で BSSID が突然変わる (履歴との不一致)
///   - 同一 BSSID で信号強度が物理的にありえない急変
///   - BSSID の OUI (ベンダー部) が以前と異なる
///   - 同一 SSID に複数の異なるセキュリティ設定が混在
///
/// CSI/専用ハードウェアは不要 (arXiv 2406.01927 の指摘: CSI ベースは
/// 専用 HW・複雑な設定・高い計算資源を要し実用性が低い)。
/// </summary>
public sealed class EvilTwinDetector
{
    // SSID → 既知の信頼できる BSSID 集合
    private readonly Dictionary<string, HashSet<string>> _knownBssids = new();
    // SSID → 既知のセキュリティ設定
    private readonly Dictionary<string, AuthMethod> _knownAuth = new();
    // SSID → 既知のベンダー (OUI) 集合
    private readonly Dictionary<string, HashSet<string>> _knownVendors = new();
    private readonly OuiLookupService _oui;

    /// <summary>コンストラクタ。OUI ルックアップサービスを注入 (省略時は新規生成)。</summary>
    public EvilTwinDetector(OuiLookupService? oui = null)
    {
        _oui = oui ?? new OuiLookupService();
    }

    /// <summary>
    /// 信頼できる接続を記録する (接続成功時に呼ぶ)。
    /// </summary>
    public void RecordTrusted(string ssid, string bssid, AuthMethod auth)
    {
        if (!_knownBssids.TryGetValue(ssid, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _knownBssids[ssid] = set;
        }
        set.Add(NormalizeBssid(bssid));
        _knownAuth[ssid] = auth;

        // ベンダー (OUI) も記録
        var vendor = _oui.Lookup(bssid);
        if (vendor != null)
        {
            if (!_knownVendors.TryGetValue(ssid, out var vendors))
            {
                vendors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _knownVendors[ssid] = vendors;
            }
            vendors.Add(vendor);
        }
    }

    /// <summary>
    /// スキャン結果から Evil Twin の疑いがある AP を診断する。
    /// </summary>
    public EvilTwinVerdict Analyze(WifiNetwork network, IReadOnlyList<WifiNetwork> allVisible)
    {
        var reasons = new List<string>();
        var ssid = network.Ssid;

        // 1. 同一 SSID に複数の異なるセキュリティ設定 → 古典的 Evil Twin
        var sameSsid = allVisible.Where(n =>
            string.Equals(n.Ssid, ssid, StringComparison.Ordinal)).ToList();
        var distinctAuth = sameSsid.Select(n => n.Auth).Distinct().Count();
        if (distinctAuth > 1)
            reasons.Add($"同一 SSID に {distinctAuth} 種類の異なるセキュリティ設定が混在");

        // 2. 既知 BSSID との不一致 (過去に接続したのに BSSID が違う)
        if (_knownBssids.TryGetValue(ssid, out var known) && known.Count > 0)
        {
            var bssids = network.BssEntries?.Select(b => NormalizeBssid(b.Bssid)) ?? Array.Empty<string>();
            foreach (var b in bssids)
            {
                if (!known.Contains(b))
                {
                    // OUI (先頭3オクテット) が既知と全く異なるか
                    var oui = b.Length >= 8 ? b[..8] : b;
                    bool ouiKnown = known.Any(k => k.StartsWith(oui, StringComparison.OrdinalIgnoreCase));
                    if (!ouiKnown)
                        reasons.Add("以前と異なるベンダー (OUI) の BSSID を検出");
                }
            }
        }

        // 3. 既知のセキュリティ設定からの降格 (WPA3 → Open 等)
        if (_knownAuth.TryGetValue(ssid, out var trustedAuth))
        {
            if (IsSecurityDowngrade(trustedAuth, network.Auth))
                reasons.Add($"既知の {trustedAuth} から {network.Auth} へのセキュリティ降格");
        }

        // 4. オープンネットワークで既知の暗号化 SSID を名乗る
        if (network.Auth == AuthMethod.Open &&
            _knownAuth.TryGetValue(ssid, out var auth2) &&
            auth2 != AuthMethod.Open)
            reasons.Add("既知の暗号化ネットワークがオープンとして出現 (なりすまし濃厚)");

        // 5. ベンダー (OUI) 照合 — 既知と異なるベンダーの機器
        if (_knownVendors.TryGetValue(ssid, out var knownVendors) && knownVendors.Count > 0)
        {
            var bssids2 = network.BssEntries?.Select(b => b.Bssid) ?? Array.Empty<string>();
            foreach (var b in bssids2)
            {
                var vendor = _oui.Lookup(b);
                if (vendor != null && !knownVendors.Contains(vendor))
                    reasons.Add($"既知と異なる機器ベンダー ({vendor}) を検出");
            }
        }

        var risk = reasons.Count switch
        {
            0 => EvilTwinRisk.None,
            1 => EvilTwinRisk.Suspicious,
            _ => EvilTwinRisk.HighRisk
        };

        return new EvilTwinVerdict(risk, reasons);
    }

    /// <summary>
    /// 既知 SSID に対する信頼できる BSSID 一覧を返す。
    /// </summary>
    public IReadOnlyCollection<string> GetTrustedBssids(string ssid)
        => _knownBssids.TryGetValue(ssid, out var set)
            ? set.ToList()
            : Array.Empty<string>();

    // ── Private ─────────────────────────────────────────────────

    private static bool IsSecurityDowngrade(AuthMethod trusted, AuthMethod current)
    {
        int Rank(AuthMethod a) => a switch
        {
            AuthMethod.WPA3Enterprise192 => 6,
            AuthMethod.WPA3Enterprise    => 5,
            AuthMethod.WPA3SAE           => 5,
            AuthMethod.WPA2Enterprise    => 4,
            AuthMethod.WPA2PSK           => 3,
            AuthMethod.OWE               => 3,
            AuthMethod.WPAPSK            => 2,
            AuthMethod.WEP               => 1,
            AuthMethod.Open              => 0,
            _                            => 2
        };
        return Rank(current) < Rank(trusted);
    }

    private static string NormalizeBssid(string bssid)
        => bssid.Replace("-", ":").ToUpperInvariant();
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>Evil Twin 診断結果</summary>
public sealed record EvilTwinVerdict(
    EvilTwinRisk          Risk,
    IReadOnlyList<string> Reasons)
{
    public bool IsSuspect => Risk != EvilTwinRisk.None;
}

/// <summary>Evil Twin リスクレベル</summary>
public enum EvilTwinRisk
{
    /// <summary>疑いなし</summary>
    None,
    /// <summary>要注意 (1つの兆候)</summary>
    Suspicious,
    /// <summary>高リスク (複数の兆候)</summary>
    HighRisk
}
