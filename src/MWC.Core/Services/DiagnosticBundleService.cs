using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MWC.Core.Services;

/// <summary>
/// サポート診断バンドル生成サービス (D9)。
///
/// アダプター状態・ヘルス・品質計測などの現況を、**PII を秘匿した**
/// Markdown レポートにまとめる。利用者が GitHub Issue 等へ安全に貼り付け、
/// 開発者がトラブルシュートできるようにする
/// (<see cref="TroubleshootingHelper"/> の「ログを報告」導線を補完)。
///
/// 秘匿方針 (I5 準拠):
///   - SSID         → 先頭 2 文字 + マスク (例: "My****")
///   - BSSID/MAC    → OUI 3 バイトのみ残し下位を伏字 (例: "aa:bb:cc:**:**:**")
///   - IPv4 アドレス → "x.x.x.x"
///   - メール/電話   → 伏字
///
/// 生成物は人間可読の Markdown 文字列。ファイル I/O は呼び出し側に委ねる。
/// </summary>
public sealed partial class DiagnosticBundleService
{
    /// <summary>診断バンドル (Markdown) を生成する。</summary>
    public string Build(DiagnosticContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# MWC Diagnostic Bundle");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {ctx.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- App version: {Redact(ctx.AppVersion)}");
        sb.AppendLine($"- OS: {Redact(ctx.OsDescription)}");
        sb.AppendLine();

        // ── アダプター ──
        sb.AppendLine("## Adapters");
        if (ctx.Adapters.Count == 0)
            sb.AppendLine("(none detected)");
        else
            foreach (var a in ctx.Adapters)
                sb.AppendLine($"- **{Redact(a.Name)}** — {a.State}" +
                              (a.ConnectedSsid is null ? "" : $" → {MaskSsid(a.ConnectedSsid)}"));
        sb.AppendLine();

        // ── ヘルス ──
        if (ctx.Health is { } h)
        {
            sb.AppendLine($"## Health: {h.Status}");
            foreach (var c in h.Checks)
                sb.AppendLine($"- [{(c.Passed ? "x" : " ")}] {c.Name}: {Redact(c.Detail)}");
            sb.AppendLine();
        }

        // ── 品質計測 ──
        if (ctx.Quality is { } q)
        {
            sb.AppendLine("## Quality Measurement");
            sb.AppendLine($"- Latency: {q.LatencyAvgMs} ms (min {q.LatencyMinMs} / max {q.LatencyMaxMs})");
            sb.AppendLine($"- Packet loss: {q.PacketLossPct:F0}%");
            sb.AppendLine($"- Grade: {q.Grade}");
            sb.AppendLine();
        }

        // ── 直近の失敗 ──
        if (ctx.LastFailure is { } f)
        {
            sb.AppendLine("## Last Connection Failure");
            sb.AppendLine($"- Type: {f}");
            sb.AppendLine();
        }

        // ── 追加ノート (利用者入力 — 必ず秘匿) ──
        if (!string.IsNullOrWhiteSpace(ctx.UserNote))
        {
            sb.AppendLine("## Notes");
            sb.AppendLine(Redact(ctx.UserNote!));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ── 秘匿ユーティリティ ───────────────────────────────────────────

    // [GeneratedRegex]: パターンをコンパイル時にコード生成する (.NET 7+)。
    // RegexOptions.Compiled は初回マッチ時に実行時 JIT する一方、ソース生成は
    // ビルド時に確定するため起動コストがゼロで Native AOT/トリミングにも対応する
    // (SYSLIB1045 推奨)。挙動は従来と同一 (全パターン ASCII・IgnoreCase 不使用)。
    [GeneratedRegex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b")]
    private static partial Regex Ipv4();
    [GeneratedRegex(@"\b([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b")]
    private static partial Regex Mac();
    [GeneratedRegex(@"\b[\w.+-]+@[\w-]+\.[\w.-]+\b")]
    private static partial Regex Email();
    [GeneratedRegex(@"\b0\d{1,4}-\d{1,4}-\d{4}\b")]
    private static partial Regex Phone();

    /// <summary>任意文字列から PII を伏字に置換する。</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string s = Mac().Replace(text, m => MaskMac(m.Value));   // MAC を先に (IPv4 より具体的)
        s = Ipv4().Replace(s, "x.x.x.x");
        s = Email().Replace(s, "[email]");
        s = Phone().Replace(s, "[phone]");
        return s;
    }

    /// <summary>SSID を先頭 2 文字残してマスクする。</summary>
    public static string MaskSsid(string ssid) => PiiMask.Ssid(ssid);

    /// <summary>BSSID/MAC を OUI (上位 3 バイト) のみ残して伏字化する。</summary>
    public static string MaskMac(string mac)
    {
        var parts = mac.Split(':', '-');
        if (parts.Length != 6) return "**:**:**:**:**:**";
        return $"{parts[0]}:{parts[1]}:{parts[2]}:**:**:**".ToLowerInvariant();
    }
}

// ── データ型 ─────────────────────────────────────────────────────

/// <summary>診断バンドル生成の入力コンテキスト。</summary>
public sealed record DiagnosticContext
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public string AppVersion { get; init; } = "";
    public string OsDescription { get; init; } = "";
    public IReadOnlyList<Models.WifiAdapter> Adapters { get; init; } = Array.Empty<Models.WifiAdapter>();
    public HealthReport? Health { get; init; }
    public NetworkQualityResult? Quality { get; init; }
    public Models.ConnectionFailure? LastFailure { get; init; }
    /// <summary>利用者が任意で添える補足。PII の可能性があるため秘匿対象。</summary>
    public string? UserNote { get; init; }
}
