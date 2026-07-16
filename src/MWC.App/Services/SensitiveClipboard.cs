using System;
using System.IO;
using System.Windows;
using Serilog;

namespace MWC.App.Services;

/// <summary>
/// 機密文字列 (パスフレーズを含む WIFI: URI 等) をクリップボードへ置くためのラッパー。
///
/// 背景:
///   通常の <see cref="Clipboard.SetText(string)"/> でコピーした内容は Windows の
///   **クリップボード履歴 (Win+V)** に残り、設定によっては **クラウドクリップボード**
///   で他デバイスへ同期される。Wi-Fi パスフレーズを含む WIFI: URI をそのまま置くと、
///   一度の貼り付けを越えて履歴・クラウドに残存し、他アプリ/他デバイスから読める。
///
///   そこで Windows が定義するクリップボードフォーマットで「履歴・クラウド・モニタの
///   対象外」を宣言する (パスワードマネージャ KeePass 等と同じ手法):
///     - ExcludeClipboardContentFromMonitorProcessing : クリップボードモニタから除外
///     - CanIncludeInClipboardHistory (DWORD 0)        : Win+V 履歴から除外
///     - CanUploadToCloudClipboard   (DWORD 0)         : クラウド同期から除外
///
///   CLAUDE.md がパスフレーズ/WIFI: URI を「ログ出力禁止」の機密として扱うのと同じ
///   方針で、クリップボードという別の永続化経路も塞ぐ。
/// </summary>
public static class SensitiveClipboard
{
    private const string ExcludeMonitor = "ExcludeClipboardContentFromMonitorProcessing";
    private const string CanHistory     = "CanIncludeInClipboardHistory";
    private const string CanCloud       = "CanUploadToCloudClipboard";

    /// <summary>
    /// 機密文字列をクリップボードへコピーする。履歴 (Win+V)・クラウド同期からは除外する。
    /// クリップボードを他プロセスがロックしている等で失敗しても例外は投げず false を返す。
    /// </summary>
    public static bool SetText(string text)
    {
        try
        {
            var data = new DataObject();
            data.SetText(text);
            // 各フォーマットを付与して「履歴・クラウド・モニタの対象外」を宣言する。
            data.SetData(ExcludeMonitor, new MemoryStream());
            data.SetData(CanHistory,     ZeroDword());
            data.SetData(CanCloud,       ZeroDword());
            // copy: true → アプリ終了後もクリップボード内容が残るようフラッシュする。
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (Exception ex)
        {
            // クリップボードは他プロセスとの競合で COMException/ExternalException を投げうる。
            // 機密内容自体はログに出さず、失敗の事実のみ記録する。
            Log.Warning(ex, "SensitiveClipboard.SetText failed (clipboard busy?)");
            return false;
        }
    }

    private static MemoryStream ZeroDword() => new(new byte[] { 0, 0, 0, 0 });
}
