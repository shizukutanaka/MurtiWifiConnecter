using System.Collections.Generic;

namespace MWC.Core.Services;

/// <summary>
/// QoS / トラフィック適性の助言サービス。
///
/// AP の WMM 設定 (<see cref="WmmParameters"/>) と実測 bufferbloat
/// (<see cref="ResponsivenessResult"/>) を統合し、アプリ用途ごとの
/// 適性を助言する (D5-4 QoS×bufferbloat 統合, D5-7 アプリ別感度)。
///
/// 背景:
///   - bufferbloat (負荷時のレイテンシ増大) はリアルタイム用途を破壊する。
///     ゲーム/ビデオ会議は増分 &lt; 30ms (グレード A) を要する。
///   - WMM (802.11e EDCA) が有効なら Voice/Video AC が優先され、
///     上り輻輳下でもリアルタイムフレームの遅延が抑えられる。
///   - WMM 無効の AP では全トラフィックが Best Effort 扱いとなり、
///     bufferbloat の影響をまともに受ける。
///
/// 本サービスは計測・接続を行わず、与えられたデータからの純粋な助言のみ。
/// </summary>
public sealed class QosAdvisoryService
{
    /// <summary>
    /// 用途ごとの適性を評価する。
    /// </summary>
    /// <param name="responsiveness">実測の負荷時遅延 (null = 未計測)</param>
    /// <param name="wmm">AP の WMM パラメータ (null = WMM 非対応/未取得)</param>
    public IReadOnlyList<AppSuitability> Evaluate(
        ResponsivenessResult? responsiveness, WmmParameters? wmm)
    {
        bool wmmActive = wmm is not null;
        var grade = responsiveness?.Grade ?? BufferbloatGrade.Unknown;

        return new[]
        {
            Assess(AppClass.RealtimeGaming,   grade, wmmActive, requiredGrade: BufferbloatGrade.B),
            Assess(AppClass.VideoConferencing, grade, wmmActive, requiredGrade: BufferbloatGrade.B),
            Assess(AppClass.VideoStreaming,   grade, wmmActive, requiredGrade: BufferbloatGrade.D),
            Assess(AppClass.WebBrowsing,      grade, wmmActive, requiredGrade: BufferbloatGrade.F),
        };
    }

    private static AppSuitability Assess(
        AppClass app, BufferbloatGrade grade, bool wmmActive, BufferbloatGrade requiredGrade)
    {
        // 計測がなければ判定不能
        if (grade == BufferbloatGrade.Unknown)
            return new AppSuitability(app, SuitabilityLevel.Unknown, wmmActive,
                Reason: "負荷時遅延が未計測のため判定できません。");

        // グレードを数値化 (A=1 ... F=5、小さいほど良い)
        int g  = GradeRank(grade);
        int req = GradeRank(requiredGrade);

        // WMM が有効なら、リアルタイム用途は実質 1 段階の余裕を得る
        bool realtime = app is AppClass.RealtimeGaming or AppClass.VideoConferencing;
        int effective = (wmmActive && realtime) ? g - 1 : g;

        SuitabilityLevel level =
            effective <= req - 1 ? SuitabilityLevel.Excellent :
            effective <= req     ? SuitabilityLevel.Good :
            effective == req + 1 ? SuitabilityLevel.Marginal :
                                   SuitabilityLevel.Poor;

        return new AppSuitability(app, level, wmmActive, BuildReason(app, grade, wmmActive, level));
    }

    private static int GradeRank(BufferbloatGrade g) => g switch
    {
        BufferbloatGrade.A => 1,
        BufferbloatGrade.B => 2,
        BufferbloatGrade.C => 3,
        BufferbloatGrade.D => 4,
        BufferbloatGrade.F => 5,
        _                  => 3
    };

    private static string BuildReason(
        AppClass app, BufferbloatGrade grade, bool wmmActive, SuitabilityLevel level)
    {
        string appName = app switch
        {
            AppClass.RealtimeGaming    => "オンラインゲーム",
            AppClass.VideoConferencing => "ビデオ会議",
            AppClass.VideoStreaming    => "動画ストリーミング",
            _                          => "Web 閲覧",
        };
        string wmmNote = wmmActive
            ? "WMM 有効で優先制御あり"
            : "WMM 無効 — 全トラフィックが Best Effort";
        string verdict = level switch
        {
            SuitabilityLevel.Excellent => "快適に利用できます",
            SuitabilityLevel.Good      => "問題なく利用できます",
            SuitabilityLevel.Marginal  => "混雑時に支障が出る可能性があります",
            _                          => "推奨できません (遅延が大きい)",
        };
        return $"{appName}: グレード{grade} / {wmmNote}。{verdict}。";
    }
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>トラフィック用途クラス。</summary>
public enum AppClass
{
    /// <summary>リアルタイムゲーム — 最も遅延に敏感</summary>
    RealtimeGaming,
    /// <summary>ビデオ会議 (Zoom/Teams 等)</summary>
    VideoConferencing,
    /// <summary>動画ストリーミング (バッファあり)</summary>
    VideoStreaming,
    /// <summary>Web 閲覧 — 遅延耐性が高い</summary>
    WebBrowsing
}

/// <summary>用途適性レベル。</summary>
public enum SuitabilityLevel
{
    Unknown,
    /// <summary>不適 — 体感品質が損なわれる</summary>
    Poor,
    /// <summary>限界 — 混雑時に問題</summary>
    Marginal,
    /// <summary>良好</summary>
    Good,
    /// <summary>優良</summary>
    Excellent
}

/// <summary>1 用途クラスの適性評価。</summary>
public sealed record AppSuitability(
    AppClass         App,
    SuitabilityLevel Level,
    bool             WmmActive,
    string           Reason);
