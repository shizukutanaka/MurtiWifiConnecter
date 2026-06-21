using System;
using System.Windows;
using System.Windows.Navigation;
using MWC.App.Services;

namespace MWC.App.Views;

public partial class CaptivePortalDialog : Window
{
    private const string CaptiveProbe = "http://www.msftconnecttest.com/redirect";

    public CaptivePortalDialog(string ssid)
    {
        InitializeComponent();
        SsidLabel.Text = ssid;
        UrlLabel.Text  = CaptiveProbe;
        Browser.Navigate(new Uri(CaptiveProbe));
    }

    private void OnNavigating(object sender, NavigatingCancelEventArgs e)
    {
        UrlLabel.Text  = e.Uri?.ToString() ?? "";
        StatusLabel.Text = MWC.App.Resources.L.Get("Captive_Loading");
    }

    private void OnLoaded(object sender, NavigationEventArgs e)
    {
        var url = e.Uri?.ToString() ?? "";
        StatusLabel.Text = url.Contains("msftconnecttest.com/connecttest.txt")
            ? MWC.App.Resources.L.Get("Captive_InternetOk")
            : MWC.App.Resources.L.Get("Captive_PageLoaded");
    }

    private void OnOpenExternal(object sender, RoutedEventArgs e)
    {
        BrowserLauncher.OpenHttp(CaptiveProbe);
    }

    private void OnDone(object sender, RoutedEventArgs e) { DialogResult = true;  Close(); }
    private void OnSkip(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
