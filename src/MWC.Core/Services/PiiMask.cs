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
    /// <remarks>
    /// 残す先頭 2 文字は制御文字を無害化する。802.11 の SSID は任意オクテット
    /// (CR/LF を含む) を取りうるため、攻撃者が <c>"\r\n偽ログ行"</c> のような SSID を
    /// ブロードキャストし、それがプレーンテキストのログ (Serilog の <c>{Message:lj}</c>
    /// はプロパティを非エスケープで描画する) に出力されると、改行が注入されログ行を
    /// 偽造できる (CWE-117: Log Injection)。可視文字 (絵文字・非ラテン等) は保持し、
    /// <see cref="char.IsControl(char)"/> のみ '?' に置換する。
    /// </remarks>
    public static string Ssid(string? ssid)
    {
        if (string.IsNullOrEmpty(ssid)) return "(empty)";
        int keep   = Math.Min(2, ssid.Length);
        int hidden = ssid.Length - keep;
        // 残す先頭 keep 文字を、制御文字を無害化しつつコピーする。
        string prefix = string.Create(keep, ssid, static (dst, src) =>
        {
            for (int i = 0; i < dst.Length; i++)
            {
                char c = src[i];
                dst[i] = char.IsControl(c) ? '?' : c;
            }
        });
        return prefix + new string('*', hidden > 0 ? Math.Min(hidden, 6) : 1);
    }
}
