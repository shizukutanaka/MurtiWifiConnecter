using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// チャネルプランナー — 近隣スキャン結果から「自分の AP をどのチャネルに設定すべきか」を
/// バンド別に推奨する。
///
/// 既存の <see cref="ChannelAdvisorService"/> が「<b>与えられた</b>チャネルの混雑度」を
/// 評価する client 視点なのに対し、本サービスは AP 運用者視点で「<b>全候補から最良の
/// 1 チャネル</b>」を選ぶ逆問題を解く (NetSpot / WiFi Analyzer の「推奨チャネル」相当)。
///
/// 候補集合 (実運用のベストプラクティスに沿う):
///   - 2.4GHz : 非重複の 1 / 6 / 11 のみ
///   - 5GHz   : 既定で非 DFS (UNII-1/UNII-3)。DFS はレーダー検出で突然停止しうるため除外。
///              <paramref name="includeDfs"/>=true で DFS も候補に含める。
///   - 6GHz   : PSC (Preferred Scanning Channel) — 6E/7 クライアントが優先聴取する 15 ch。
///
/// スコアリング (決定論的):
///   候補 c のコスト = Σ_{近隣AP on band} OverlapFactor(c, ap) × SignalWeight(ap)
///     OverlapFactor: 2.4GHz は 5MHz 間隔・20MHz 幅の重なりを階調 (|Δch|/5)、
///                    5/6GHz は同一チャネルが支配的なので co-channel=1.0 を基本とする。
///     SignalWeight : 強い近隣ほど干渉する。SignalQuality(0-100) を 0..1 に正規化。
///   score = round(100 / (1 + cost))   (cost 0 → 100、cost 1 → 50、… 単調減少)
///
/// プラットフォーム非依存・純関数。スキャンや設定変更は行わず推奨のみ。
/// </summary>
public sealed class ChannelPlannerService
{
    // 2.4GHz の非重複チャネル
    private static readonly int[] Candidates24 = { 1, 6, 11 };
    // 5GHz 非 DFS (UNII-1 + UNII-3)。DFS (52–144) は既定で除外。
    private static readonly int[] Candidates5NonDfs =
        { 36, 40, 44, 48, 149, 153, 157, 161, 165 };
    // 5GHz DFS (UNII-2 / UNII-2e) — includeDfs=true のときに追加する。
    private static readonly int[] Candidates5Dfs =
        { 52, 56, 60, 64, 100, 104, 108, 112, 116, 120, 124, 128, 132, 136, 140, 144 };

