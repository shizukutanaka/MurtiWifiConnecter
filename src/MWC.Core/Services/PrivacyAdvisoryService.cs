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
///   - **マルチチャネルスニファ+2段クラスタリングによる de-randomization** (arXiv 2408.01578, 2024):
///     複数チャネルの同時観測とクラスタリングを組み合わせることで、ランダム MAC 環境下でも
///     プローブ要求の再識別精度が向上しうるという追加の知見(2026-07 追補)
///
/// MAC ランダム化状態 (<see cref="MacAddressMode"/>) は、アダプターの MAC アドレスから
/// <see cref="MacAddressModeInference"/> が推定する(OS 設定の読み取りは不要)。
/// 供給されなかった場合は <see cref="MacAddressMode.Unknown"/> のまま助言を控える。
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
                Title:     "Hardware MAC on Public Network",
                Detail:    "You are connecting to an open network with your hardware (fixed) MAC address. " +
                           "A fixed MAC acts as a persistent identifier across visits, making cross-location " +
                           "tracking easy (arXiv 2206.10927). Enable random MAC for this network.",
                Reference: "arXiv 2206.10927 (Probe Request Privacy)"));
        }
        // 2. 固定 MAC(一般) → ランダム化の推奨
        else if (mode == MacAddressMode.Hardware)
        {
            advisories.Add(new PrivacyAdvisory(
                Severity:  AdvisorySeverity.Info,
                Code:      "MWC-PRIV-002",
                Title:     "Random MAC Disabled",
                Detail:    "You are using your hardware (fixed) MAC address. Enabling per-network random MAC " +
                           "addresses reduces tracking by network operators and local observers.",
                Reference: "arXiv 2206.10927"));
        }

        // 3. ネットワーク別ランダム(固定的) → 日次ローテーションの提案
        if (mode == MacAddressMode.RandomPerNetwork)
        {
            advisories.Add(new PrivacyAdvisory(
                Severity:  AdvisorySeverity.Info,
                Code:      "MWC-PRIV-003",
                Title:     "Random MAC (Per-Network, Fixed)",
                Detail:    "You are using a random MAC for this network, but the value is fixed. " +
                           "Using daily rotation further reduces long-term tracking.",
                Reference: "Windows random hardware address settings"));
        }

        // 4. 日次ローテーション → 良好
        if (mode == MacAddressMode.RandomDaily)
        {
            advisories.Add(new PrivacyAdvisory(
                Severity:  AdvisorySeverity.Good,
                Code:      "MWC-PRIV-100",
                Title:     "Good MAC Privacy",
                Detail:    "You are using daily random MAC rotation — high resistance to tracking.",
                Reference: "Windows random hardware address (change daily)"));
        }

        // 5. ランダム化していても限界がある旨の教育的情報(ランダム系モード時)
        if (mode is MacAddressMode.Randomized or MacAddressMode.RandomPerNetwork or MacAddressMode.RandomDaily)
        {
            advisories.Add(new PrivacyAdvisory(
                Severity:  AdvisorySeverity.Info,
                Code:      "MWC-PRIV-004",
                Title:     "Randomisation May Not Be Sufficient",
                Detail:    "Even with MAC randomisation, a device may be re-identified via Information Element " +
                           "fingerprints in Probe Requests (up to ~99% accuracy reported). Limiting scans and " +
                           "turning off Wi-Fi when not in use further improves tracking resistance.",
                Reference: "arXiv 2412.10548 / 1703.02874"));
        }

        return advisories;
    }
}

// ── データ型 ─────────────────────────────────────────────────────────

/// <summary>端末の MAC アドレス使用モード。</summary>
public enum MacAddressMode
{
    /// <summary>不明 (判定できない)</summary>
    Unknown,
    /// <summary>固定 — 端末のハードウェア MAC をそのまま使用</summary>
    Hardware,
    /// <summary>
    /// ランダム化されているが、種類(ネットワーク別か日次か)までは決まっていない。
    /// アドレスの LAA ビットだけで判定した場合はここに落ちる
    /// (<see cref="MacAddressModeInference.FromAddress"/>)。
    /// </summary>
    Randomized,
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
