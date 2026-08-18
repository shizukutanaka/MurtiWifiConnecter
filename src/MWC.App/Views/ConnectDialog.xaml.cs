using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MWC.App.Resources;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.App.Views;

public partial class ConnectDialog : Window
{
    public string? Passphrase { get; private set; }
    private readonly AuthMethod _auth;
    private readonly string _ssid;

    /// <summary>この認証方式が 802.1X Enterprise か。</summary>
    private bool IsEnterprise => _auth is AuthMethod.WPA2Enterprise
        or AuthMethod.WPA3Enterprise or AuthMethod.WPA3Enterprise192;

    public ConnectDialog(string ssid, AuthMethod auth)
    {
        InitializeComponent();
        _auth = auth;
        _ssid = ssid;
        SsidLabel.Text = ssid;
        var badge = SecurityBadgeService.GetBadge(auth);
        AuthLabel.Text = L.SecurityLevelLabel(badge.Level) + $"  ({badge.TechLabel})";

        bool needsPassword = auth is not (AuthMethod.Open or AuthMethod.OWE);
        PasswordPanel.Visibility = needsPassword ? Visibility.Visible : Visibility.Collapsed;

        if (IsEnterprise)
        {
            // CLI (`mwc connect --eap-type`) と同じ 3 方式のみ提供する。
            // EAP-AKA は SIM ハードウェアを要し ProfileXmlBuilder が明示的に拒否するため出さない。
            EapTypeCombo.ItemsSource = new[]
            {
                EapType.PEAP_MSCHAPv2, EapType.EAP_TLS, EapType.EAP_TTLS
            };
            EapTypeCombo.SelectedIndex = 0;
            EnterprisePanel.Visibility = Visibility.Visible;
            UsernameBox.Focus();
        }
        else if (needsPassword)
        {
            PasswordBox.Focus();
        }
    }

    /// <summary>
    /// 選択中の EAP 種別。Enterprise 以外では null。
    /// </summary>
    private EapType? SelectedEapType =>
        IsEnterprise && EapTypeCombo.SelectedItem is EapType t ? t : null;

