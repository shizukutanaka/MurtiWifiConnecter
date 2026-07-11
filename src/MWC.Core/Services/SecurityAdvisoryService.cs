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
///   - **FragAttacks** (Vanhoef, USENIX Security 2021):
///     フレーム集約/フラグメンテーションの設計・実装欠陥 (CVE-2020-24586/24587/24588) が
///     ほぼ全 Wi-Fi 機器に影響。更新・HTTPS・MFP 必須が緩和策
///
///   - **WPS PIN brute-force / Pixie-Dust**:
///     WPS 外部レジストラの PIN 方式は 8 桁 PIN の構造的弱点で破られうる
///
///   - **WPA3 mesh SSID-binding gap (2025)**:
///     WPA3(SAE)でも SSID はハンドシェイクや導出鍵に暗号学的に束縛されないため、
///     同名 SSID を broadcast する rogue AP は依然構築可能(2025年前半に指摘)。
///     本サービスは情報提供に留め、実際の検知は EvilTwinDetector(別サービス)が担う。
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

        // 1. WPA3 transition mode → Dragonblood downgrade attack
        if (network.IsWpa3TransitionMode)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Warning,
                Code:       "MWC-SEC-001",
                Title:      "WPA3 Transition Mode Detected",
                Detail:     "This network operates in mixed WPA2/WPA3 mode. " +
                            "An attacker can induce a WPA2 downgrade and perform a dictionary attack " +
                            "(Dragonblood, IEEE S&P 2020). Prefer WPA3-only mode.",
                Reference:  "Vanhoef & Ronen, Dragonblood (2020)"));
        }

        // 2. MFP disabled → deauth/disassoc attack
        if (network.Pmf == PmfStatus.Disabled &&
            network.Auth is AuthMethod.WPA2PSK or AuthMethod.WPA2Enterprise)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Warning,
                Code:       "MWC-SEC-002",
                Title:      "Protected Management Frames Disabled",
                Detail:     "This network does not support management frame protection (802.11w/MFP). " +
                            "An attacker can force-disconnect clients with spoofed deauth frames " +
                            "(WiSec 2022). Use a network with MFP enabled.",
                Reference:  "Schepers et al., WiSec 2022"));
        }

        // 3. WEP / WPA (TKIP) → legacy ciphers
        if (network.Auth is AuthMethod.WEP)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Critical,
                Code:       "MWC-SEC-003",
                Title:      "WEP Is Broken",
                Detail:     "WEP encryption can be cracked in minutes. Migrate to WPA2 or WPA3 immediately.",
                Reference:  "RC4 keystream reuse"));
        }
        else if (network.Auth is AuthMethod.WPAPSK)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Warning,
                Code:       "MWC-SEC-004",
                Title:      "WPA (TKIP) Is Deprecated",
                Detail:     "WPA1/TKIP has known vulnerabilities. Use WPA2-AES or higher.",
                Reference:  "TKIP MIC attacks"));
        }

        // 4. Open network (not OWE) — Warning rather than Critical because WEP carries
        //    the additional false-sense-of-security risk (ComputeScore also ranks WEP < Open).
        if (network.Auth is AuthMethod.Open)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Warning,
                Code:       "MWC-SEC-005",
                Title:      "Unencrypted Network",
                Detail:     "Traffic is not encrypted and can be eavesdropped by anyone on the network. " +
                            "Use the OWE (Enhanced Open) variant if available. " +
                            "Avoid transmitting sensitive data.",
                Reference:  "RFC 8110 (OWE)"));
        }

        // 5. FragAttacks — design/implementation flaws in frame aggregation/fragmentation
        //    (affects nearly all Wi-Fi devices). Provide info when encrypted but MFP not required.
        //    WEP/Open have stronger advisories already, so exclude them here.
        bool isEncrypted = network.Auth is not (AuthMethod.Open or AuthMethod.WEP);
        if (isEncrypted && network.Pmf != PmfStatus.Required)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Info,
                Code:       "MWC-SEC-006",
                Title:      "FragAttacks Mitigation",
                Detail:     "Design and implementation flaws in frame aggregation and fragmentation " +
                            "(FragAttacks, CVE-2020-24586/24587/24588) affect nearly all Wi-Fi devices. " +
                            "Keep OS, drivers, and firmware up to date, and prefer HTTPS. " +
                            "An AP with mandatory MFP (802.11w) reduces plaintext injection risk.",
                Reference:  "Vanhoef, FragAttacks CVE-2020-24586/87/88 (USENIX Security 2021)"));
        }

        // 6. WPS enabled → external registrar PIN brute-force / Pixie-Dust
        if (network.WpsEnabled)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Warning,
                Code:       "MWC-SEC-007",
                Title:      "WPS Enabled",
                Detail:     "This AP has WPS active. The external registrar PIN method can be broken " +
                            "via brute-force (hours) or the Pixie-Dust attack due to structural weaknesses " +
                            "in the 8-digit PIN. Disable WPS (especially PIN method) in your router settings.",
                Reference:  "WSC PIN external registrar brute-force / Pixie-Dust"));
        }

        // 7. Hardened network → positive feedback
        if (network.Hardening == SecurityHardening.Hardened)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Good,
                Code:       "MWC-SEC-100",
                Title:      "Hardened Security Configuration",
                Detail:     "WPA3-SAE with mandatory MFP. Resistant to both Dragonblood and deauth attacks.",
                Reference:  "WPA3 + 802.11w"));
        }

        // 8. WPA3-SAE (transition mode を除く純 WPA3) → SSID は依然ハンドシェイクに
        //    暗号学的に束縛されないという 2025 年の指摘。同名 rogue AP を装うこと自体は
        //    WPA3 でも技術的に可能なため、EvilTwinDetector による検査(実装済み・別経路)が
        //    引き続き有効であることを利用者に伝える情報提供。
        if (network.Auth is AuthMethod.WPA3SAE && !network.IsWpa3TransitionMode)
        {
            advisories.Add(new SecurityAdvisory(
                Severity:   AdvisorySeverity.Info,
                Code:       "MWC-SEC-008",
                Title:      "SSID Not Cryptographically Bound",
                Detail:     "Even with WPA3-SAE, the SSID itself is not cryptographically bound to the " +
                            "authentication handshake. A rogue AP can still broadcast the same SSID; " +
                            "MWC's Evil Twin detection (BSSID/vendor history) remains the relevant defense.",
                Reference:  "WPA3 mesh SSID-binding gap (2025)"));
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

        // WPS 有効ペナルティ (外部レジストラ PIN ブルートフォース / Pixie-Dust)
        if (network.WpsEnabled) score -= 10;

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