    /// <summary>
    /// 1 バンドについて、全候補チャネルをスコア順に並べた推奨を返す。
    /// 対象バンドに候補が無い (Unknown 等) 場合は空リスト。
    /// </summary>
    public IReadOnlyList<ChannelScore> RankCandidates(
        WifiBand band, IReadOnlyList<WifiNetwork> visible, bool includeDfs = false)
    {
        ArgumentNullException.ThrowIfNull(visible);

        var candidates = CandidatesFor(band, includeDfs);
        if (candidates.Count == 0) return Array.Empty<ChannelScore>();

        // 対象バンドの近隣 AP のみを抽出 (自分自身の評価対象なので全件＝近隣とみなす)。
        var neighbors = visible.Where(n => n.Band == band).ToList();

        var scored = new List<ChannelScore>(candidates.Count);
        foreach (var c in candidates)
        {
            double cost = 0;
            int competing = 0;
            foreach (var ap in neighbors)
            {
                // チャネル不明 (0 以下) の AP は干渉計算から除外する。
                // 含めると |候補-0| が小さい低番号候補に偽の干渉が乗る。
                if (ap.Channel <= 0) continue;

                double overlap = OverlapFactor(band, c, ap.Channel, ap.ChannelWidth);
                if (overlap <= 0) continue;
                competing++;
                cost += overlap * SignalWeight(ap.SignalQuality);
            }
            int score = (int)Math.Round(100.0 / (1.0 + cost));
            scored.Add(new ChannelScore(
                Channel:          c,
                Score:            score,
                CompetingApCount: competing,
                IsDfs:            DfsChannelHelper.IsDfsChannel(band, c)));
        }

        // スコア降順 → 同点はチャネル番号昇順 (決定論的)。
        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Channel)
            .ToList();
    }

    /// <summary>
    /// 1 バンドの最良チャネル推奨 (根拠つき)。候補が無ければ null。
    /// </summary>
    public ChannelRecommendation? Recommend(
        WifiBand band, IReadOnlyList<WifiNetwork> visible, bool includeDfs = false)
    {
        var ranked = RankCandidates(band, visible, includeDfs);
        if (ranked.Count == 0) return null;

        var best = ranked[0];
        return new ChannelRecommendation(
            Band:               band,
            RecommendedChannel: best.Channel,
            Score:              best.Score,
            CompetingApCount:   best.CompetingApCount,
            IsDfs:              best.IsDfs,
            Reason:             BuildReason(band, best),
            Ranked:             ranked);
    }

    /// <summary>
    /// 利用可能な全バンド (2.4 / 5 / 6GHz) の推奨をまとめて返す。
    /// </summary>
    public IReadOnlyList<ChannelRecommendation> RecommendAllBands(
        IReadOnlyList<WifiNetwork> visible, bool includeDfs = false)
    {
        var result = new List<ChannelRecommendation>(3);
        foreach (var band in new[] { WifiBand.Band2_4GHz, WifiBand.Band5GHz, WifiBand.Band6GHz })
        {
            var rec = Recommend(band, visible, includeDfs);
            if (rec is not null) result.Add(rec);
        }
        return result;
    }

    // ── Private ─────────────────────────────────────────────────

    private IReadOnlyList<int> CandidatesFor(WifiBand band, bool includeDfs) => band switch
    {
        WifiBand.Band2_4GHz => Candidates24,
        WifiBand.Band5GHz   => includeDfs
                                   ? Candidates5NonDfs.Concat(Candidates5Dfs).OrderBy(c => c).ToArray()
                                   : Candidates5NonDfs,
        WifiBand.Band6GHz   => SixGhzChannelHelper.PreferredScanningChannels,
        _                   => Array.Empty<int>(),
    };

    /// <summary>
    /// 候補チャネル c と近隣 AP (チャネル apCh, 幅 apWidthMhz) の重なり係数 (0..1)。
    /// 2.4GHz: 5MHz 間隔・20MHz 幅で |Δch| &lt; 5 が重なる → 1 - |Δch|/5。
    /// 5/6GHz: 20MHz 非重複だが、近隣がワイドチャネル (40/80/160/320MHz) だと占有範囲が
    ///         広がる。チャネル番号は 20MHz あたり 4 刻みなので、幅 W の AP は
    ///         (W/20) 個の 20MHz スロット = 中心から ±reach ch に及ぶ (reach=(slots-1)×4)。
    ///         primary 位置が (primary,width) からは一意に定まらないため対称近似で評価する。
    /// </summary>
    private static double OverlapFactor(WifiBand band, int c, int apCh, int apWidthMhz)
    {
        int delta = Math.Abs(c - apCh);
        if (band == WifiBand.Band2_4GHz)
            return delta < 5 ? 1.0 - delta / 5.0 : 0.0;

        // 5GHz / 6GHz — ワイドチャネルの占有を考慮する。
        int slots = Math.Max(1, apWidthMhz / 20);   // 20→1, 40→2, 80→4, 160→8, 320→16
        int reach = (slots - 1) * 4;                 // 20→0, 40→4, 80→12, 160→28, 320→60
        if (delta == 0)           return 1.0;        // co-channel
        if (delta <= reach)       return 0.6;        // ワイドチャネルのサブチャネル重なり
        if (delta == reach + 4)   return 0.3;        // 直近の隣接 20MHz ブロック
        return 0.0;
    }

    /// <summary>SignalQuality(0-100) を干渉重み 0..1 に正規化。強い近隣ほど重い。</summary>
    private static double SignalWeight(int signalQuality)
        => Math.Clamp(signalQuality, 0, 100) / 100.0;

    private static string BuildReason(WifiBand band, ChannelScore best)
    {
        string bandLabel = band switch
        {
            WifiBand.Band2_4GHz => "2.4 GHz",
            WifiBand.Band5GHz   => "5 GHz",
            WifiBand.Band6GHz   => "6 GHz",
            _                   => "?"
        };
        if (best.CompetingApCount == 0)
            return $"{bandLabel} ch {best.Channel}: no overlapping neighbors detected — clear channel.";

        string dfs = best.IsDfs
            ? " (DFS — may pause briefly on radar detection)"
            : "";
        return $"{bandLabel} ch {best.Channel}: least interference of the candidates " +
               $"({best.CompetingApCount} overlapping neighbor(s), cleanliness {best.Score}/100){dfs}.";
    }
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>1 候補チャネルの評価。</summary>
public sealed record ChannelScore(
    int  Channel,
    int  Score,            // 0-100、高いほどクリーン
    int  CompetingApCount, // この候補と重なる近隣 AP 数
    bool IsDfs);

/// <summary>1 バンドの最良チャネル推奨 (候補ランキングつき)。</summary>
public sealed record ChannelRecommendation(
    WifiBand                  Band,
    int                       RecommendedChannel,
    int                       Score,
    int                       CompetingApCount,
    bool                      IsDfs,
    string                    Reason,
    IReadOnlyList<ChannelScore> Ranked);
