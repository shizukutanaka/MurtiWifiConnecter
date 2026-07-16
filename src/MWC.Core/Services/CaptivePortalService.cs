using System;
using System.Collections.Generic;
using System.Text.Json;

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
/// 実際の HTTP 通信はプラットフォーム層が担う。
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
    /// </summary>
    public CaptivePortalState ParseApiResponse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new CaptivePortalState
        {
            Captive          = root.TryGetProperty("captive",            out var c)   && c.ValueKind == JsonValueKind.True,
            UserPortalUrl    = root.TryGetProperty("user-portal-url",    out var u)   ? u.GetString() : null,
            VenueInfoUrl     = root.TryGetProperty("venue-info-url",     out var vi)  ? vi.GetString() : null,
            CanExtendSession = root.TryGetProperty("can-extend-session", out var ces) && ces.ValueKind == JsonValueKind.True,
            SecondsRemaining = root.TryGetProperty("seconds-remaining",  out var sr)  && sr.TryGetInt32(out var sri)  ? sri : null,
            BytesRemaining   = root.TryGetProperty("bytes-remaining",    out var br)  && br.TryGetInt64(out var brl)  ? brl : null,
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

        var parts = new List<string>();
        if (state.SecondsRemaining is { } secs)
            parts.Add($"{secs / 60}m remaining");
        if (state.BytesRemaining is { } bytes)
            parts.Add($"{bytes / 1_000_000} MB remaining");
        if (state.CanExtendSession)
            parts.Add("extendable");

        return parts.Count > 0 ? string.Join(" / ", parts) : "Authentication required";
    }

}

/// <summary>Captive Portal 認証判定</summary>
public sealed record CaptivePortalDecision(
    bool    RequiresAuth,
    string  Message,
    string? PortalUrl);
