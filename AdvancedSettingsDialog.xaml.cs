using System;
using System.Windows;
using System.IO;
using Microsoft.Win32;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    public partial class AdvancedSettingsDialog : Window
    {
        private readonly AdvancedSettings _originalSettings;
        private AdvancedSettings _currentSettings;

        public AdvancedSettingsDialog()
        {
            InitializeComponent();
            _originalSettings = AdvancedSettings.Load();
            _currentSettings = _originalSettings.Clone();
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                // Connection Settings
                AutoConnectEnabled.IsChecked = _currentSettings.Connection.AutoConnectEnabled;
                RememberPasswords.IsChecked = _currentSettings.Connection.RememberPasswords;
                ConnectionTimeout.Value = _currentSettings.Connection.TimeoutSeconds;
                PreferStrongerSignal.IsChecked = _currentSettings.Connection.PreferStrongerSignal;
                PreferSecureNetworks.IsChecked = _currentSettings.Connection.PreferSecureNetworks;
                AvoidPublicNetworks.IsChecked = _currentSettings.Connection.AvoidPublicNetworks;
                MinSignalStrength.Value = _currentSettings.Connection.MinSignalStrength;

                // Security Settings
                EnableSecurityAnalysis.IsChecked = _currentSettings.Security.EnableSecurityAnalysis;
                WarnUnsecureNetworks.IsChecked = _currentSettings.Security.WarnUnsecureNetworks;
                DetectHotspots.IsChecked = _currentSettings.Security.DetectHotspots;
                BlockSuspiciousNetworks.IsChecked = _currentSettings.Security.BlockSuspiciousNetworks;
                EnforceStrongPasswords.IsChecked = _currentSettings.Security.EnforceStrongPasswords;
                MinPasswordLength.Value = _currentSettings.Security.MinPasswordLength;

                // Performance Settings
                ScanInterval.Value = _currentSettings.Performance.ScanIntervalSeconds;
                CacheTimeout.Value = _currentSettings.Performance.CacheTimeoutSeconds;
                AutoMemoryOptimization.IsChecked = _currentSettings.Performance.AutoMemoryOptimization;
                MemoryThreshold.Value = _currentSettings.Performance.MemoryThresholdMB;
                PowerOptimization.IsChecked = _currentSettings.Performance.PowerOptimization;
                ReduceBackgroundScanning.IsChecked = _currentSettings.Performance.ReduceBackgroundScanning;
                SetPowerProfileSelection(_currentSettings.Performance.PowerProfile);

                // Maintenance Settings
                EnableAutoMaintenance.IsChecked = _currentSettings.Maintenance.EnableAutoMaintenance;
                MaintenanceInterval.Value = _currentSettings.Maintenance.IntervalHours;
                EnableLogging.IsChecked = _currentSettings.Maintenance.EnableLogging;
                LogRetentionDays.Value = _currentSettings.Maintenance.LogRetentionDays;
                AutoBackupProfiles.IsChecked = _currentSettings.Maintenance.AutoBackupProfiles;
                BackupRetentionCount.Value = _currentSettings.Maintenance.BackupRetentionCount;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("AdvancedSettingsDialog.LoadSettings", ex);
                MessageBox.Show($"設定の読み込み中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveSettings()
        {
            try
            {
                // Connection Settings
                _currentSettings.Connection.AutoConnectEnabled = AutoConnectEnabled.IsChecked == true;
                _currentSettings.Connection.RememberPasswords = RememberPasswords.IsChecked == true;
                _currentSettings.Connection.TimeoutSeconds = (int)ConnectionTimeout.Value;
                _currentSettings.Connection.PreferStrongerSignal = PreferStrongerSignal.IsChecked == true;
                _currentSettings.Connection.PreferSecureNetworks = PreferSecureNetworks.IsChecked == true;
                _currentSettings.Connection.AvoidPublicNetworks = AvoidPublicNetworks.IsChecked == true;
                _currentSettings.Connection.MinSignalStrength = (int)MinSignalStrength.Value;

                // Security Settings
                _currentSettings.Security.EnableSecurityAnalysis = EnableSecurityAnalysis.IsChecked == true;
                _currentSettings.Security.WarnUnsecureNetworks = WarnUnsecureNetworks.IsChecked == true;
                _currentSettings.Security.DetectHotspots = DetectHotspots.IsChecked == true;
                _currentSettings.Security.BlockSuspiciousNetworks = BlockSuspiciousNetworks.IsChecked == true;
                _currentSettings.Security.EnforceStrongPasswords = EnforceStrongPasswords.IsChecked == true;
                _currentSettings.Security.MinPasswordLength = (int)MinPasswordLength.Value;

                // Performance Settings
                _currentSettings.Performance.ScanIntervalSeconds = (int)ScanInterval.Value;
                _currentSettings.Performance.CacheTimeoutSeconds = (int)CacheTimeout.Value;
                _currentSettings.Performance.AutoMemoryOptimization = AutoMemoryOptimization.IsChecked == true;
                _currentSettings.Performance.MemoryThresholdMB = (int)MemoryThreshold.Value;
                _currentSettings.Performance.PowerOptimization = PowerOptimization.IsChecked == true;
                _currentSettings.Performance.ReduceBackgroundScanning = ReduceBackgroundScanning.IsChecked == true;
                _currentSettings.Performance.PowerProfile = GetSelectedPowerProfile();

                // Maintenance Settings
                _currentSettings.Maintenance.EnableAutoMaintenance = EnableAutoMaintenance.IsChecked == true;
                _currentSettings.Maintenance.IntervalHours = (int)MaintenanceInterval.Value;
                _currentSettings.Maintenance.EnableLogging = EnableLogging.IsChecked == true;
                _currentSettings.Maintenance.LogRetentionDays = (int)LogRetentionDays.Value;
                _currentSettings.Maintenance.AutoBackupProfiles = AutoBackupProfiles.IsChecked == true;
                _currentSettings.Maintenance.BackupRetentionCount = (int)BackupRetentionCount.Value;

                _currentSettings.Save();
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("AdvancedSettingsDialog.SaveSettings", ex);
                throw;
            }
        }

        private void SetPowerProfileSelection(string powerProfile)
        {
            foreach (var item in PowerProfile.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem comboItem)
                {
                    if (comboItem.Tag?.ToString() == powerProfile)
                    {
                        comboItem.IsSelected = true;
                        break;
                    }
                }
            }
        }

        private string GetSelectedPowerProfile()
        {
            if (PowerProfile.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                return selectedItem.Tag?.ToString() ?? "balanced";
            }
            return "balanced";
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveSettings();
                await Task.Delay(100).ConfigureAwait(false);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("AdvancedSettingsDialog.SaveButton_Click", ex);
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveSettings();
                await Task.Delay(100).ConfigureAwait(false);
                MessageBox.Show("設定を適用しました。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("AdvancedSettingsDialog.ApplyButton_Click", ex);
                MessageBox.Show($"設定の適用中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("すべての設定を初期値に戻しますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _currentSettings = AdvancedSettings.CreateDefault();
                LoadSettings();
                MessageBox.Show("設定を初期値に戻しました。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("すべてのログファイルを削除しますか？この操作は元に戻せません。", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await FileManager.CleanupOldFilesAsync(TimeSpan.Zero).ConfigureAwait(false);
                    MessageBox.Show("ログファイルを削除しました。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError("AdvancedSettingsDialog.ClearLogsButton_Click", ex);
                    MessageBox.Show($"ログの削除中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Title = "ログファイルをエクスポート",
                    Filter = "ZIPファイル (*.zip)|*.zip",
                    DefaultExt = "zip",
                    FileName = $"MurtiWifiConnector_Logs_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    await FileManager.ExportLogsAsync(saveDialog.FileName).ConfigureAwait(false);
                    MessageBox.Show($"ログファイルをエクスポートしました: {saveDialog.FileName}", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("AdvancedSettingsDialog.ExportLogsButton_Click", ex);
                MessageBox.Show($"ログのエクスポート中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}