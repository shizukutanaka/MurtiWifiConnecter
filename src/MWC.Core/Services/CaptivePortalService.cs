using System;

namespace MWC.Core.Services;

/// <summary>
/// Captive Portal 検出・状態管理サービス (RFC 8908/8910 準拠)。
///
/// 標準:
///   - RFC 8910: DHCP Option 114 / IPv6 RA で portal API endpoint を通知
///   - RFC 8908: portal API は JSON で captive 状態 (captive, user-portal-url,
///     venue-info-url, can-extend-session, seconds-remaining, bytes-remaining) を返す
///
/// レガシーな HTTP リダイレクト傍受より堅牢で、modern iOS/Android が優先利用する。
///
/// 本サービスは検出ロジックの状態機械とパースを提供する。
/// 実際の HTTP 通信はプラットフォーム層が担う (Core はゼロ外部依存)。
/// </summary>
public sealed class CaptivePortalService
{
    /// <summary>
    /// RFC 8908 の Captive Portal API レスポンス (JSON) を表す。
    /// </summary>
    public sealed record CaptivePortalState
    {
        /// <summary>captive=true の場合、認証が必要</summary>
        public required bool Captive { get; init; }

        /// <summary>ユーザーが認証を行う portal の URL (RFC 8908 user-portal-url)</summary>
        public string? UserPortalUrl { get; init; }

        /// <summary>会場情報 URL (RFC 8908 venue-info-url / Passpoint R3)</summary>
        public string? VenueInfoUrl { get; init; }

        /// <summary>セッション延長が可能か (can-extend-session)</summary>
        public bool CanExtendSession { get; init; }

        /// <summary>残りセッション秒数 (seconds-remaining)</summary>
        public int? SecondsRemaining { get; init; }

        /// <summary>残りバイト数 (bytes-remaining)</summary>
        public long? BytesRemaining { get; init; }
    }

    /// <summary>
    /// RFC 8908 形式の JSON をパースする。
    /// ゼロ依存のため System.Text.Json は使わず、シンプルなフィールド抽出。
    /// </summary>
    public CaptivePortalState ParseApiResponse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return new CaptivePortalState
        {
            Captive          = ExtractBool(json, "captive") ?? false,
            UserPortalUrl    = ExtractString(json, "user-portal-url"),
            VenueInfoUrl     = ExtractString(json, "venue-info-url"),
            CanExtendSession = ExtractBool(json, "can-extend-session") ?? false,
            SecondsRemaining = ExtractInt(json, "seconds-remaining"),
            BytesRemaining   = ExtractLong(json, "bytes-remaining"),
        };
    }

    /// <summary>
    /// 接続状態から portal 認証が必要かを判定する。
    /// </summary>
    public CaptivePortalDecision Evaluate(CaptivePortalState state)
    {
        if (!state.Captive)
            return new CaptivePortalDecision(
                RequiresAuth: false,
                Message:      "Internet connected. No authentication required.",
                PortalUrl:    null);

        if (!string.IsNullOrEmpty(state.UserPortalUrl))
            return new CaptivePortalDecision(
                RequiresAuth: true,
                Message:      "Authentication required. Open the portal and sign in.",
                PortalUrl:    state.UserPortalUrl);

        return new CaptivePortalDecision(
            RequiresAuth: true,
            Message:      "Authentication required but no portal URL provided. Open any site in a browser and wait for redirect.",
            PortalUrl:    null);
    }

    /// <summary>
    /// セッション残量を人間語で説明する。
    /// </summary>
    public string DescribeSession(CaptivePortalState state)
    {
        if (!state.Captive) return "Authenticated";

        var parts = new System.Collections.Generic.List<string>();
        if (state.SecondsRemaining is { } secs)
            parts.Add($"{secs / 60}m remaining");
        if (state.BytesRemaining is { } bytes)
            parts.Add($"{bytes / 1_000_000} MB remaining");
        if (state.CanExtendSession)
            parts.Add("extendable");

        return parts.Count > 0 ? string.Join(" / ", parts) : "Authentication required";
    }

    // ── 軽量 JSON フィールド抽出 (ゼロ依存) ──────────────────────

    private static string? ExtractString(string json, string key)
    {
        var marker = $"\"{key}\"";
        var idx = json.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var colon = json.IndexOf(':', idx + marker.Length);
        if (colon < 0) return null;
        var q1 = json.IndexOf('"', colon);
        if (q1 < 0) return null;
        var q2 = json.IndexOf('"', q1 + 1);
        if (q2 < 0) return null;
        return json.Substring(q1 + 1, q2 - q1 - 1);
    }

    private static bool? ExtractBool(string json, string key)
    {
        var raw = ExtractRawValue(json, key);
        if (raw is null) return null;
        if (raw.StartsWith("true",  StringComparison.OrdinalIgnoreCase)) return true;
        if (raw.StartsWith("false", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static int? ExtractInt(string json, string key)
        => long.TryParse(TrimNumber(ExtractRawValue(json, key)), out var v) ? (int)v : null;

    private static long? ExtractLong(string json, string key)
        => long.TryParse(TrimNumber(ExtractRawValue(json, key)), out var v) ? v : null;

    private static string? ExtractRawValue(string json, string key)
    {
        var marker = $"\"{key}\"";
        var idx = json.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var colon = json.IndexOf(':', idx + marker.Length);
        if (colon < 0) return null;
        return json[(colon + 1)..].TrimStart();
    }

    private static string TrimNumber(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = 0;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-')) i++;
        return s[..i];
    }
}

/// <summary>Captive Portal 認証判定</summary>
public sealed record CaptivePortalDecision(
    bool    RequiresAuth,
    string  Message,
    string? PortalUrl);
