using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MurtiWifiConnecter
{
    public partial class App : Application
    {
        private static readonly string AppName = "MurtiWifiConnecter";
        private static readonly string LogFileName = $"{AppName}.log";
        private SimplifiedWifiManager? _wifiManager;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Setup exception handling
            SetupExceptionHandling();

            // Initialize logging
            InitializeLogging();

            Logger.Info("Application starting...");

            // Check for command line arguments
            if (e.Args.Length > 0)
            {
                HandleCommandLineArgs(e.Args);
                return;
            }

            // Check administrator privileges
            Task.Run(async () =>
            {
                await CheckPrivilegesAsync();
            });

            base.OnStartup(e);
        }

        private void SetupExceptionHandling()
        {
            Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error("UI Thread Exception", e.Exception);

            var result = MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nContinue running?",
                "Error",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (result == MessageBoxResult.Yes)
            {
                e.Handled = true;
            }
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Error("Unhandled Exception", ex);

                MessageBox.Show(
                    $"Fatal error occurred:\n\n{ex.Message}\n\nApplication will exit.",
                    "Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error("Unobserved Task Exception", e.Exception);
            e.SetObserved();
        }

        private void InitializeLogging()
        {
            try
            {
                var logPath = Path.Combine(GetApplicationDataPath(), LogFileName);
                var logDir = Path.GetDirectoryName(logPath);

                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                // Rotate log file if too large
                RotateLogFile(logPath);

                // Log startup
                File.AppendAllText(logPath,
                    $"\n=== {AppName} Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize logging: {ex.Message}");
            }
        }

        private async Task CheckPrivilegesAsync()
        {
            var isAdmin = CheckAdministratorPrivileges();

            if (!isAdmin)
            {
                await Current.Dispatcher.InvokeAsync(() =>
                {
                    var result = MessageBox.Show(
                        "This application works best with administrator privileges.\n" +
                        "Some features may be limited.\n\n" +
                        "Continue anyway?",
                        "Administrator Privileges",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                    {
                        Shutdown();
                    }
                });
            }
        }

        private void HandleCommandLineArgs(string[] args)
        {
            // Simple CLI mode
            if (args.Length > 0 && args[0] == "--help")
            {
                Console.WriteLine("MurtiWifiConnecter - WiFi Connection Manager");
                Console.WriteLine("Usage: MurtiWifiConnecter.exe [options]");
                Console.WriteLine("Options:");
                Console.WriteLine("  --help    Show this help message");
                Console.WriteLine("  --scan    Scan for available networks");
                Console.WriteLine("  --connect <SSID> <Password>    Connect to network");
            }
            else if (args.Length > 0 && args[0] == "--scan")
            {
                Task.Run(async () =>
                {
                    _wifiManager = new SimplifiedWifiManager();
                    var networks = await _wifiManager.ScanNetworksAsync();
                    Console.WriteLine("Available Networks:");
                    foreach (var network in networks)
                    {
                        Console.WriteLine($"  {network.SSID} - Signal: {network.SignalStrength}% - {network.Authentication}");
                    }
                    Shutdown();
                });
            }
            else if (args.Length >= 3 && args[0] == "--connect")
            {
                var ssid = args[1];
                var password = args[2];
                Task.Run(async () =>
                {
                    _wifiManager = new SimplifiedWifiManager();
                    var success = await _wifiManager.ConnectAsync(ssid, password);
                    Console.WriteLine(success ? $"Connected to {ssid}" : $"Failed to connect to {ssid}");
                    Shutdown();
                });
            }
            else
            {
                Console.WriteLine("Invalid arguments. Use --help for usage information.");
                Shutdown();
            }
        }

        private bool CheckAdministratorPrivileges()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private string GetVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "2.0.0";
            }
            catch
            {
                return "2.0.0";
            }
        }

        private string GetApplicationDataPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appDataPath, AppName);
        }

        private void RotateLogFile(string logPath)
        {
            try
            {
                if (File.Exists(logPath))
                {
                    var info = new FileInfo(logPath);
                    // Rotate if larger than 1MB
                    if (info.Length > 1024 * 1024)
                    {
                        var backupPath = Path.ChangeExtension(logPath, ".old.log");
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);
                        File.Move(logPath, backupPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Log rotation failed: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Logger.Info($"Application shutting down (Exit code: {e.ApplicationExitCode})");

                // Cleanup
                _wifiManager?.Dispose();
                Logger.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Shutdown cleanup failed: {ex.Message}");
            }

            base.OnExit(e);
        }
    }
}