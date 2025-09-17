using System;
using System.Windows;
using System.Windows.Controls;

namespace MurtiWifiConnecter
{
    public partial class QuickSetupWizard : Window
    {
        private int _currentStep = 1;

        public QuickSetupWizard()
        {
            InitializeComponent();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateStepVisibility();
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 3)
            {
                _currentStep++;
                UpdateStepVisibility();
            }
            else
            {
                // Complete setup
                CompleteSetup();
            }
        }

        private void UpdateStepVisibility()
        {
            Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

            BackButton.IsEnabled = _currentStep > 1;
            NextButton.Content = _currentStep == 3 ? "完了" : "次へ";

            // Update progress bar
            ProgressBar.Value = _currentStep * 33;
        }

        private void CompleteSetup()
        {
            try
            {
                // Save settings
                var userName = UserNameTextBox.Text;
                var ssid = HomeNetworkSSID.Text;
                var password = string.Empty; // Password field will be added later if needed

                // Save settings to file
                var settingsPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MurtiWifiConnecter");

                if (!System.IO.Directory.Exists(settingsPath))
                {
                    System.IO.Directory.CreateDirectory(settingsPath);
                }

                var settingsFile = System.IO.Path.Combine(settingsPath, "home_wifi_settings.json");
                var settings = $"{{\"userName\": \"{userName}\", \"ssid\": \"{ssid}\"}}";
                System.IO.File.WriteAllText(settingsFile, settings);

                // Open main window
                var mainWindow = new MainWindow();
                mainWindow.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}