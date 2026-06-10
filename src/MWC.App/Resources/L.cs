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
    }

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
    public static string TrayNoNetworks     => Get("Tray_NoNetworks");
    public static string TrayOpenApp        => Get("Tray_OpenApp");
    public static string TrayStatusConnected(string ssid, int quality)
        => Format("Tray_StatusConnected", ssid, quality);

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
    public static string StatusAdaptersConnected(int connected, int total)
        => Format("Status_AdaptersConnected", connected, total);
    public static string StatusNetworksFound(int count)   => Format("Status_NetworksFound", count);
    public static string StatusProfileCount(int count)    => Format("Status_ProfileCount", count);

    // ─── Settings dialog ─────────────────────────────────────────────
    public static string SettingsTitle             => Get("Settings_Title");
    public static string SettingsDialogAutomation  => Get("Settings_DialogAutomation");
    public static string SettingsSectionDisplay    => Get("Settings_Section_Display");
    public static string SettingsDisplayModeLabel  => Get("Settings_DisplayMode_Label");
    public static string SettingsDisplayModeDesc   => Get("Settings_DisplayMode_Desc");
    public static string SettingsModeSimple        => Get("Settings_Mode_Simple");
    public static string SettingsModeExpert        => Get("Settings_Mode_Expert");
    public static string SettingsTheme             => Get("Settings_Theme");
    public static string ThemeDark                 => Get("Theme_Dark");
    public static string ThemeLight                => Get("Theme_Light");
    public static string ThemeSystem               => Get("Theme_System");
    public static string SettingsLanguage          => Get("Settings_Language");
    public static string SettingsSectionScan       => Get("Settings_Section_Scan");
    public static string SettingsScanIntervalLabel => Get("Settings_ScanInterval_Label");
    public static string SettingsScanIntervalDesc  => Get("Settings_ScanInterval_Desc");
    public static string SettingsScanOnStartup     => Get("Settings_ScanOnStartup");
    public static string SettingsSectionNotify     => Get("Settings_Section_Notify");
    public static string SettingsNotifyLabel       => Get("Settings_Notify_Label");
    public static string SettingsNotifyDesc        => Get("Settings_Notify_Desc");
    public static string ActionSave                => Get("Action_Save");
    public static string ActionResetDefaults       => Get("Action_ResetDefaults");

    // ─── Adapter preferences dialog ──────────────────────────────────
    public static string AdapterDialogTitle        => Get("Adapter_Dialog_Title");
    public static string AdapterDialogAutomation   => Get("Adapter_Dialog_Automation");
    public static string AdapterDisplayNameSection => Get("Adapter_DisplayName_Section");
    public static string AdapterDisplayNameHint    => Get("Adapter_DisplayName_Hint");
    public static string AdapterBandSection        => Get("Adapter_Band_Section");
    public static string BandAny                   => Get("Band_Any");
    public static string AdapterBand24             => Get("Adapter_Band_24");
    public static string AdapterBand5              => Get("Adapter_Band_5");
    public static string AdapterBand6E             => Get("Adapter_Band_6E");
    public static string AdapterBandDesc           => Get("Adapter_Band_Desc");
    public static string AdapterPinnedSection      => Get("Adapter_Pinned_Section");
    public static string AdapterPinnedUnpin        => Get("Adapter_Pinned_Unpin");
    public static string AdapterPinnedEmpty        => Get("Adapter_Pinned_Empty");
    public static string AdapterAutoJoinDesc       => Get("Adapter_AutoJoin_Desc");
    public static string AdapterEnabledLabel        => Get("Adapter_Enabled_Label");
    public static string AdapterEnabledDesc         => Get("Adapter_Enabled_Desc");

    // ─── Connect dialog ───────────────────────────────────────────────
    public static string LabelPasswordPlaceholder  => Get("Label_PasswordPlaceholder");
    public static string LabelShowPassword         => Get("Label_ShowPassword");

    // ─── About dialog ─────────────────────────────────────────────────
    public static string AboutTitle                => Get("About_Title");
    public static string AboutAutomation           => Get("About_Automation");
    public static string AboutTagline              => Get("About_Tagline");
    public static string AboutDesc                 => Get("About_Desc");
    public static string AboutGitHub               => Get("About_GitHub");
    public static string AboutReportBug            => Get("About_ReportBug");
    public static string AboutLicense              => Get("About_License");

    // ─── FirstRunWizard ───────────────────────────────────────────────
    public static string WizardWindowTitle         => Get("Wizard_WindowTitle");
    public static string WizardDialogAutomation    => Get("Wizard_DialogAutomation");
    public static string WizardBack                => Get("Wizard_Back");
    public static string WizardNext                => Get("Wizard_Next");

    // ─── ShortcutHelpDialog ───────────────────────────────────────────
    public static string ShortcutsTitle            => Get("Shortcuts_Title");
    public static string ShortcutsDialogAutomation => Get("Shortcuts_DialogAutomation");
    public static string ShortcutsDesc             => Get("Shortcuts_Desc");

    // ─── QrCodeDialog ─────────────────────────────────────────────────
    public static string QRWindowTitle             => Get("QR_WindowTitle");
    public static string QRDialogAutomation        => Get("QR_DialogAutomation");
    public static string ActionCopy                => Get("Action_Copy");
    public static string QRSavePng                 => Get("QR_SavePng");

    // ─── ProfileManagerDialog ─────────────────────────────────────────
    public static string ProfileWindowTitle        => Get("Profile_WindowTitle");
    public static string ProfileDialogAutomation   => Get("Profile_DialogAutomation");
    public static string ProfileListAutomation     => Get("Profile_ListAutomation");
    public static string ActionDelete              => Get("Action_Delete");
    public static string ProfileDeleteWarning      => Get("Profile_DeleteWarning");

    // ─── ConnectionProgressDialog ─────────────────────────────────────
    public static string ProgressWindowTitle       => Get("Progress_WindowTitle");
    public static string ProgressDialogAutomation  => Get("Progress_DialogAutomation");

    // ─── CaptivePortalDialog ──────────────────────────────────────────
    public static string CaptiveDialogAutomation   => Get("Captive_DialogAutomation");
    public static string CaptiveOpenExternal       => Get("Captive_OpenExternal");
    public static string CaptiveLoading            => Get("Captive_Loading");
    public static string ActionSkip                => Get("Action_Skip");
    public static string CaptiveDone               => Get("Captive_Done");

    // ─── TroubleshootingDialog ────────────────────────────────────────
    public static string TroubleWindowTitle        => Get("Trouble_WindowTitle");
    public static string TroubleDialogAutomation   => Get("Trouble_DialogAutomation");
    public static string TroubleSolutions          => Get("Trouble_Solutions");
    public static string ActionRetry               => Get("Action_Retry");

    // ─── CertificatePickerDialog ──────────────────────────────────────
    public static string CertPickerTitle           => Get("Cert_PickerTitle");
    public static string CertPickerAutomation      => Get("Cert_PickerAutomation");
    public static string CertPickerDesc            => Get("Cert_PickerDesc");
    public static string CertListAutomation        => Get("Cert_ListAutomation");
    public static string CertSubject               => Get("Cert_Subject");
    public static string CertIssuerLabel           => Get("Cert_IssuerLabel");
    public static string CertExpiryLabel           => Get("Cert_ExpiryLabel");
    public static string CertOpenStore             => Get("Cert_OpenStore");
    public static string CertUseThis               => Get("Cert_UseThis");
}
