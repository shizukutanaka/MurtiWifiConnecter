using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MurtiWifiConnecter.Services;

namespace MurtiWifiConnecter
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // CLIモードチェック
            if (args != null && args.Length > 0 && (args[0] == "--cli" || args[0] == "-c"))
            {
                // CLIモード
                RunCLI(args.Skip(1).ToArray()).Wait();
                return;
            }

            try
            {
                // 軽量な初期化
                InitializeBasicServices();
                
                // Initialize application
                var app = new Application();
                
                // Setup application shutdown
                app.Exit += OnApplicationExit;
                
                // Create and show main window
                var mainWindow = new MainWindow();
                app.MainWindow = mainWindow;
                
                // Run application
                app.Run(mainWindow);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Application startup failed: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }

        private static async Task RunCLI(string[] args)
        {
            try
            {
                Console.WriteLine("Starting MurtiWifi Connector in CLI mode...\n");
                
                using var cli = new SimpleCLI();
                await cli.RunAsync(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CLI Error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private static void InitializeBasicServices()
        {
            try
            {
                SimpleLoggingService.LogInfo("Application starting...");
                
                // 基本的なディレクトリの作成
                FileManager.Initialize();
                
                SimpleLoggingService.LogInfo("Basic services initialized successfully");
            }
            catch (Exception ex)
            {
                SimpleLoggingService.LogError("Service initialization failed", ex);
                throw;
            }
        }
        
        private static void OnApplicationExit(object sender, ExitEventArgs e)
        {
            try
            {
                SimpleLoggingService.LogInfo("Application shutting down...");
                SimpleLoggingService.LogInfo("Application shutdown complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during shutdown: {ex.Message}");
            }
        }
    }
}