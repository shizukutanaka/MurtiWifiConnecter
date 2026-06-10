using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// ヘルスチェックサービス。
///
/// アダプターとサービスの稼働状態を診断し、運用監視に供する。
/// Kubernetes 等の liveness/readiness probe に相当する考え方を
/// デスクトップアプリに適用する。
///
/// あわせて、ログ文字列が PII (I5: 氏名/住所/電話/IP/SSID/パスフレーズ) を
/// 含まないことを検証するユーティリティを提供する。
/// </summary>
public sealed class HealthCheckService
{
    /// <summary>
    /// アダプター群の総合ヘルスを評価する。
    /// </summary>
    public HealthReport CheckAdapters(IReadOnlyList<WifiAdapter> adapters)
    {
        if (adapters.Count == 0)
            return new HealthReport(
                Status:  HealthStatus.Unhealthy,
                Checks:  new[] { new HealthCheck("adapters", false, "No Wi-Fi adapters found") });

        var checks = new List<HealthCheck>();

        int enabled = adapters.Count(a => a.State != AdapterState.NotReady);
        checks.Add(new HealthCheck(
            "adapters.enabled",
            enabled > 0,
            enabled > 0 ? $"{enabled}/{adapters.Count} adapters ready"
                        : "No adapters ready"));

        int connected = adapters.Count(a => a.State == AdapterState.Connected);
        checks.Add(new HealthCheck(
            "adapters.connected",
            true,  // 接続なしは異常ではない (情報のみ)
            $"{connected} adapter(s) connected"));

        var status = checks.All(c => c.Passed)
            ? HealthStatus.Healthy
            : checks.Any(c => c.Name == "adapters.enabled" && !c.Passed)
                ? HealthStatus.Unhealthy
                : HealthStatus.Degraded;

        return new HealthReport(status, checks);
    }

    /// <summary>
    /// ログ文字列が PII を含まないことを検証する (I5 準拠)。
    /// 含む場合は false と検出したパターンを返す。
    /// </summary>
    public bool VerifyNoPii(string logLine, out IReadOnlyList<string> detected)
    {
        var found = new List<string>();

        // IPv4 アドレス
        if (System.Text.RegularExpressions.Regex.IsMatch(
            logLine, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b"))
            found.Add("IPv4 address");

        // MAC アドレス (BSSID は許容されるが、ログでは avoid)
        if (System.Text.RegularExpressions.Regex.IsMatch(
            logLine, @"\b([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b"))
            found.Add("MAC address");

        // メールアドレス
        if (System.Text.RegularExpressions.Regex.IsMatch(
            logLine, @"\b[\w.+-]+@[\w-]+\.[\w.-]+\b"))
            found.Add("email address");

        // 電話番号 (日本形式の簡易検出)
        if (System.Text.RegularExpressions.Regex.IsMatch(
            logLine, @"\b0\d{1,4}-\d{1,4}-\d{4}\b"))
            found.Add("phone number");

        detected = found;
        return found.Count == 0;
    }

    /// <summary>
    /// ヘルスレポートを liveness probe 形式の真偽値で返す。
    /// </summary>
    public bool IsLive(HealthReport report) => report.Status != HealthStatus.Unhealthy;
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>ヘルスレポート</summary>
public sealed record HealthReport(
    HealthStatus            Status,
    IReadOnlyList<HealthCheck> Checks);

/// <summary>個別ヘルスチェック</summary>
public sealed record HealthCheck(string Name, bool Passed, string Detail);

/// <summary>ヘルス状態</summary>
public enum HealthStatus
{
    /// <summary>正常</summary>
    Healthy,
    /// <summary>一部劣化 (動作は継続)</summary>
    Degraded,
    /// <summary>異常</summary>
    Unhealthy
}
