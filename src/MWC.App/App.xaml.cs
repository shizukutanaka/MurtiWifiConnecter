using System;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using MWC.App.Services;
using MWC.App.ViewModels;
using MWC.App.Views;
using MWC.Core.Abstractions;
using MWC.Core.Services;
using MWC.App.Controls;
using MWC.Platform.Windows;

namespace MWC.App;

public partial class App : Application
{
    public static IHost Host { get; private set; } = null!;
    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MWC", "logs");
        Directory.CreateDirectory(logsDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logsDir, "mwc-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(s =>
            {
                // プラットフォーム層
                s.AddSingleton<IConnectivityChecker, HttpConnectivityChecker>();
                s.AddSingleton<ISecretProtector, DpapiSecretProtector>();
                s.AddSingleton<IWifiService, WindowsWifiService>();

                // Coreサービス
                s.AddSingleton<SignalHistoryService>();
                s.AddSingleton<OuiLookupService>();
                s.AddSingleton<SettingsService>();

                // Appサービス (NotifyIcon を先に生成して共有)
                s.AddSingleton<System.Windows.Forms.NotifyIcon>(_ =>
                {
                    var ni = new System.Windows.Forms.NotifyIcon
                    {
                        Text    = "MWC",
                        Visible = true,
                        Icon    = System.Drawing.SystemIcons.Information
                    };
                    return ni;
                });
                s.AddSingleton<NotificationService>(sp => new NotificationService(
                    sp.GetRequiredService<ILogger<NotificationService>>(),
                    sp.GetRequiredService<System.Windows.Forms.NotifyIcon>()));
                s.AddSingleton<SystemTrayService>(sp => new SystemTrayService(
                    sp.GetRequiredService<IWifiService>(),
                    Dispatcher.CurrentDispatcher,
                    sp.GetRequiredService<ILogger<SystemTrayService>>()));

                // ViewModels
                s.AddSingleton<NetworkFilterViewModel>(sp =>
                    new NetworkFilterViewModel(
                        sp.GetRequiredService<SettingsService>(),
                        sp.GetRequiredService<AdapterPreferencesService>()));
                s.AddSingleton<MainViewModel>(sp => new MainViewModel(
                    sp.GetRequiredService<IWifiService>(),
                    sp.GetRequiredService<ILogger<MainViewModel>>(),
                    sp.GetRequiredService<SignalHistoryService>(),
                    sp.GetRequiredService<OuiLookupService>(),
                    sp.GetRequiredService<NetworkFilterViewModel>(),
                    sp.GetRequiredService<AdapterPreferencesService>(),
                    sp.GetRequiredService<ConnectionExecutor>()));
                s.AddSingleton<SettingsViewModel>();
                s.AddTransient<NetworkDetailViewModel>();

                s.AddSingleton<ThemeService>();
                s.AddSingleton<JumpListService>();
                s.AddSingleton<AppUpdateService>();
                s.AddSingleton<NetworkHistoryService>();
                s.AddSingleton<ConnectionExecutor>();
                s.AddSingleton<AdapterPreferencesService>();
                s.AddSingleton<ErrorHandlerService>();
                s.AddSingleton<KeyboardShortcutService>();
                s.AddSingleton<MainWindowCommands>(sp => new MainWindowCommands(
                    sp.GetRequiredService<IWifiService>(),
                    sp.GetRequiredService<NotificationService>(),
                    sp.GetRequiredService<NetworkHistoryService>(),
                    sp.GetRequiredService<NetworkQualityService>(),
                    sp.GetRequiredService<SettingsService>(),
                    sp.GetRequiredService<ThemeService>(),
                    sp.GetRequiredService<ErrorHandlerService>(),
                    sp.GetRequiredService<KeyboardShortcutService>(),
                    sp));
                s.AddSingleton<NetworkQualityService>();
                s.AddTransient<AllAdaptersOverviewViewModel>(sp =>
                    new AllAdaptersOverviewViewModel(
                        sp.GetRequiredService<IWifiService>(),
                        sp.GetRequiredService<AdapterPreferencesService>(),
                        sp.GetRequiredService<NetworkHistoryService>(),
                        sp.GetRequiredService<ConnectionExecutor>(),
                        sp.GetRequiredService<OuiLookupService>(),
                        sp.GetRequiredService<ILogger<AllAdaptersOverviewViewModel>>()));
                s.AddSingleton<AutoReconnectService>();
                s.AddSingleton<AdapterFailoverService>();
                s.AddTransient<ProfileManagerViewModel>(sp => new ProfileManagerViewModel(
                    sp.GetRequiredService<IWifiService>(),
                    sp.GetRequiredService<NetworkHistoryService>()));

                // Views
                s.AddSingleton<MainWindow>();
                s.AddTransient<SettingsDialog>();
                s.AddTransient<FirstRunWizard>();
            })
            .Build();

