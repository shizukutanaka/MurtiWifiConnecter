using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using MWC.App.Resources;
using MWC.Core.Services;
using Serilog;

namespace MWC.App.Views;

public partial class CertificatePickerDialog : Window
{
    private readonly CertificateStoreService _svc;

    /// <summary>選択されたクライアント証明書(OK 時)</summary>
    public ClientCertInfo? SelectedCert { get; private set; }

    public CertificatePickerDialog(CertificateStoreService svc)
    {
        InitializeComponent();
        _svc = svc;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var certs = _svc.GetClientCertificates();
        var vms   = new List<CertViewModel>();

        foreach (var c in certs)
            vms.Add(new CertViewModel(c));

        CertList.ItemsSource = vms;

        if (vms.Count == 0)
        {
            OkButton.IsEnabled = false;
            // 証明書なしメッセージを表示
        }
    }

    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CertList.SelectedItem is not CertViewModel vm) return;

        OkButton.IsEnabled   = true;
        DetailPanel.Visibility = Visibility.Visible;

        SubjectLabel.Text     = vm.Cert.Subject;
        IssuerLabel.Text      = vm.Cert.Issuer;
        ExpiryDetailLabel.Text = L.CertPickerExpiryFormat(vm.Cert.NotAfter.ToString("yyyy-MM-dd"), vm.Cert.DaysUntilExpiry);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (CertList.SelectedItem is CertViewModel vm)
        {
            SelectedCert = vm.Cert;
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void OnOpenStore(object sender, RoutedEventArgs e)
    {
        // certmgr.msc を起動
        try { Process.Start(new ProcessStartInfo("certmgr.msc") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warning(ex, "Failed to open Windows Certificate Manager"); }
    }

    // ── ViewModel ───────────────────────────────────────────────────

    private sealed class CertViewModel
    {
        public ClientCertInfo Cert { get; }

        public CertViewModel(ClientCertInfo cert)
        {
            Cert           = cert;
            DisplayLabel   = cert.DisplayLabel;
            Issuer         = FormatIssuer(cert.Issuer);
            ExpiryLabel    = BuildExpiryLabel(cert.DaysUntilExpiry);
            ThumbprintShort = cert.Thumbprint.Length > 8 ? cert.Thumbprint[..8] + "…" : cert.Thumbprint;
            ExpiryColor    = ResolveExpiryBrush(cert.DaysUntilExpiry);
        }

        // 他の全要素は {DynamicResource ...Brush} でテーマに追従するが、この一覧は
        // C# 側で Brush を確定させる必要がある(バインディング先が ItemsSource の POCO)。
        // ハードコードした Brushes.OrangeRed 等は現在のテーマ(Dark/Light/Solarized/...)を
        // 無視し、アクセシビリティコントラスト監査の対象外になってしまうため、
        // 16-brush contract のキー (DangerBrush/WarnBrush/SuccessBrush) を都度解決する。
        private static Brush ResolveExpiryBrush(int daysUntilExpiry)
        {
            string key = daysUntilExpiry < 30 ? "DangerBrush"
                       : daysUntilExpiry < 90 ? "WarnBrush"
                       : "SuccessBrush";
            return (Application.Current?.TryFindResource(key) as Brush) ?? Brushes.Gray;
        }

        public string  DisplayLabel    { get; }
        public string  Issuer          { get; }
        public string  ExpiryLabel     { get; }
        public string  ThumbprintShort { get; }
        public Brush   ExpiryColor     { get; }

        private static string FormatIssuer(string issuer)
        {
            var cn = ExtractCn(issuer);
            return string.IsNullOrEmpty(cn) ? issuer : cn;
        }

        private static string? ExtractCn(string dn)
        {
            var idx = dn.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var start = idx + 3;
            var end   = dn.IndexOf(',', start);
            return end < 0 ? dn[start..] : dn[start..end];
        }

        private static string BuildExpiryLabel(int days) => days switch
        {
            < 0  => MWC.App.Resources.L.CertExpired,
            < 30 => MWC.App.Resources.L.CertExpirySoon(days),
            < 90 => MWC.App.Resources.L.CertExpiry90(days),
            _    => MWC.App.Resources.L.CertExpiryOk(days)
        };
    }
}
