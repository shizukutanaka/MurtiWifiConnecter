using System;
using System.Windows;

namespace MurtiWifiConnecter
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                // Initialize application
                var app = new Application();

                // 初回起動チェック
                var isFirstRun = IsFirstRun();

                if (isFirstRun)
                {
                    // 初回起動時は設定ウィザード表示
                    var wizard = new QuickSetupWizard();
                    app.MainWindow = wizard;
                    app.Run(wizard);
                }
                else
                {
                    // 通常起動時はメインウィンドウを表示
                    var mainWindow = new MainWindow();
                    app.MainWindow = mainWindow;
                    app.Run(mainWindow);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Application startup failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }
        
        private static bool IsFirstRun()
        {
            try
            {
                var settingsPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MurtiWifiConnecter", "home_wifi_settings.json");
                    
                return !System.IO.File.Exists(settingsPath);
            }
            catch
            {
                return true; // エラー時は初回起動として扱う
            }
        }
    }
}