        DispatcherUnhandledException         += OnUiUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
        TaskScheduler.UnobservedTaskException      += OnTaskUnhandled;

        // 言語適用: 保存された言語設定を UI カルチャへ反映する。これが無いと
        // CurrentUICulture が OS カルチャのままになり、設定の言語セレクタが
        // 事実上機能しない (選んでも表示言語が変わらない)。MainWindow 生成より
        // 前に行う必要がある (resx は CurrentUICulture で解決されるため)。
        // 言語変更は再起動で反映。
        ApplyLanguage(Host.Services.GetRequiredService<SettingsService>().Current.Language);

        // テーマ適用
        var theme = Host.Services.GetRequiredService<ThemeService>();
        theme.Apply(Host.Services.GetRequiredService<SettingsService>().Current.Theme);
        theme.StartSystemWatcher();

        // トレイ + 通知サービス起動
        var tray = Host.Services.GetRequiredService<SystemTrayService>();
        tray.RequestOpenMainWindow += BringToFront;

        // AutoReconnect 起動
        Host.Services.GetRequiredService<AutoReconnectService>().Start();

        // AdapterFailover 起動
        Host.Services.GetRequiredService<AdapterFailoverService>().Start();

        // MainWindow 表示
        var win = Host.Services.GetRequiredService<MainWindow>();
        win.DataContext = Host.Services.GetRequiredService<MainViewModel>();
        win.Show();

        // FirstRun チェック
        var settings = Host.Services.GetRequiredService<SettingsService>();
        if (!settings.Current.HasCompletedFirstRun)
        {
            var wizard = Host.Services.GetRequiredService<FirstRunWizard>();
            wizard.Owner = win;
            wizard.ShowDialog();
        }
    }

    /// <summary>
    /// 保存された言語コード ("en"/"ja"/"ar" 等) を UI/フォーマットカルチャへ適用する。
    /// RTL 言語 (アラビア語等) では全 FrameworkElement の既定 FlowDirection を右→左へ反転する。
    /// FrameworkElement 生成前に呼ぶこと (OverrideMetadata は一度きり)。
    /// </summary>
    private static void ApplyLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang) ||
            lang.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return;   // OS カルチャに従う

        System.Globalization.CultureInfo culture;
        try { culture = System.Globalization.CultureInfo.GetCultureInfo(lang); }
        catch (System.Globalization.CultureNotFoundException) { return; }

        System.Globalization.CultureInfo.CurrentUICulture          = culture;
        System.Globalization.CultureInfo.CurrentCulture            = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture   = culture;

        // RTL 言語ではレイアウトを右→左へ反転 (これが無いと UI が鏡像でなく
        // LTR のまま表示され、ラベル位置・整列が崩れる)。生成済み要素があると
        // OverrideMetadata が例外を投げるため best-effort: 失敗時は LTR のまま。
        if (culture.TextInfo.IsRightToLeft)
        {
            try
            {
                FrameworkElement.FlowDirectionProperty.OverrideMetadata(
                    typeof(FrameworkElement),
                    new FrameworkPropertyMetadata(FlowDirection.RightToLeft));
            }
            catch { /* 既に生成済み等 — LTR フォールバック */ }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // バックグラウンドタイマーをまず停止し、Host 破棄中に CheckAsync が
        // 破棄済みの _wifi を触る競合を防ぐ (AutoReconnect と対称に明示停止する)。
        Host?.Services.GetService<AdapterFailoverService>()?.Stop();

        // 監視ループの完了を待ってから破棄 (WatchAsync は ConfigureAwait(false) のためデッドロックしない)
        var autoReconnect = Host?.Services.GetService<AutoReconnectService>();
        if (autoReconnect is not null)
            autoReconnect.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Host?.Services.GetService<SystemTrayService>()?.Dispose();
        Host?.Services.GetService<System.Windows.Forms.NotifyIcon>()?.Dispose();
        Log.CloseAndFlush();
        Host?.Dispose();
        base.OnExit(e);
    }

    private void BringToFront()
    {
        var w = Host.Services.GetService<MainWindow>();
        if (w is null) return;
        w.Show();
        w.WindowState = WindowState.Normal;
        w.Activate();
    }

    private void OnUiUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "UI unhandled");
        MessageBox.Show(MWC.App.Resources.L.ErrorUnexpected(e.Exception.Message), "MWC",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) Log.Error(ex, "Domain unhandled");
    }

    private static void OnTaskUnhandled(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception (suppressed)");
        e.SetObserved();  // プロセス終了を防ぐ
    }
}
