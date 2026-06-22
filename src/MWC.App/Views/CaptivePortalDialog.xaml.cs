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
        // 埋め込み WebBrowser (レガシー IE エンジン) は信頼できない可能性のある
        // キャプティブポータルを描画する。悪意あるポータルが file:// やカスタムスキームへ
        // リダイレクトしてローカルファイル開示・スキーム悪用を狙う恐れがあるため、
        // http/https 以外の絶対 URI へのナビゲーションは拒否する (R6 BrowserLauncher と
        // 同じ多層防御をエンジン内部のナビゲーションにも適用)。
        if (e.Uri is { IsAbsoluteUri: true } uri &&
            uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            // URL 自体はログに残さず、ブロックの事実のみ記録する。
            Serilog.Log.Warning("Captive portal blocked a non-http(s) navigation (scheme blocked for safety)");
            e.Cancel = true;
            return;
        }
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

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        e.Handled = true;
        StatusLabel.Text = MWC.App.Resources.L.Get("Captive_NavigationFailed");
        Serilog.Log.Warning("Captive portal navigation failed (error handled in UI)");
    }

    private void OnOpenExternal(object sender, RoutedEventArgs e)
    {
        BrowserLauncher.OpenHttp(CaptiveProbe);
    }

    private void OnDone(object sender, RoutedEventArgs e) { DialogResult = true;  Close(); }
    private void OnSkip(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
