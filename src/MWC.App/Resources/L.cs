using System.Globalization;
using System.Resources;

namespace MWC.App.Resources;

/// <summary>
/// Strings.resx への型安全アクセサ。
///
/// Apple HIG: "Localize all user-facing text. No hardcoded strings."
///
/// 設計:
///   - resx の各キーを静的プロパティとして公開
///   - フォーマット引数版(Format("xxx", arg))を統一API化
///   - フォールバック: 該当キーなしなら英語、それも無ければキー名そのまま
/// </summary>
public static class L
{
    private static readonly ResourceManager _rm =
        new("MWC.App.Resources.Strings", typeof(L).Assembly);

    /// <summary>キー → 翻訳文字列</summary>
    public static string Get(string key)
        => _rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>キー + フォーマット引数 → 翻訳済みフォーマット文字列</summary>
    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(CultureInfo.CurrentUICulture, template, args); }
        catch { return template; }
    
    public static string LabelNoData            => Get("Label_NoData");
    public static string LabelNetworksNotFound   => Get("Label_NetworksNotFound");
    public static string LabelRetryHint          => Get("Label_RetryHint");

    public static string CertExpired                    => Get("Cert_Expired");
    public static string CertExpirySoon(int days)       => Format("Cert_ExpirySoon", days);
    public static string CertExpiry90(int days)         => Format("Cert_Expiry90", days);
    public static string CertExpiryOk(int days)         => Format("Cert_ExpiryOk", days);
    public static string CaptiveSignInRequired   => Get("Captive_SignInRequired");
    public static string StatusConnectedOk       => Get("Status_ConnectedOk");
    public static string StatusConnectionFailed  => Get("Status_ConnectionFailed");

    public static string ErrorUnexpected(string msg)    => Format("Error_Unexpected", msg);
    public static string StatusDeleted(string ssid)     => Format("Status_Deleted", ssid);
    public static string StatusDeleteFailed(string ssid) => Format("Status_DeleteFailed", ssid);
}

    // ─── 静的プロパティ(よく使うキーのIntelliSense用) ───
    public static string AppTitle           => Get("App_Title");
    public static string ActionRefresh      => Get("Action_Refresh");
    public static string ActionConnect      => Get("Action_Connect");
    public static string ActionDisconnect   => Get("Action_Disconnect");
    public static string ActionCancel       => Get("Action_Cancel");
    public static string LabelPassphrase    => Get("Label_Passphrase");
    public static string LabelNotConnected  => Get("Label_NotConnected");
    public static string StatusScanning     => Get("Status_Scanning");
    public static string StatusNoData       => Get("Status_NoData");

    public static string TabDetail          => Get("Tab_Detail");
    public static string TabSignal          => Get("Tab_Signal");
    public static string TabChannel         => Get("Tab_Channel");

    public static string ActionExportCsv    => Get("Action_Export_Csv");
    public static string ActionExportJson   => Get("Action_Export_Json");
    public static string ActionExportTxt    => Get("Action_Export_Txt");

    public static string TrayNotConnected   => Get("Status_Tray_NotConnected");

    // ─── 動的引数版 ───────────────────────────────────
    public static string LabelConnected(string ssid)
        => Format("Label_Connected", ssid);
    public static string StatusAdapterCount(int count)
        => Format("Status_AdapterCount", count);
    public static string ErrorConnectionFailed(string reason)
        => Format("Error_ConnectionFailed", reason);
    // ─── v1.8 動的引数版 ─────────────────────────────────
    public static string StatusCopied(string ssid)        => Format("Status_Copied", ssid);
    public static string StatusDisconnected(string label) => Format("Status_Disconnected", label);
}
