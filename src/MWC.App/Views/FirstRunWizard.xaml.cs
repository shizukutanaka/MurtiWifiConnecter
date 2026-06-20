using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MWC.App.Services;

namespace MWC.App.Views;

public partial class FirstRunWizard : Window
{
    private readonly SettingsService _settings;
    private int _page = 0;

    private static (string Icon, string Title, string Body, string Hint)[] BuildPages() => new[]
    {
        ("📡",
         MWC.App.Resources.L.Get("Wizard_Page1_Title"),
         MWC.App.Resources.L.Get("Wizard_Page1_Body"),
         MWC.App.Resources.L.Get("Wizard_Page1_Hint")),
        ("🔒",
         MWC.App.Resources.L.Get("Wizard_Page2_Title"),
         MWC.App.Resources.L.Get("Wizard_Page2_Body"),
         MWC.App.Resources.L.Get("Wizard_Page2_Hint")),
        ("⚙️",
         MWC.App.Resources.L.Get("Wizard_Page3_Title"),
         MWC.App.Resources.L.Get("Wizard_Page3_Body"),
         MWC.App.Resources.L.Get("Wizard_Page3_Hint")),
    };

    public FirstRunWizard(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
        ShowPage(0);
    }

    private void ShowPage(int idx)
    {
        _page = idx;
        var pages = BuildPages();
        int lastPage = pages.Length - 1;
        var (icon, title, body, hint) = pages[idx];

        // ページコンテンツ生成
        var panel = new StackPanel
        {
            Margin = new Thickness(40, 48, 40, 80),
            VerticalAlignment = VerticalAlignment.Top
        };

        panel.Children.Add(new TextBlock
        {
            Text = icon, FontSize = 52, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        });
        panel.Children.Add(new TextBlock
        {
            Text = title, FontSize = 22, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6E8EB")),
            Margin = new Thickness(0, 0, 0, 16)
        });
        panel.Children.Add(new TextBlock
        {
            Text = body, FontSize = 14, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, LineHeight = 22,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF")),
            Margin = new Thickness(0, 0, 0, 20)
        });

        // ヒントボックス
        var hintBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1D23")),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10)
        };
        hintBorder.Child = new TextBlock
        {
            Text = hint, FontSize = 12, TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00C4CC"))
        };
        panel.Children.Add(hintBorder);

        PageContainer.Children.Clear();
        PageContainer.Children.Add(panel);

        // ドット更新
        var dots = new[] { Dot1, Dot2, Dot3 };
        var on   = (Color)ColorConverter.ConvertFromString("#00C4CC");
        var off  = (Color)ColorConverter.ConvertFromString("#2B313A");
        for (int i = 0; i < dots.Length; i++)
            dots[i].Fill = new SolidColorBrush(i == idx ? on : off);

        // ナビゲーション
        BackBtn.Visibility = idx > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Content    = idx == lastPage ? MWC.App.Resources.L.Get("Wizard_Start") : MWC.App.Resources.L.Get("Wizard_Next");
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_page < BuildPages().Length - 1) { ShowPage(_page + 1); return; }
        _settings.Save(new AppSettings
        {
            HasCompletedFirstRun       = true,
            ScanOnStartup              = true,
            ShowConnectionNotifications = true,
            Language                   = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            AutoScanIntervalSeconds    = 15,
            DisplayMode                = DisplayMode.Simple,
            Theme                      = AppTheme.Dark
        });
        DialogResult = true;
        Close();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_page > 0) ShowPage(_page - 1);
    }
}
