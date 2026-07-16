using System;
using System.Diagnostics;
using Serilog;

namespace MWC.App.Services;

/// <summary>
/// 既定ブラウザで URL を開くための安全なラッパー。
///
/// 背景:
///   <c>Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })</c> は
///   シェル経由で「URL に関連付けられたハンドラ」を起動する。これは http/https
///   だけでなく <c>file://</c>・<c>ms-…:</c> 等の任意スキーム、さらには実行ファイル
///   パスまで起動しうる典型的な任意起動シンクである (Qiita: naoki_oda ほか)。
///
///   MWC では現状すべて呼び出し元がハードコード URL を渡すため実害はないが、
///   キャプティブポータルのように「信頼できないネットワークが関与する画面」から
///   ブラウザ起動を行う以上、シンク側で http/https のみに限定する不変条件を
///   明示・強制しておく (多層防御)。将来 XAML の Hyperlink がデータバインドされたり
///   非 http スキームの URL を渡しても、ここで弾かれる。
///
/// 設計:
///   - http / https の絶対 URI のみ許可。それ以外は起動せず警告ログのみ。
///   - 起動失敗 (ブラウザ未関連付け等) は握りつぶさずログに残す。
/// </summary>
public static class BrowserLauncher
{
    /// <summary>
    /// http/https の絶対 URL のみを既定ブラウザで開く。
    /// 不正スキーム・相対 URI・起動失敗時は false を返し、例外は投げない。
    /// </summary>
    public static bool OpenHttp(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsHttp(uri))
        {
            Log.Warning("BrowserLauncher refused non-http(s) URL (scheme blocked for safety)");
            return false;
        }
        return Open(uri);
    }

    /// <summary>
    /// http/https の絶対 <see cref="Uri"/> のみを既定ブラウザで開く。
    /// </summary>
    public static bool OpenHttp(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || !IsHttp(uri))
        {
            Log.Warning("BrowserLauncher refused non-http(s) URI (scheme blocked for safety)");
            return false;
        }
        return Open(uri);
    }

    private static bool IsHttp(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private static bool Open(Uri uri)
    {
        try
        {
            // 検証済み http/https の絶対 URI のみがここに到達する。
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            // ブラウザ未関連付け・ユーザーキャンセル等。クラッシュさせず記録する。
            Log.Warning(ex, "BrowserLauncher failed to launch the default browser");
            return false;
        }
    }
}
