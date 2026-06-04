using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// Wi-Fi セキュリティ勧告サービス。
///
/// 学術研究の知見に基づき、接続先ネットワークのセキュリティリスクを診断する:
///
///   - **Dragonblood** (Vanhoef &amp; Ronen, IEEE S&amp;P 2020):
///     WPA3 transition mode は WPA2 へのダウングレード攻撃・辞書攻撃に脆弱
///
///   - **wifi-deauthentication** (Schepers et al., WiSec 2022):
///     MFP (802.11w) 無効の AP は deauth/disassoc 攻撃で強制切断される
///
///   - **SAE-PK time-memory trade-off** (Seddigh &amp; Soleimany):
///     SSID の再利用が攻撃の償却コストを下げる
///
/// 本サービスは攻撃を実行せず、防御側の情報提供のみを行う。
/// </summary>
public sealed class SecurityAdvisoryService
{
    /// <summary>
    /// ネットワークのセキュリティ勧告を生成する。
    /// </summary>
    public IReadOnlyList<SecurityAdvisory> Analyze(WifiNetwork network)
    {
        var advisories = new List<SecurityAdvisory>();

        // 1. WPA3 transition mode → Dragonblood ダウングレード攻撃
        if (network.IsWpa3TransitionMode)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Warning,
                Code:       "MWC-SEC-001",
                Title:      "WPA3 移行モード検出",
                Detail:     "このネットワークは WPA2 と WPA3 の混在モードで動作している。" +
                            "攻撃者が WPA2 へのダウングレードを誘導し、辞書攻撃を行える可能性がある " +
                            "(Dragonblood, IEEE S&P 2020)。WPA3 専用モードの利用を推奨。",
                Reference:  "Vanhoef & Ronen, Dragonblood (2020)"));
        }

        // 2. MFP 無効 → deauth/disassoc 攻撃
        if (network.Pmf == PmfStatus.Disabled &&
            network.Auth is AuthMethod.WPA2PSK or AuthMethod.WPA2Enterprise)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Warning,
                Code:       "MWC-SEC-002",
                Title:      "Protected Management Frames 無効",
                Detail:     "このネットワークは管理フレーム保護 (802.11w/MFP) に対応していない。" +
                            "攻撃者が偽装した切断フレームでクライアントを強制切断できる " +
                            "(WiSec 2022)。MFP 対応ネットワークの利用を推奨。",
                Reference:  "Schepers et al., WiSec 2022"));
        }

        // 3. WEP / WPA (TKIP) → 旧式暗号
        if (network.Auth is AuthMethod.WEP)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Critical,
                Code:       "MWC-SEC-003",
                Title:      "WEP は危殆化済み",
                Detail:     "WEP 暗号は数分で解読可能。直ちに WPA2/WPA3 への移行が必要。",
                Reference:  "RC4 keystream reuse"));
        }
        else if (network.Auth is AuthMethod.WPAPSK)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Warning,
                Code:       "MWC-SEC-004",
                Title:      "WPA (TKIP) は非推奨",
                Detail:     "WPA1/TKIP は既知の脆弱性がある。WPA2-AES 以上を推奨。",
                Reference:  "TKIP MIC attacks"));
        }

        // 4. オープンネットワーク (OWE でない)
        if (network.Auth is AuthMethod.Open)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Info,
                Code:       "MWC-SEC-005",
                Title:      "暗号化されていないネットワーク",
                Detail:     "通信が暗号化されない。OWE (Enhanced Open) 対応版があればそちらを推奨。" +
                            "機密情報の送受信は避けること。",
                Reference:  "RFC 8110 (OWE)"));
        }

        // 5. 堅牢ネットワーク → 肯定的フィードバック
        if (network.Hardening == SecurityHardening.Hardened)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Good,
                Code:       "MWC-SEC-100",
                Title:      "堅牢なセキュリティ設定",
                Detail:     "WPA3-SAE + MFP 必須。Dragonblood・deauth 両方の攻撃に耐性がある。",
                Reference:  "WPA3 + 802.11w"));
        }

        return advisories;
    }

    /// <summary>
    /// 複数ネットワークから最もセキュアな選択肢を推奨する。
    /// </summary>
    public WifiNetwork? RecommendMostSecure(IEnumerable<WifiNetwork> networks, string ssid)
    {
        return networks
            .Where(n => string.Equals(n.Ssid, ssid, StringComparison.Ordinal))
            .OrderBy(n => (int)n.Hardening)   // Hardened(0) を最優先
            .ThenByDescending(n => n.SignalQuality)
            .FirstOrDefault();
    }

    /// <summary>
    /// ネットワークの総合セキュリティスコア (0-100)。
    /// </summary>
    public int ComputeScore(WifiNetwork network)
    {
        int score = network.Auth switch
        {
            AuthMethod.WPA3SAE            => 90,
            AuthMethod.WPA3Enterprise192  => 100,
            AuthMethod.WPA3Enterprise     => 92,
            AuthMethod.WPA2Enterprise     => 75,
            AuthMethod.WPA2PSK            => 70,
            AuthMethod.OWE                => 65,
            AuthMethod.WPAPSK             => 40,
            AuthMethod.WEP                => 10,
            AuthMethod.Open               => 20,
            _                             => 50
        };

        // MFP ボーナス/ペナルティ
        score += network.Pmf switch
        {
            PmfStatus.Required => 10,
            PmfStatus.Capable  => 5,
            PmfStatus.Disabled => -10,
            _                  => 0
        };

        // transition mode ペナルティ (Dragonblood)
        if (network.IsWpa3TransitionMode) score -= 15;

        return Math.Clamp(score, 0, 100);
    }
}

// ── データ型 ─────────────────────────────────────────────────────────

/// <summary>セキュリティ勧告</summary>
public sealed record SecurityAdvisory(
    AdvisorySeverity Severity,
    string           Code,
    string           Title,
    string           Detail,
    string           Reference);

/// <summary>勧告の重大度</summary>
public enum AdvisorySeverity
{
    /// <summary>良好 (肯定的フィードバック)</summary>
    Good,
    /// <summary>情報</summary>
    Info,
    /// <summary>警告</summary>
    Warning,
    /// <summary>重大</summary>
    Critical
}
