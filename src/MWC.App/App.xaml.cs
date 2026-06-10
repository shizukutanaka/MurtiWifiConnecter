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
                    new NetworkFilterViewModel(sp.GetRequiredService<SettingsService>()));
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
                    sp.GetRequiredService<IWifiService>()));

                // Views
                s.AddSingleton<MainWindow>();
                s.AddTransient<SettingsDialog>();
                s.AddTransient<FirstRunWizard>();
            })
            .Build();

        DispatcherUnhandledException         += OnUiUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
        TaskScheduler.UnobservedTaskException      += OnTaskUnhandled;

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

    protected override void OnExit(ExitEventArgs e)
    {
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
