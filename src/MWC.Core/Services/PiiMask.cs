using System;

namespace MWC.Core.Services;

/// <summary>
/// PII (所在地特定情報) のマスキング共通ユーティリティ。
///
/// SSID と BSSID は所在地を特定しうる情報である (BSSID は地理位置データベースに
/// 載り、可視 SSID の一覧は所在地のフィンガープリントになる)。診断バンドル
/// (<see cref="DiagnosticBundleService"/>) だけでなく、ディスクに永続化される
/// ログでも伏字化することで、ログ共有・フォレンジック・「忘れた」後の残存に
/// よる所在地履歴の漏洩を防ぐ。
/// </summary>
public static class PiiMask
{
    /// <summary>SSID を先頭 2 文字残してマスクする (例: "MyWiFi" → "My****")。</summary>
    public static string Ssid(string? ssid)
    {
        if (string.IsNullOrEmpty(ssid)) return "(empty)";
        if (ssid.Length <= 2) return ssid[0] + "*";
        return ssid.Substring(0, 2) + new string('*', Math.Min(ssid.Length - 2, 6));
    }
}
