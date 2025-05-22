using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MurtiWifiConnecter.Properties;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public ObservableCollection<WifiNetwork> WifiNetworks { get; set; } = new();

        public MainWindow()
        {
            InitializeComponent();
            WifiListBox.ItemsSource = WifiNetworks;
            Loaded += MainWindow_Loaded;

            // 設定から言語復元
            string savedLang = Properties.Settings.Default.UserLanguage;
            if (!string.IsNullOrEmpty(savedLang))
            {
                foreach (ComboBoxItem item in LanguageComboBox.Items)
                {
                    if ((item.Tag as string) == savedLang)
                    {
                        item.IsSelected = true;
                        break;
                    }
                }
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadWifiNetworksAsync();
            // 自動リフレッシュ（10秒ごと）
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(10);
            timer.Tick += async (s, args) => await LoadWifiNetworksAsync();
            timer.Start();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadWifiNetworksAsync();
        }

        private async Task LoadWifiNetworksAsync()
        {
            try
            {
                string connectedSsid = await GetCurrentConnectedSsidAsync();
                var wifiList = new List<WifiNetwork>();
                await Task.Run(() =>
                {
                    var obj = new ManagementObjectSearcher("SELECT * FROM MSNdis_80211_BSSIList").Get().Cast<ManagementBaseObject>().FirstOrDefault();
                    if (obj == null) return;
                    var bssilist = (ManagementBaseObject[])obj["Ndis80211BSSIList"];
                    if (bssilist == null) return;
                    var ssidSet = new HashSet<string>();
                    foreach (var bss in bssilist)
                    {
                        string ssid = GetSsidFromBss(bss);
                        if (string.IsNullOrEmpty(ssid) || !ssidSet.Add(ssid)) continue;
                        int signal = 0;
                        try
                        {
                            if (bss.Properties["Ndis80211Rssi"]?.Value is int rssi)
                            {
                                signal = Math.Max(0, Math.Min(100, 2 * (rssi + 100)));
                            }
                        }
                        catch { }
                        bool isConnected = !string.IsNullOrEmpty(connectedSsid) && ssid == connectedSsid;
                        wifiList.Add(new WifiNetwork { SSID = ssid, SignalStrength = signal, IsConnected = isConnected });
                    }
                });
                // ソート: 接続中→強度順
                var sorted = wifiList.OrderByDescending(w => w.IsConnected).ThenByDescending(w => w.SignalStrength).ToList();
                App.Current.Dispatcher.Invoke(() =>
                {
                    WifiNetworks.Clear();
                    foreach (var wifi in sorted)
                        WifiNetworks.Add(wifi);
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"WiFi一覧の取得に失敗しました: {ex.Message}");
            }
        }

        private async Task<string> GetCurrentConnectedSsidAsync()
        {
            // netsh wlan show interfaces で現在のSSIDを取得
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("netsh", "wlan show interfaces")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    var lines = output.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.TrimStart().StartsWith("SSID", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = line.Split(':');
                            if (parts.Length > 1)
                                return parts[1].Trim();
                        }
                    }
                }
                catch { }
                return null;
            });
        }

        private string GetSsidFromBss(ManagementBaseObject bss)
        {
            try
            {
                var ssidBytes = (byte[])bss["Ndis80211Ssid"];
                if (ssidBytes == null) return null;
                return System.Text.Encoding.ASCII.GetString(ssidBytes).TrimEnd('\0');
            }
            catch
            {
                return null;
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (WifiListBox.SelectedItem is not WifiNetwork selected)
            {
                System.Windows.MessageBox.Show("接続するWiFiを選択してください。");
                return;
            }
            string password = PasswordBox.Password;
            bool success = await ConnectToWifiAsync(selected.SSID, password);
            System.Windows.MessageBox.Show(success ? $"{selected.SSID}に接続しました。" : $"{selected.SSID}への接続に失敗しました。");
        }

        private Task<bool> ConnectToWifiAsync(string ssid, string password)
        {
            return Task.Run(() =>
            {
                try
                {
                    string profileXml = $@"<?xml version=""1.0""?><WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1""><name>{ssid}</name><SSIDConfig><SSID><name>{ssid}</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security><authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption><sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{password}</keyMaterial></sharedKey></security></MSM></WLANProfile>";
                    string xmlPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wifi_{ssid}.xml");
                    System.IO.File.WriteAllText(xmlPath, profileXml);
                    var addProc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"wlan add profile filename=\"{xmlPath}\" user=all",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    addProc.WaitForExit();
                    var connectProc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"wlan connect name=\"{ssid}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    connectProc.WaitForExit();
                    return connectProc.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
            {
                try
                {
                    var culture = new System.Globalization.CultureInfo(langCode);
                    System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
                    System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                    Properties.Resources.Culture = culture;
                    TitleTextBlock.Text = Properties.Resources.AvailableNetworks;
                    ConnectButton.Content = Properties.Resources.Connect;
                    PasswordPlaceholderText.Text = Properties.Resources.PasswordPlaceholder;
                    // 言語設定保存
                    Properties.Settings.Default.UserLanguage = langCode;
                    Properties.Settings.Default.Save();
                    string fontKey = "FontFamilyDefault";
                    switch (langCode)
                    {
                        case "ja": fontKey = "FontFamilyJa"; break;
                        case "zh": fontKey = "FontFamilyZh"; break;
                        case "ru": fontKey = "FontFamilyRu"; break;
                        case "ar": fontKey = "FontFamilyAr"; break;
                        case "fr": fontKey = "FontFamilyFr"; break;
                        case "de": fontKey = "FontFamilyFr"; break;
                        case "es": fontKey = "FontFamilyFr"; break;
                        case "it": fontKey = "FontFamilyFr"; break;
                        case "ko": fontKey = "FontFamilyDefault"; break;
                    }
                    var font = (System.Windows.Media.FontFamily)FindResource(fontKey);
                    TitleTextBlock.FontFamily = font;
                    ConnectButton.FontFamily = font;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"言語切替に失敗しました: {ex.Message}");
                }
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            PasswordPlaceholderText.Visibility = string.IsNullOrEmpty(PasswordBox.Password)
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}