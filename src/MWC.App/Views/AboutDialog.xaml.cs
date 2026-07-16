using System.Windows;
using System.Windows.Navigation;
using MWC.App.Services;

namespace MWC.App.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionLabel.Text = MWC.App.Resources.L.Format("About_Version", App.Version);
    }

    private void OnHyperlinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        // Open via the scheme-validated launcher (http/https only) rather than
        // shell-executing the raw NavigateUri — defense-in-depth at the sink.
        BrowserLauncher.OpenHttp(e.Uri);
        e.Handled = true;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
