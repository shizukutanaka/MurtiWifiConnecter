using System.Globalization;
using System.Resources;
using MWC.Core.Services;

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
    public static string ActionClose        => Get("Action_Close");
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

    public static string StatusConnectedWithDuration(string ssid, System.TimeSpan elapsed)
    {
        var dur = elapsed.TotalHours >= 1
            ? Format("Duration_HoursMinutes", (int)elapsed.TotalHours, elapsed.Minutes)
            : Format("Duration_Minutes", (int)elapsed.TotalMinutes);
        return Format("Status_ConnectedWithDuration", ssid, dur);
    }
    public static string StatusConnectedNoTimer(string ssid) => Format("Status_ConnectedNoTimer", ssid);

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
    public static string SettingsSectionNotify      => Get("Settings_Section_Notify");
    public static string SettingsSectionHidden      => Get("Settings_Section_Hidden");
    public static string SettingsHiddenEmpty        => Get("Settings_Hidden_Empty");
    public static string SettingsHiddenUnhide       => Get("Settings_Hidden_Unhide");
    public static string SettingsHiddenListAutomation => Get("Settings_Hidden_ListAutomation");
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

    // ─── MainWindow ───────────────────────────────────────────────────
    public static string DetailConnected           => Get("Detail_Connected");
    public static string MainWindowTitle           => Get("Main_WindowTitle");
    public static string MainWindowAutomation      => Get("Main_WindowAutomation");
    public static string MainSearchTooltip         => Get("Main_SearchTooltip");
    public static string MainSearchAutomation      => Get("Main_SearchAutomation");
    public static string MainRescanTooltip         => Get("Main_RescanTooltip");
    public static string MainRescanAutomation      => Get("Main_RescanAutomation");
    public static string MainToggleModeTooltip     => Get("Main_ToggleModeTooltip");
    public static string MainAllAdaptersTooltip    => Get("Main_AllAdaptersTooltip");
    public static string MainAllAdaptersAutomation => Get("Main_AllAdaptersAutomation");
    public static string MainOverflowMenuAutomation=> Get("Main_OverflowMenuAutomation");
    public static string MainConnectAutomation     => Get("Main_ConnectAutomation");
    public static string MainAdapterTabsAutomation => Get("Main_AdapterTabsAutomation");
    public static string MainNetworkListAutomation => Get("Main_NetworkListAutomation");
    public static string MainEmptyStateMessage     => Get("Main_EmptyStateMessage");
    public static string ContextMenuShowQr         => Get("ContextMenu_ShowQr");
    public static string ContextMenuCopySsid       => Get("ContextMenu_CopySsid");
    public static string ContextMenuPinNetwork     => Get("ContextMenu_PinNetwork");
    public static string ContextMenuUnpinNetwork   => Get("ContextMenu_UnpinNetwork");
    public static string ContextMenuHideNetwork    => Get("ContextMenu_HideNetwork");
    public static string MainProfileSavedTooltip   => Get("Main_ProfileSavedTooltip");
    public static string MainPinnedTooltip         => Get("Main_PinnedTooltip");
    public static string MenuExportCsv             => Get("Menu_ExportCsv");
    public static string MenuExportJson            => Get("Menu_ExportJson");
    public static string MenuExportTxt             => Get("Menu_ExportTxt");
    public static string MenuShowQr                => Get("Menu_ShowQr");
    public static string MenuSettings              => Get("Menu_Settings");
    public static string MenuAbout                 => Get("Menu_About");
    public static string MenuScanAll               => Get("Menu_ScanAll");
    public static string MenuSavedNetworks         => Get("Menu_SavedNetworks");
    public static string MenuQualityMeasure        => Get("Menu_QualityMeasure");
    public static string MenuAllAdapters           => Get("Menu_AllAdapters");
    public static string LabelBssid                => Get("Label_Bssid");
    public static string BandFilter24              => Get("BandFilter_2_4");
    public static string BandFilter5               => Get("BandFilter_5");
    public static string BandFilter6               => Get("BandFilter_6");
    public static string DetailAuth                => Get("Detail_Auth");
    public static string DetailCipher              => Get("Detail_Cipher");
    public static string DetailPhy                 => Get("Detail_Phy");
    public static string DetailVendor              => Get("Detail_Vendor");
    public static string DetailBand                => Get("Detail_Band");
    public static string DetailChannel             => Get("Detail_Channel");
    public static string DetailFrequency           => Get("Detail_Frequency");
    public static string DetailSpeed               => Get("Detail_Speed");
    public static string DetailSignal              => Get("Detail_Signal");
    public static string DetailStatus              => Get("Detail_Status");
    public static string DetailScore               => Get("Detail_Score");
    public static string MainSignalGraphAutomation  => Get("Main_SignalGraphAutomation");
    public static string MainChannelGraphAutomation => Get("Main_ChannelGraphAutomation");
    public static string MainSelectNetworkHint      => Get("Main_SelectNetworkHint");
    public static string MainSelectHistoryHint      => Get("Main_SelectHistoryHint");
    public static string MainSignalHistoryTitle(string ssid) => Format("Main_SignalHistoryTitle", ssid);
    public static string MainSignalStrength(int pct)         => Format("Main_SignalStrength", pct);

    // ─── AllAdaptersOverviewView ──────────────────────────────────────
    public static string AllAdaptersWindowTitle       => Get("AllAdapters_WindowTitle");
    public static string AllAdaptersWindowAutomation  => Get("AllAdapters_WindowAutomation");
    public static string AllAdaptersHeading           => Get("AllAdapters_Heading");
    public static string AllAdaptersScanAll           => Get("AllAdapters_ScanAll");
    public static string AllAdaptersScanAllTooltip    => Get("AllAdapters_ScanAllTooltip");
    public static string AllAdaptersConnectAll        => Get("AllAdapters_ConnectAll");
    public static string AllAdaptersConnectAllTooltip => Get("AllAdapters_ConnectAllTooltip");
    public static string AllAdaptersPreferredHeader   => Get("AllAdapters_PreferredHeader");
    public static string AllAdaptersMoveUp            => Get("AllAdapters_MoveUp");
    public static string AllAdaptersRemovePreferred   => Get("AllAdapters_RemovePreferred");
    public static string AllAdaptersAddPreferred      => Get("AllAdapters_AddPreferred");
    public static string AllAdaptersAutoReconnect     => Get("AllAdapters_AutoReconnect");
    public static string AllAdaptersNetworkListAutomation(string name) => Format("AllAdapters_NetworkListAutomation", name);

    // ─── Notifications ────────────────────────────────────────────────
    public static string NotifyConnectedTo(string ssid)       => Format("Notify_ConnectedTo", ssid);
    public static string NotifyConnectedComplete(string ssid) => Format("Notify_ConnectedComplete", ssid);
    public static string NotifyDisconnected(string ssid)      => Format("Notify_Disconnected", ssid);
    public static string NotifyCannotConnect(string ssid)     => Format("Notify_CannotConnect", ssid);

    // ─── Accessibility announcements ──────────────────────────────────
    public static string AnnounceConnected(string ssid)     => Format("Announce_Connected", ssid);
    public static string AnnounceConnectFailed(string ssid) => Format("Announce_ConnectFailed", ssid);
    public static string AnnounceSsidCopied(string ssid)    => Format("Announce_SsidCopied", ssid);

    // ─── Quality measurement result ───────────────────────────────────
    public static string QualityResultFormat(string rtt, string loss, string grade)
        => Format("Quality_ResultFormat", rtt, loss, grade);

    // ─── Connection progress steps ────────────────────────────────────
    public static string StepIpAddress => Get("Step_IpAddress");

    // ─── Scan interval labels ─────────────────────────────────────────
    public static string ScanIntervalManual => Get("ScanInterval_Manual");
    public static string ScanInterval10s    => Get("ScanInterval_10s");
    public static string ScanInterval15s    => Get("ScanInterval_15s");
    public static string ScanInterval30s    => Get("ScanInterval_30s");
    public static string ScanInterval60s    => Get("ScanInterval_60s");
    public static string ScanInterval300s   => Get("ScanInterval_300s");

    // ─── JumpList ─────────────────────────────────────────────────────
    public static string JumpConnectDescription(string ssid) => Format("Jump_ConnectDescription", ssid);

    // ─── ConnectDialog accessibility ──────────────────────────────────
    public static string ConnectPassphraseAutomation        => Get("Connect_PassphraseAutomation");
    public static string ConnectPassphraseVisibleAutomation => Get("Connect_PassphraseVisibleAutomation");

    // ─── CertificatePickerDialog ──────────────────────────────────────
    public static string CertPickerExpiryFormat(string date, int days)
        => Format("CertPicker_ExpiryFormat", date, days);

    // ─── Compact auth / band labels (network list column display) ────────
    public static string AuthCompact(MWC.Core.Models.AuthMethod auth) => auth switch
    {
        MWC.Core.Models.AuthMethod.Open              => Get("Auth_Compact_Open"),
        MWC.Core.Models.AuthMethod.OWE               => Get("Auth_Compact_OWE"),
        MWC.Core.Models.AuthMethod.WEP               => Get("Auth_Compact_WEP"),
        MWC.Core.Models.AuthMethod.WPA3SAE           => Get("Auth_Compact_WPA3"),
        MWC.Core.Models.AuthMethod.WPA3Transition    => Get("Auth_Compact_WPA23"),
        MWC.Core.Models.AuthMethod.WPA2PSK           => Get("Auth_Compact_WPA2"),
        MWC.Core.Models.AuthMethod.WPA2Enterprise    => Get("Auth_Compact_WPA2Ent"),
        MWC.Core.Models.AuthMethod.WPA3Enterprise    => Get("Auth_Compact_WPA3Ent"),
        MWC.Core.Models.AuthMethod.WPA3Enterprise192 => Get("Auth_Compact_WPA3Ent192"),
        MWC.Core.Models.AuthMethod.WPAPSK            => Get("Auth_Compact_WPA"),
        _                                            => auth.ToString()
    };

    public static string BandCompact(MWC.Core.Models.WifiBand band) => band switch
    {
        MWC.Core.Models.WifiBand.Band2_4GHz => Get("Band_Compact_2_4"),
        MWC.Core.Models.WifiBand.Band5GHz   => Get("Band_Compact_5"),
        MWC.Core.Models.WifiBand.Band6GHz   => Get("Band_Compact_6"),
        _                                   => "?"
    };

    // ─── Security badge labels ────────────────────────────────────────
    public static string SecurityLevelLabel(MWC.Core.Services.SecurityLevel level) => level switch
    {
        MWC.Core.Services.SecurityLevel.Excellent => Get("Security_Excellent"),
        MWC.Core.Services.SecurityLevel.Good      => Get("Security_Good"),
        MWC.Core.Services.SecurityLevel.Fair      => Get("Security_Fair"),
        MWC.Core.Services.SecurityLevel.Weak      => Get("Security_Weak"),
        MWC.Core.Services.SecurityLevel.Danger    => Get("Security_Danger"),
        _                                          => Get("Security_Weak")
    };

    // ─── DFS channel ──────────────────────────────────────────────────
    public static string DetailDfsWarning  => Get("Detail_DfsWarning");
    public static string DetailDfsHint     => Get("Detail_DfsHint");
    public static string DetailDistance      => Get("Detail_Distance");
    public static string DetailRoaming      => Get("Detail_Roaming");
    public static string DetailInterference => Get("Detail_Interference");
    public static string DetailMesh         => Get("Detail_Mesh");
    public static string DetailPowerSave    => Get("Detail_PowerSave");
    public static string DetailLinkEstimate  => Get("Detail_LinkEstimate");
    public static string DetailMlo           => Get("Detail_Mlo");
    public static string DetailSignalTrend   => Get("Detail_SignalTrend");
    public static string MenuDiagnosticExport => Get("Menu_DiagnosticExport");
    public static string StatusDiagnosticExported(string filename)
        => Format("Status_DiagnosticExported", filename);
    public static string StatusExported(string filename)
        => Format("Status_Exported", filename);

    // ─── Adapter failover ─────────────────────────────────────────────
    public static string FailoverSection                        => Get("Failover_Section");
    public static string FailoverDesc                           => Get("Failover_Desc");
    public static string FailoverEnable                         => Get("Failover_Enable");
    public static string FailoverBackupAdapter                  => Get("Failover_BackupAdapter");
    public static string FailoverBackupNone                     => Get("Failover_BackupNone");
    public static string FailoverBackupHint                     => Get("Failover_BackupHint");
    public static string NotifyFailoverActivated(string name)   => Format("Notify_FailoverActivated", name);
    public static string NotifyFailoverRestored(string name)    => Format("Notify_FailoverRestored", name);

    // ─── Quality grade labels (localized) ────────────────────────────
    public static string QualityGradeLabel(QualityGrade grade) => grade switch
    {
        QualityGrade.Excellent => Get("Quality_Grade_Excellent"),
        QualityGrade.Good      => Get("Quality_Grade_Good"),
        QualityGrade.Fair      => Get("Quality_Grade_Fair"),
        QualityGrade.Poor      => Get("Quality_Grade_Poor"),
        _                      => Get("Quality_Grade_Unknown"),
    };
    public static string QualityTimeout => Get("Quality_Timeout");

    // ─── Channel congestion tooltips ──────────────────────────────────
    public static string CongestionOverloadedTooltip(int pct) => Format("Congestion_OverloadedTooltip", pct);
    public static string CongestionBusyTooltip(int pct)       => Format("Congestion_BusyTooltip", pct);

    // ─── Security advisory titles (localized) ─────────────────────────
    public static string LocalizeAdvisoryTitle(string code) => code switch
    {
        "MWC-SEC-001" => Get("Advisory_SEC001_Title"),
        "MWC-SEC-002" => Get("Advisory_SEC002_Title"),
        "MWC-SEC-003" => Get("Advisory_SEC003_Title"),
        "MWC-SEC-004" => Get("Advisory_SEC004_Title"),
        "MWC-SEC-005" => Get("Advisory_SEC005_Title"),
        "MWC-SEC-006" => Get("Advisory_SEC006_Title"),
        "MWC-SEC-007" => Get("Advisory_SEC007_Title"),
        "MWC-SEC-100" => Get("Advisory_SEC100_Title"),
        _             => code
    };

    // ─── Troubleshooting dialog (localized) ───────────────────────────
    public static MWC.Core.Services.TroubleshootingAdvice GetTroubleshootingAdvice(
        MWC.Core.Models.ConnectionFailure failure,
        MWC.Core.Models.AuthMethod auth)
    {
        bool isEnterprise = auth is MWC.Core.Models.AuthMethod.WPA2Enterprise
                                 or MWC.Core.Models.AuthMethod.WPA3Enterprise;
        var (prefix, icon) = failure switch
        {
            MWC.Core.Models.ConnectionFailure.BadCredentials when isEnterprise
                => ("Trouble_BadCredentialsEnt", "🏢"),
            MWC.Core.Models.ConnectionFailure.BadCredentials
                => ("Trouble_BadCredentials", "🔑"),
            MWC.Core.Models.ConnectionFailure.Timeout
                => ("Trouble_Timeout", "⏱"),
            MWC.Core.Models.ConnectionFailure.NotInRange
                => ("Trouble_NotInRange", "📡"),
            MWC.Core.Models.ConnectionFailure.AdapterDisabled
                => ("Trouble_AdapterDisabled", "📵"),
            MWC.Core.Models.ConnectionFailure.InsufficientPrivilege
                => ("Trouble_InsufficientPrivilege", "🔒"),
            _ => ("Trouble_Unknown", "❓")
        };
        return new MWC.Core.Services.TroubleshootingAdvice(
            Title:  Get($"{prefix}_Title"),
            Reason: Get($"{prefix}_Reason"),
            Steps:  Get($"{prefix}_Steps").Split('|'),
            Icon:   icon);
    }
}
