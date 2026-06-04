using System.Windows;
using MWC.Core.Services;

namespace MWC.App.Views;

public partial class TroubleshootingDialog : Window
{
    public bool ShouldRetry { get; private set; }

    public TroubleshootingDialog(string ssid, TroubleshootingAdvice advice)
    {
        InitializeComponent();
        IconLabel.Text   = advice.Icon;
        TitleLabel.Text  = advice.Title;
        SsidLabel.Text   = ssid;
        ReasonLabel.Text = advice.Reason;
        StepsList.ItemsSource = advice.Steps;
    }

    private void OnRetry(object sender, RoutedEventArgs e)
    {
        ShouldRetry  = true;
        DialogResult = true;
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        ShouldRetry  = false;
        DialogResult = false;
        Close();
    }
}
