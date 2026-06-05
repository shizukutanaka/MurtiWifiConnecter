using System.Collections.Generic;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// MAC アドレス・プローブ要求に関するプライバシー勧告サービス。
///
/// 研究の知見に基づき、端末追跡(トラッキング)リスクを診断する:
///
///   - **プローブ要求追跡** (arXiv 2206.10927):
///     端末は周期的に Probe Request を送出し、固定 MAC は容易な追跡識別子になる
///
///   - **指紋による再識別** (arXiv 2412.10548):
///     ランダム MAC でも Probe Request 内の Information Element 指紋で
///     高精度(~99%)に端末を再識別できる場合がある
///
///   - **ランダム化の限界** (arXiv 1703.02874):
///     実装不備によりランダム化が破られるケースがある
///
/// プラットフォーム層が実際の MAC ランダム化状態 (<see cref="MacAddressMode"/>) を供給する。
/// 本サービスは攻撃を実行せず、防御側の情報提供のみを行う。
/// </summary>
public sealed class PrivacyAdvisoryService
{
    /// <summary>
    /// MAC ランダム化状態と接続先から、プライバシー勧告を生成する。
    /// </summary>
    public IReadOnlyList<PrivacyAdvisory> Analyze(MacAddressMode mode, WifiNetwork network)
    {
        var advisories = new List<PrivacyAdvisory>();

        // 公共ネットワーク(認証なし)= 追跡が最も露出しやすい場面
        bool isPublic = network.Auth is AuthMethod.Open or AuthMethod.OWE;

        // 1. 固定(ハードウェア)MAC + 公共ネットワーク → 追跡リスク警告
        if (mode == MacAddressMode.Hardware && isPublic)
        {
            advisories.Add(new PrivacyAdvisory(
                Severity:  AdvisorySeverity.Warning,
                Code:      "MWC-PRIV-001",
                Title:     "固定 MAC で公共ネットワークに接続",
                Detail:    "ハードウェア(固定)MAC アドレスのまま認証なしのネットワークに接続している。" +
                           "固定 MAC は来訪のたびに同一識別子となり、場所をまたいだ追跡を容易にする " +
                           "(arXiv 2206.10927)。このネットワーク用にランダム MAC を有効化することを推奨。",
                Reference: "arXiv 2206.10927 (Probe Request Privacy)"));
        }
        // 2. 固定 MAC(一般) → ランダム化の推奨
        else if (mode == MacAddressMode.Hardware)
        {
            advisories.Add(new PrivacyAdvisory(
                Severity:  AdvisorySeverity.Info,
                Code:      "MWC-PRIV-002",
                Title:     "ランダム MAC が無効",
                Detail:    "ハードウェア(固定)MAC を使用している。ネットワーク別のランダム MAC を" +
                           "有効化すると、ネットワーク運営者やローカル観測者による追跡を抑制できる。",
                Reference: "arXiv 2206.10927"));
        }

        // 3. ネットワーク別ランダム(固定的) → 日次ローテーションの提案
        if (mode == MacAddressMode.RandomPerNetwork)
        {
            advisories.Add(new PrivacyAdvisory(
                Severity:  AdvisorySeverity.Info,
                Code:      "MWC-PRIV-003",
                Title:     "ランダム MAC(ネットワーク別・固定)",
                Detail:    "このネットワークではランダム MAC を使用しているが、値は固定的。" +
                           "日次ローテーションを使うと、長期の追跡をさらに抑制できる。",
                Reference: "Windows ランダムなハードウェア アドレス設定"));
        }

        // 4. 日次ローテーション → 良好
        if (mode == MacAddressMode.RandomDaily)
        {
            advisories.Add(new PrivacyAdvisory(
                Severity:  AdvisorySeverity.Good,
                Code:      "MWC-PRIV-100",
                Title:     "MAC プライバシー良好",
                Detail:    "ランダム MAC を日次でローテーションしており、追跡耐性が高い。",
                Reference: "Windows ランダムなハードウェア アドレス(毎日変更)"));
        }

        // 5. ランダム化していても限界がある旨の教育的情報(ランダム系モード時)
        if (mode is MacAddressMode.RandomPerNetwork or MacAddressMode.RandomDaily)
        {
            advisories.Add(new PrivacyAdvisory(
                Severity:  AdvisorySeverity.Info,
                Code:      "MWC-PRIV-004",
                Title:     "ランダム化だけでは不十分な場合がある",
                Detail:    "MAC をランダム化していても、Probe Request 内の Information Element 指紋で" +
                           "端末が再識別される場合がある(報告では最大 ~99%)。スキャンの抑制や" +
                           "不要時の Wi-Fi オフも併用すると追跡耐性が向上する。",
                Reference: "arXiv 2412.10548 / 1703.02874"));
        }

        return advisories;
    }
}

// ── データ型 ─────────────────────────────────────────────────────────

/// <summary>端末の MAC アドレス使用モード(プラットフォーム層が供給)。</summary>
public enum MacAddressMode
{
    /// <summary>不明 (判定できない)</summary>
    Unknown,
    /// <summary>固定 — 端末のハードウェア MAC をそのまま使用</summary>
    Hardware,
    /// <summary>ネットワーク別ランダム(値は固定的)</summary>
    RandomPerNetwork,
    /// <summary>ランダム MAC を日次でローテーション</summary>
    RandomDaily
}

/// <summary>プライバシー勧告(重大度は <see cref="AdvisorySeverity"/> を共用)。</summary>
public sealed record PrivacyAdvisory(
    AdvisorySeverity Severity,
    string           Code,
    string           Title,
    string           Detail,
    string           Reference);