    // EAP-TLS はクライアント証明書で認証するためユーザー名/パスワードを取らない。
    // 該当欄を隠すことで「入れなければならない」という誤解を防ぐ
    // (ProfileXmlBuilder の検証も EAP-TLS では資格情報を要求しない)。
    private void OnEapTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsEnterprise) return;
        bool needsCredentials = SelectedEapType is not EapType.EAP_TLS;

        var vis = needsCredentials ? Visibility.Visible : Visibility.Collapsed;
        UsernameLabel.Visibility = vis;
        UsernameBox.Visibility   = vis;
        PasswordPanel.Visibility = vis;
    }

    /// <summary>
    /// 入力内容から接続用の <see cref="WifiProfileSpec"/> を組み立てる。
    /// Enterprise ではパスワード欄を EAP パスワードとして扱う
    /// (CLI の `-p` が PSK/EAP 兼用なのと同じ設計)。
    /// </summary>
    public WifiProfileSpec BuildSpec()
    {
        var password = ShowPwCheck.IsChecked == true
            ? PasswordVisible.Text : PasswordBox.Password;

        if (!IsEnterprise)
            return new WifiProfileSpec { Ssid = _ssid, Auth = _auth, Passphrase = password };

        var servers = string.IsNullOrWhiteSpace(ServerNameBox.Text)
            ? Array.Empty<string>()
            : ServerNameBox.Text.Split(';', StringSplitOptions.RemoveEmptyEntries
                                          | StringSplitOptions.TrimEntries);

        return new WifiProfileSpec
        {
            Ssid        = _ssid,
            Auth        = _auth,
            EapType     = SelectedEapType,
            Username    = string.IsNullOrWhiteSpace(UsernameBox.Text) ? null : UsernameBox.Text,
            Password    = string.IsNullOrEmpty(password) ? null : password,
            Domain      = string.IsNullOrWhiteSpace(IdentityBox.Text) ? null : IdentityBox.Text,
            ServerNames = servers,
        };
    }

    // パスフレーズ入力 → リアルタイム強度インジケーター
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var pw = ShowPwCheck.IsChecked == true
            ? PasswordVisible.Text
            : PasswordBox.Password;
        UpdateStrengthIndicator(pw);
        PasswordPlaceholder.Visibility =
            pw.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStrengthIndicator(string pw)
    {
        var (score, label, brushKey) = MeasureStrength(pw);
        var brush = (Brush)Application.Current.Resources[brushKey];
        StrengthBar.Width       = StrengthBarTrack.ActualWidth * score;
        StrengthBar.Background  = brush;
        StrengthLabel.Text       = pw.Length == 0 ? "" : label;
        StrengthLabel.Foreground = brush;
    }

    // 戻り値の色はテーマブラシのリソースキー名 (16進色の直書きではない) —
    // ハードコード色はテーマ切替 (ThemeService) を無視してしまう
    // (2026-07 品質パスで是正。既存の Danger/Warn/Success/Accent 各ブラシを
    // 意味的に再利用し、新規ブラシは追加しない)。
    private static (double score, string label, string brushKey) MeasureStrength(string pw)
    {
        if (pw.Length == 0) return (0, "", "FgMutedBrush");

        int pts = 0;
        if (pw.Length >= 8)  pts++;
        if (pw.Length >= 12) pts++;
        if (pw.Length >= 16) pts++;
        if (pw.Any(char.IsUpper)) pts++;
        if (pw.Any(char.IsLower)) pts++;
        if (pw.Any(char.IsDigit)) pts++;
        if (pw.Any(c => !char.IsLetterOrDigit(c))) pts++;

        return pts switch
        {
            <= 2 => (0.25, MWC.App.Resources.L.Get("Strength_Weak"),       "DangerBrush"),
            <= 4 => (0.5,  MWC.App.Resources.L.Get("Strength_Fair"),       "WarnBrush"),
            <= 5 => (0.75, MWC.App.Resources.L.Get("Strength_Strong"),     "SuccessBrush"),
            _    => (1.0,  MWC.App.Resources.L.Get("Strength_VeryStrong"), "AccentBrush")
        };
    }

    private void OnTogglePassword(object sender, RoutedEventArgs e)
    {
        if (ShowPwCheck.IsChecked == true)
        {
            PasswordVisible.Text = PasswordBox.Password;
            PasswordVisible.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordVisible.Focus();
        }
        else
        {
            PasswordBox.Password = PasswordVisible.Text;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordVisible.Visibility = Visibility.Collapsed;
            PasswordBox.Focus();
        }
        OnPasswordChanged(sender, e);
    }

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnConnect(sender, new RoutedEventArgs());
    }

    private void OnConnect(object sender, RoutedEventArgs e)
    {
        Passphrase = ShowPwCheck.IsChecked == true
            ? PasswordVisible.Text : PasswordBox.Password;

        if (IsEnterprise)
        {
            // Enterprise では PSK の長さ規則(8〜63 文字等)は適用されない。
            // PEAP / EAP-TTLS はユーザー名+パスワードが必須、EAP-TLS は証明書認証のため不要 —
            // この分岐は ProfileXmlBuilder の ValidateEnterprise と同じ規則。
            if (SelectedEapType is not EapType.EAP_TLS
                && (string.IsNullOrWhiteSpace(UsernameBox.Text) || string.IsNullOrEmpty(Passphrase)))
            {
                ErrorLabel.Text = MWC.App.Resources.L.Get("Error_EapUsernameRequired");
                ErrorLabel.Visibility = Visibility.Visible;
                return;
            }
        }
        else if (_auth is not (AuthMethod.Open or AuthMethod.OWE))
        {
            if (!IsPassphraseValid(Passphrase, _auth))
            {
                ErrorLabel.Text = MWC.App.Resources.L.Get("Error_PassphraseShort");
                ErrorLabel.Visibility = Visibility.Visible;
                return;
            }
        }
        DialogResult = true;
        Close();
    }

    // 認証方式ごとのパスフレーズ長検証。WEP は WPA と異なる鍵長 (5/13 ASCII or 10/26 hex) を持つため
    // 一律 8 文字以上では有効な WEP キーを誤って拒否してしまう (ProfileXmlBuilder の検証と整合)。
    private static bool IsPassphraseValid(string? passphrase, AuthMethod auth)
    {
        if (string.IsNullOrEmpty(passphrase)) return false;
        if (auth is AuthMethod.WEP)
        {
            int len = passphrase.Length;
            bool ascii = len is 5 or 13;
            bool hex   = len is 10 or 26 &&
                         passphrase.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F'));
            return ascii || hex;
        }
        // WPA/WPA2/WPA3 PSK: 8〜63 文字、または 64 桁 hex の raw PSK
        if (passphrase.Length == 64 &&
            passphrase.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F')))
            return true;
        return passphrase.Length is >= 8 and <= 63;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false; Close();
    }
}
