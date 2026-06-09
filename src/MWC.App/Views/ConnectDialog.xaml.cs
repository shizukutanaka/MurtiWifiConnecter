using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.App.Views;

public partial class ConnectDialog : Window
{
    public string? Passphrase { get; private set; }
    private readonly AuthMethod _auth;

    public ConnectDialog(string ssid, AuthMethod auth)
    {
        InitializeComponent();
        _auth = auth;
        SsidLabel.Text = ssid;
        AuthLabel.Text = SecurityBadgeService.GetBadge(auth).Label
                       + $"  ({SecurityBadgeService.GetBadge(auth).TechLabel})";

        bool needsPassword = auth is not (AuthMethod.Open or AuthMethod.OWE);
        PasswordPanel.Visibility = needsPassword ? Visibility.Visible : Visibility.Collapsed;
        if (needsPassword) PasswordBox.Focus();
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
        var (score, label, color) = MeasureStrength(pw);
        StrengthBar.Width = StrengthBarTrack.ActualWidth * score;
        StrengthBar.Fill  = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(color));
        StrengthLabel.Text = pw.Length == 0 ? "" : label;
        StrengthLabel.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(color));
    }

    private static (double score, string label, string color) MeasureStrength(string pw)
    {
        if (pw.Length == 0) return (0, "", "#9CA3AF");

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
            <= 2 => (0.25, MWC.App.Resources.L.Get("Strength_Weak"),   "#EF4444"),
            <= 4 => (0.5,  MWC.App.Resources.L.Get("Strength_Fair"),   "#F59E0B"),
            <= 5 => (0.75, MWC.App.Resources.L.Get("Strength_Strong"),   "#22C55E"),
            _    => (1.0,  MWC.App.Resources.L.Get("Strength_VeryStrong"), "#00C4CC")
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

        if (_auth is not (AuthMethod.Open or AuthMethod.OWE))
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
