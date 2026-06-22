using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// ハンドオーバー (AP 切替) 予測サービス。
///
/// 既存の SignalQualityPredictor (信号トレンド予測) と
/// RoamingAdvisoryService (ローミング能力) を統合し、
/// 「いつ別の AP に切り替えるべきか」を判断する。
///
/// 検出する問題:
///   - スティッキークライアント: 信号が悪化しても遠方 AP に固執する
///   - フラッピング: 2つの AP 間で頻繁に往復する (ピンポン)
///
/// 学術的背景:
///   信号トレンドの予測 (arXiv 2509.18933) とローミング標準 (802.11k/v) を
///   組み合わせ、悪化を予測して事前にローミングを促す。
/// </summary>
public sealed class HandoverPredictor
{
    // ローミング判断のしきい値 (RSSI, dBm)
    private const int StickyThresholdDbm    = -75;   // これ以下で固執は問題
    private const int GoodSignalDbm         = -60;   // これ以上なら切替不要
    private const int FlapWindowSeconds     = 30;    // この時間内の複数切替=フラッピング
    private const int FlapCountThreshold    = 3;     // この回数でフラッピング判定

    private readonly List<HandoverEvent> _history = new();
    private readonly object _histLock = new();

    /// <summary>
    /// 現在の接続を評価し、ハンドオーバーすべきか判断する。
    /// </summary>
    /// <param name="currentRssi">現在接続中 AP の RSSI</param>
    /// <param name="predictedRssi">SignalQualityPredictor による予測 RSSI</param>
    /// <param name="trend">信号トレンド</param>
    /// <param name="candidate">候補 AP (より良い AP があれば)</param>
    public HandoverRecommendation Evaluate(
        int currentRssi,
        double? predictedRssi,
        SignalTrend trend,
        WifiNetwork? candidate = null)
    {
        // 信号が十分強い → 切替不要
        if (currentRssi >= GoodSignalDbm && trend != SignalTrend.Degrading)
            return new HandoverRecommendation(
                ShouldHandover: false,
                Reason:         "Current signal is good. No handover needed.",
                Urgency:        HandoverUrgency.None);

        // 悪化傾向 + 候補あり → 事前ローミング推奨
        bool degrading = trend == SignalTrend.Degrading ||
                         (predictedRssi is { } p && p < currentRssi - 5);

        if (degrading && candidate != null && candidate.SignalQuality > 50)
            return new HandoverRecommendation(
                ShouldHandover: true,
                Reason:         "Signal degradation predicted. Proactive roaming to stronger candidate AP recommended.",
                Urgency:        currentRssi < StickyThresholdDbm
                                    ? HandoverUrgency.High
                                    : HandoverUrgency.Medium,
                Candidate:      candidate);

        // 信号が弱いが候補なし → スティッキー状態の警告
        if (currentRssi < StickyThresholdDbm && candidate == null)
            return new HandoverRecommendation(
                ShouldHandover: false,
                Reason:         "Weak signal but no handover candidate available. Move closer to an AP or rescan.",
                Urgency:        HandoverUrgency.Low);

        return new HandoverRecommendation(
            ShouldHandover: false,
            Reason:         "No issues with current connection. No action needed.",
            Urgency:        HandoverUrgency.None);
    }

    /// <summary>
    /// ハンドオーバーイベントを記録する (フラッピング検出用)。
    /// </summary>
    public void RecordHandover(string fromBssid, string toBssid, DateTimeOffset when)
    {
        lock (_histLock)
        {
            _history.Add(new HandoverEvent(fromBssid, toBssid, when));
            var cutoff = when.AddMinutes(-5);
            _history.RemoveAll(e => e.When < cutoff);
        }
    }

    /// <summary>
    /// スティッキークライアント状態を判定する。
    /// 信号が弱いのに長時間同じ AP に留まっている。
    /// </summary>
    public bool IsStickyClient(int currentRssi, TimeSpan connectedDuration)
        => currentRssi < StickyThresholdDbm && connectedDuration > TimeSpan.FromMinutes(2);

    /// <summary>
    /// フラッピング (ピンポンローミング) を検出する。
    /// 短時間に同じ AP ペア間で複数回往復している。
    /// </summary>
    public FlappingVerdict DetectFlapping(DateTimeOffset now)
    {
        var window = now.AddSeconds(-FlapWindowSeconds);
        List<HandoverEvent> recent;
        lock (_histLock) { recent = _history.Where(e => e.When >= window).ToList(); }

        if (recent.Count < FlapCountThreshold)
            return new FlappingVerdict(false, recent.Count, "");

        // 同一 AP ペア間の往復をカウント
        var pairs = recent
            .Select(e => NormalizePair(e.FromBssid, e.ToBssid))
            .GroupBy(p => p)
            .Where(g => g.Count() >= FlapCountThreshold);

        var flappingPair = pairs.FirstOrDefault();
        if (flappingPair != null)
            return new FlappingVerdict(
                IsFlapping: true,
                RecentHandovers: recent.Count,
                Detail: $"Detected {flappingPair.Count()} back-and-forth handovers within {FlapWindowSeconds} seconds. " +
                        "Recommend adjusting roaming threshold.");

        return new FlappingVerdict(false, recent.Count, "");
    }

    /// <summary>記録されたハンドオーバー履歴数。</summary>
    public int HistoryCount { get { lock (_histLock) { return _history.Count; } } }

    // ── Private ─────────────────────────────────────────────────

    private static string NormalizePair(string a, string b)
    {
        // 方向を無視したペアキー (A→B と B→A を同一視)
        var x = a.ToUpperInvariant();
        var y = b.ToUpperInvariant();
        return string.CompareOrdinal(x, y) <= 0 ? $"{x}|{y}" : $"{y}|{x}";
    }

    private readonly record struct HandoverEvent(
        string FromBssid, string ToBssid, DateTimeOffset When);
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>ハンドオーバー推奨</summary>
public sealed record HandoverRecommendation(
    bool            ShouldHandover,
    string          Reason,
    HandoverUrgency Urgency,
    WifiNetwork?    Candidate = null);

/// <summary>ハンドオーバーの緊急度</summary>
public enum HandoverUrgency
{
    None, Low, Medium, High
}

/// <summary>フラッピング判定</summary>
public sealed record FlappingVerdict(
    bool   IsFlapping,
    int    RecentHandovers,
    string Detail);
