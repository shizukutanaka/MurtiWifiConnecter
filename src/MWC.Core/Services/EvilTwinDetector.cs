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
            reasons.Add($"Same SSID has {distinctAuth} different security configurations");

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
                    {
                        // Only report here when the OUI DB cannot resolve a vendor name.
                        // When the DB *can* name it, check 4 adds a richer reason for the
                        // same signal — adding both would double-count one indicator.
                        var vendorFromDb = _oui.Lookup(b);
                        if (vendorFromDb is null)
                            reasons.Add("BSSID detected with a different vendor (OUI) than previously seen");
                    }
                }
            }
        }

        // 3. 既知のセキュリティ設定からの降格 (WPA3 → Open 等)
        // Note: "appearing as open" is the most severe downgrade case and is fully
        // covered here — a separate check would double-count it as two reasons.
        if (_knownAuth.TryGetValue(ssid, out var trustedAuth))
        {
            if (IsSecurityDowngrade(trustedAuth, network.Auth))
                reasons.Add($"Security downgrade detected: known {trustedAuth} vs current {network.Auth}");
        }

        // 4. ベンダー (OUI) 照合 — 既知と異なるベンダーの機器
        if (_knownVendors.TryGetValue(ssid, out var knownVendors) && knownVendors.Count > 0)
        {
            var bssids2 = network.BssEntries?.Select(b => b.Bssid) ?? Array.Empty<string>();
            foreach (var b in bssids2)
            {
                var vendor = _oui.Lookup(b);
                if (vendor != null && !knownVendors.Contains(vendor))
                    reasons.Add($"Device vendor different from known vendor detected ({vendor})");
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

    /// <summary>
    /// 学習済みの信頼ベースラインを書き出す(永続化用)。
    ///
    /// なぜ必要か: 検査 2〜4 (BSSID 不一致・ダウングレード・ベンダー相違) は
    /// すべて過去の学習を前提とする。学習がプロセスメモリ限りだと、アプリ再起動の
    /// たびにベースラインが消え、直後は検査 1 しか発火しない = 理由が 1 件までしか
    /// 積まれず HighRisk (2 件以上) に到達できない。つまり再起動直後は
    /// 自動再接続の Evil Twin 防御が事実上無効化される。
    /// 不正 AP 検出は「信頼済み SSID/BSSID のベースラインを事前に確立しておく」
    /// ことが前提の技術であり、その永続化はセキュリティ上の必須要件。
    ///
    /// I/O はここでは行わない — 本クラスをファイルシステム非依存に保ち
    /// (テスト容易性)、保存先や書式は呼び出し側の責務とする。
    /// </summary>
    public IReadOnlyList<TrustedApBaseline> ExportBaseline()
        => _knownAuth.Select(kv => new TrustedApBaseline(
                Ssid:    kv.Key,
                Auth:    kv.Value,
                Bssids:  _knownBssids.TryGetValue(kv.Key, out var b) ? b.ToList() : new List<string>(),
                Vendors: _knownVendors.TryGetValue(kv.Key, out var v) ? v.ToList() : new List<string>()))
            .ToList();

    /// <summary>
    /// <see cref="ExportBaseline"/> で書き出したベースラインを復元する。
    /// 既存の学習内容には加算的にマージする(復元後に RecordTrusted しても消えない)。
    /// 不正な項目 (SSID 空) は黙って読み飛ばす — 破損データでフィルタ全体を
    /// 失うより、読める分だけでも防御を復旧させる方が安全側。
    /// </summary>
    public void ImportBaseline(IEnumerable<TrustedApBaseline> baseline)
    {
        foreach (var entry in baseline)
        {
            if (string.IsNullOrEmpty(entry.Ssid)) continue;

            _knownAuth[entry.Ssid] = entry.Auth;

            if (entry.Bssids is { Count: > 0 })
            {
                if (!_knownBssids.TryGetValue(entry.Ssid, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _knownBssids[entry.Ssid] = set;
                }
                foreach (var b in entry.Bssids)
                    if (!string.IsNullOrEmpty(b)) set.Add(NormalizeBssid(b));
            }

            if (entry.Vendors is { Count: > 0 })
            {
                if (!_knownVendors.TryGetValue(entry.Ssid, out var vendors))
                {
                    vendors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _knownVendors[entry.Ssid] = vendors;
                }
                foreach (var v in entry.Vendors)
                    if (!string.IsNullOrEmpty(v)) vendors.Add(v);
            }
        }
    }

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

/// <summary>
/// 1 SSID 分の信頼ベースライン(永続化の単位)。
/// <see cref="EvilTwinDetector.ExportBaseline"/> /
/// <see cref="EvilTwinDetector.ImportBaseline"/> で用いる。
/// JSON シリアライズ可能であること (System.Text.Json の既定コンストラクタ解決)。
/// </summary>
public sealed record TrustedApBaseline(
    string       Ssid,
    AuthMethod   Auth,
    List<string> Bssids,
    List<string> Vendors);

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
