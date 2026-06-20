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
            ThumbprintShort = cert.Thumbprint[..8] + "…";
            ExpiryColor    = cert.DaysUntilExpiry < 30 ? Brushes.OrangeRed
                           : cert.DaysUntilExpiry < 90 ? Brushes.Orange
                           : Brushes.LightGreen;
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
