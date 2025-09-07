using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MurtiWifiConnecter.Properties;

namespace MurtiWifiConnecter
{
    public enum PasswordStrength
    {
        VeryWeak = 0,
        Weak = 1,
        Fair = 2,
        Strong = 3,
        VeryStrong = 4
    }
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private System.Windows.Threading.DispatcherTimer? _refreshTimer;
        private readonly SemaphoreSlim _scanSemaphore = new(1, 1);
        private readonly ConnectionHistory _connectionHistory = new();
        private readonly WifiProfileManager _profileManager = new();
        private readonly NetworkStatusMonitor _networkMonitor = new();
        private readonly AppSettings _appSettings = new();
        private readonly ConnectionStatistics _connectionStats = new();
        private readonly ConnectionLogger _connectionLogger = new();

        public ObservableCollection<WifiNetwork> WifiNetworks { get; set; } = new();
        
        private PasswordStrength _passwordStrength = PasswordStrength.VeryWeak;
        public PasswordStrength PasswordStrength 
        { 
            get => _passwordStrength; 
            set 
            { 
                _passwordStrength = value;
                // PropertyChanged通知は簡略化のため省略
            } 
        }

        public MainWindow()
        {
            InitializeComponent();
            WifiListBox.ItemsSource = WifiNetworks;
            Loaded += MainWindow_Loaded;
            StateChanged += MainWindow_StateChanged;
            
            // ネットワーク状態監視
            _networkMonitor.StatusChanged += OnNetworkStatusChanged;

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
            try
            {
                await LoadWifiNetworksAsync();
                // 自動リフレッシュ（設定可能間隔）
                _refreshTimer = new System.Windows.Threading.DispatcherTimer();
                _refreshTimer.Interval = TimeSpan.FromSeconds(_appSettings.Configuration.RefreshIntervalSeconds);
                _refreshTimer.Tick += async (s, args) => 
                {
                    if (_scanSemaphore.CurrentCount == 0) return; // スキャン中はスキップ
                    await LoadWifiNetworksAsync();
                };
                _refreshTimer.Start();

                // 起動後のバックグラウンドタスクを遅延実行（軽量化）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // メモリ最適化を開始
                        MemoryOptimizer.StartMemoryMonitoring();
                        
                        // 起動完了を待つ
                        await Task.Delay(3000);
                        
                        // プロファイルクリーンアップ
                        if (_appSettings.Configuration.AutoCleanupProfiles)
                        {
                            await _profileManager.CleanupOldProfilesAsync(_appSettings.Configuration.MaxProfileHistory);
                        }
                        
                        // 古い統計データクリーンアップ（30日以上前）
                        _connectionStats.CleanupOldData(TimeSpan.FromDays(30));
                        
                        // 接続履歴の最適化
                        _connectionHistory.OptimizeStorage();
                        _connectionHistory.CleanupOldEntries(90);
                        
                        // 障害検出器を初期化（削除済み）
                        
                        // 初期ガベージコレクション実行
                        MemoryOptimizer.ForceGarbageCollection();
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler.LogError("MainWindow.StartupBackgroundTasks", ex, _connectionLogger);
                    }
                });
                
                // ウィンドウ設定の復元
                RestoreWindowSettings();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"初期化に失敗しました: {ex.Message}");
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_scanSemaphore.CurrentCount == 0)
            {
                System.Windows.MessageBox.Show("スキャン中です。しばらくお待ちください。");
                return;
            }
            await LoadWifiNetworksAsync();
        }

        private async Task LoadWifiNetworksAsync()
        {
            if (!await _scanSemaphore.WaitAsync(100))
                return; // 他のスキャンが実行中

            try
            {
                string connectedSsid = await GetCurrentConnectedSsidAsync(_cancellationTokenSource.Token);
                var wifiList = new List<WifiNetwork>();
                
                await Task.Run(() =>
                {
                    ManagementObjectSearcher searcher = null;
                    ManagementObjectCollection results = null;
                    try
                    {
                        searcher = new ManagementObjectSearcher("SELECT * FROM MSNdis_80211_BSSIList");
                        results = searcher.Get();
                        var obj = results.Cast<ManagementBaseObject>().FirstOrDefault();
                        if (obj == null) return;
                        
                        var bssilist = (ManagementBaseObject[])obj["Ndis80211BSSIList"];
                        if (bssilist == null) return;
                        
                        var ssidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        // バッチ処理で履歴チェックを最適化
                        var tempWifiList = new List<(string ssid, int signal, bool isConnected)>();
                        
                        foreach (var bss in bssilist)
                        {
                            if (_cancellationTokenSource.Token.IsCancellationRequested) break;
                            
                            string ssid = GetSsidFromBss(bss);
                            if (string.IsNullOrWhiteSpace(ssid) || ssid.Length > 32 || !ssidSet.Add(ssid)) continue;
                            
                            int rssi = 0;
                            try
                            {
                                if (bss.Properties["Ndis80211Rssi"]?.Value is int rssiValue)
                                    rssi = rssiValue;
                            }
                            catch { }
                            int signal = NetworkUtils.CalculateSignalStrength(rssi);
                            bool isConnected = !string.IsNullOrEmpty(connectedSsid) && 
                                             string.Equals(ssid, connectedSsid, StringComparison.OrdinalIgnoreCase);
                            
                            tempWifiList.Add((ssid, signal, isConnected));
                        }

                        // 履歴チェックを一括で実行し、統計情報も記録
                        foreach (var (ssid, signal, isConnected) in tempWifiList)
                        {
                            bool hasConnectedBefore = _connectionHistory.HasConnectedBefore(ssid);
                            
                            // 信号強度を統計に記録
                            _connectionStats.RecordSignalStrength(ssid, signal);
                            
                            wifiList.Add(new WifiNetwork 
                            { 
                                SSID = ssid, 
                                SignalStrength = signal, 
                                IsConnected = isConnected,
                                HasConnectedBefore = hasConnectedBefore 
                            });
                        }
                    }
                    finally
                    {
                        results?.Dispose();
                        searcher?.Dispose();
                    }
                }, _cancellationTokenSource.Token);

                if (_cancellationTokenSource.Token.IsCancellationRequested) return;

                // ソート: 接続中→履歴あり→強度順
                var sorted = wifiList
                    .OrderByDescending(w => w.IsConnected)
                    .ThenByDescending(w => w.HasConnectedBefore)
                    .ThenByDescending(w => w.SignalStrength)
                    .ThenBy(w => w.SSID)
                    .ToList();

                if (!_cancellationTokenSource.Token.IsCancellationRequested && App.Current?.Dispatcher != null)
                {
                    await App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // UI更新を効率化
                        WifiNetworks.Clear();
                        var displayNetworks = sorted.Take(_appSettings.Configuration.MaxDisplayedNetworks);
                        foreach (var wifi in displayNetworks)
                            WifiNetworks.Add(wifi);
                        
                        // クイック接続ボタンの表示制御（キャッシュ済みデータを使用）
                        var hasHistory = displayNetworks.Any(w => w.HasConnectedBefore);
                        QuickConnectButton.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセルされた場合は無視
            }
            catch (Exception ex)
            {
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await App.Current?.Dispatcher?.InvokeAsync(() =>
                        System.Windows.MessageBox.Show($"WiFiスキャンエラー: {ex.Message}"));
                }
            }
            finally
            {
                _scanSemaphore.Release();
            }
        }

        private async Task<string> GetCurrentConnectedSsidAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                Process proc = null;
                try
                {
                    var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    
                    proc = Process.Start(psi);
                    if (proc == null) return null;
                    
                    if (!proc.WaitForExit(5000)) // 5秒タイムアウト
                    {
                        proc.Kill();
                        return null;
                    }
                    
                    string output = proc.StandardOutput.ReadToEnd();
                    if (proc.ExitCode != 0) return null;
                    
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();
                        if (trimmedLine.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && 
                            !trimmedLine.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                        {
                            var colonIndex = trimmedLine.IndexOf(':');
                            if (colonIndex > 0 && colonIndex < trimmedLine.Length - 1)
                            {
                                var ssid = trimmedLine.Substring(colonIndex + 1).Trim();
                                return string.IsNullOrWhiteSpace(ssid) ? null : ssid;
                            }
                        }
                    }
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    // キャンセル時は無視
                }
                catch
                {
                    // その他のエラーも無視（ログは出さない）
                }
                finally
                {
                    try { proc?.Kill(); } catch { }
                    proc?.Dispose();
                }
                return null;
            }, cancellationToken);
        }


        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (WifiListBox.SelectedItem is not WifiNetwork selected)
            {
                System.Windows.MessageBox.Show("接続するWiFiを選択してください。");
                return;
            }

            if (selected.IsConnected)
            {
                System.Windows.MessageBox.Show($"{selected.SSID}は既に接続されています。");
                return;
            }

            string password = PasswordBox.Password;
            ConnectButton.IsEnabled = false;
            ConnectButton.Content = "接続中...";
            
            try
            {
                var result = await ConnectToWifiAsync(selected.SSID, password, _cancellationTokenSource.Token);
                
                if (result.Success)
                {
                    _connectionHistory.AddSuccessfulConnection(selected.SSID);
                    System.Windows.MessageBox.Show($"{selected.SSID}に正常に接続しました。");
                    PasswordBox.Password = ""; // パスワードをクリア
                    await LoadWifiNetworksAsync(); // ステータス更新
                }
                else
                {
                    string errorDetail = string.IsNullOrEmpty(result.ErrorMessage) ? 
                        "不明なエラー" : result.ErrorMessage;
                    System.Windows.MessageBox.Show($"{selected.SSID}への接続に失敗しました。\n詳細: {errorDetail}");
                }
            }
            catch (OperationCanceledException)
            {
                System.Windows.MessageBox.Show("接続がキャンセルされました。");
            }
            finally
            {
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = Properties.Resources.Connect;
            }
        }

        private Task<WifiConnectionResult> ConnectToWifiAsync(string ssid, string password, CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                string xmlPath = null;
                Process addProc = null;
                Process connectProc = null;
                
                try
                {
                    // 入力検証
                    if (string.IsNullOrWhiteSpace(ssid))
                        return new WifiConnectionResult { Success = false, ErrorMessage = "SSIDが空です" };
                    
                    if (string.IsNullOrEmpty(password))
                        return new WifiConnectionResult { Success = false, ErrorMessage = "パスワードが空です" };

                    // XMLプロファイル作成（セキュア）
                    string safePassword = System.Security.SecurityElement.Escape(password);
                    string safeSsid = System.Security.SecurityElement.Escape(ssid);
                    
                    string profileXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{safeSsid}</name>
    <SSIDConfig>
        <SSID>
            <name>{safeSsid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{safePassword}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";

                    // 一時ファイル作成
                    string tempDir = Path.GetTempPath();
                    string safeFileName = new string(ssid.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').Take(20).ToArray());
                    xmlPath = Path.Combine(tempDir, $"wifi_{safeFileName}_{Guid.NewGuid():N}.xml");
                    await File.WriteAllTextAsync(xmlPath, profileXml, cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();

                    // プロファイル追加
                    addProc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"wlan add profile filename=\"{xmlPath}\" user=current",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        }
                    };

                    addProc.Start();
                    if (!addProc.WaitForExit(10000))
                    {
                        addProc.Kill();
                        return new WifiConnectionResult { Success = false, ErrorMessage = "プロファイル追加がタイムアウトしました" };
                    }

                    if (addProc.ExitCode != 0)
                    {
                        string error = addProc.StandardError.ReadToEnd();
                        return new WifiConnectionResult { Success = false, ErrorMessage = $"プロファイル追加エラー: {error}" };
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // WiFi接続
                    connectProc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"wlan connect name=\"{safeSsid}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        }
                    };

                    connectProc.Start();
                    if (!connectProc.WaitForExit(15000))
                    {
                        connectProc.Kill();
                        return new WifiConnectionResult { Success = false, ErrorMessage = "接続がタイムアウトしました" };
                    }

                    if (connectProc.ExitCode != 0)
                    {
                        string error = connectProc.StandardError.ReadToEnd();
                        return new WifiConnectionResult { Success = false, ErrorMessage = $"接続エラー: {error}" };
                    }

                    return new WifiConnectionResult { Success = true };
                }
                catch (OperationCanceledException)
                {
                    return new WifiConnectionResult { Success = false, ErrorMessage = "キャンセルされました" };
                }
                catch (Exception ex)
                {
                    return new WifiConnectionResult { Success = false, ErrorMessage = ex.Message };
                }
                finally
                {
                    // クリーンアップ
                    try { addProc?.Kill(); } catch { }
                    try { connectProc?.Kill(); } catch { }
                    addProc?.Dispose();
                    connectProc?.Dispose();
                    
                    if (!string.IsNullOrEmpty(xmlPath) && File.Exists(xmlPath))
                    {
                        try { File.Delete(xmlPath); } catch { }
                    }
                }
            }, cancellationToken);
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
                
            // パスワード強度更新
            // パスワード強度分析（簡易版）
            var password = PasswordBox.Password;
            PasswordStrength = password.Length switch
            {
                < 8 => PasswordStrength.VeryWeak,
                < 12 => PasswordStrength.Weak,
                < 16 => PasswordStrength.Fair,
                < 20 => PasswordStrength.Strong,
                _ => PasswordStrength.VeryStrong
            };
        }

        private async void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.F5:
                    await LoadWifiNetworksAsync();
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Escape:
                    if (ConnectButton.Content.ToString() == "接続中...")
                    {
                        _cancellationTokenSource.Cancel();
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void WifiListBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && WifiListBox.SelectedItem != null)
            {
                PasswordBox.Focus();
                e.Handled = true;
            }
        }

        private async void PasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                ConnectButton_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private async void QuickConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var recentNetworks = _connectionHistory.GetRecentNetworks(5);
                if (!recentNetworks.Any())
                {
                    System.Windows.MessageBox.Show("接続履歴がありません。");
                    return;
                }

                // 利用可能なネットワークとマッチするものを探す
                var availableRecent = WifiNetworks
                    .Where(w => recentNetworks.Contains(w.SSID, StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(w => w.SignalStrength)
                    .FirstOrDefault();

                if (availableRecent == null)
                {
                    System.Windows.MessageBox.Show("最近の接続先が見つかりません。");
                    return;
                }

                // 接続済みの場合はスキップ
                if (availableRecent.IsConnected)
                {
                    System.Windows.MessageBox.Show($"{availableRecent.SSID}は既に接続されています。");
                    return;
                }

                // 自動接続試行（パスワードなしで）
                QuickConnectButton.IsEnabled = false;
                QuickConnectButton.Content = "⏳";
                
                var result = await ConnectToWifiAsync(availableRecent.SSID, "", _cancellationTokenSource.Token);
                
                if (result.Success)
                {
                    _connectionHistory.AddSuccessfulConnection(availableRecent.SSID);
                    System.Windows.MessageBox.Show($"{availableRecent.SSID}にクイック接続しました。");
                    await LoadWifiNetworksAsync();
                }
                else
                {
                    // 失敗した場合は通常の接続画面に切り替え
                    WifiListBox.SelectedItem = availableRecent;
                    PasswordBox.Focus();
                    System.Windows.MessageBox.Show($"{availableRecent.SSID}への自動接続に失敗しました。\\nパスワードを入力してください。");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"クイック接続エラー: {ex.Message}");
            }
            finally
            {
                QuickConnectButton.IsEnabled = true;
                QuickConnectButton.Content = "⚡";
            }
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _refreshTimer?.Stop();
                _cancellationTokenSource?.Cancel();
                
                // プロファイルクリーンアップ（最終）
                await _profileManager.CleanupOldProfilesAsync(25);
            }
            catch { }
        }

        private void OnNetworkStatusChanged(object? sender, NetworkStatusChangedEventArgs e)
        {
            // システムトレイのステータス更新
            // システムトレイ更新（削除済み）
            
            // ネットワーク接続状態変更時にWiFiリスト更新
            App.Current?.Dispatcher?.InvokeAsync(async () =>
            {
                try
                {
                    await LoadWifiNetworksAsync();
                    
                    // 接続成功時の通知
                    if (e.IsConnected && !string.IsNullOrEmpty(e.ConnectedSSID))
                    {
                        // バルーン通知（削除済み）
                    }
                }
                catch { }
            });
        }
        
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            // 最小化時にシステムトレイに隠す
            if (WindowState == WindowState.Minimized)
            {
                Hide(); // トレイ最小化（簡易版）
            }
        }

        private void RestoreWindowSettings()
        {
            try
            {
                var config = _appSettings.Configuration;
                if (config.WindowWidth > 0 && config.WindowHeight > 0)
                {
                    Width = config.WindowWidth;
                    Height = config.WindowHeight;
                }
                
                if (config.WindowLeft >= 0 && config.WindowTop >= 0)
                {
                    Left = config.WindowLeft;
                    Top = config.WindowTop;
                }
                
                if (Enum.TryParse<WindowState>(config.WindowState, out var windowState))
                {
                    WindowState = windowState;
                }
                
                if (config.StartMinimized)
                {
                    WindowState = WindowState.Minimized;
                }
            }
            catch { }
        }
        
        private void SaveWindowSettings()
        {
            try
            {
                var config = _appSettings.Configuration;
                config.WindowWidth = ActualWidth;
                config.WindowHeight = ActualHeight;
                config.WindowLeft = Left;
                config.WindowTop = Top;
                config.WindowState = WindowState.ToString();
            }
            catch { }
        }

        private static string GetSsidFromBss(object bss)
        {
            try
            {
                var managementObj = bss as System.Management.ManagementBaseObject;
                var ssidBytes = (byte[])managementObj?["Ndis80211Ssid"];
                if (ssidBytes == null || ssidBytes.Length == 0) return null;
                
                // 有効なバイトのみを取得
                int validLength = Array.IndexOf(ssidBytes, (byte)0);
                if (validLength == -1) validLength = ssidBytes.Length;
                if (validLength == 0) return null;
                
                // UTF-8でデコード、失敗したらASCIIでリトライ
                try
                {
                    return System.Text.Encoding.UTF8.GetString(ssidBytes, 0, validLength);
                }
                catch
                {
                    return System.Text.Encoding.ASCII.GetString(ssidBytes, 0, validLength);
                }
            }
            catch
            {
                return null;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                SaveWindowSettings();
                _refreshTimer?.Stop();
                _cancellationTokenSource?.Cancel();
                _networkMonitor?.Dispose();
                _scanSemaphore?.Dispose();
                _cancellationTokenSource?.Dispose();
                // メモリ最適化最終実行
                MemoryOptimizer.ForceGarbageCollection();
                _connectionLogger?.Dispose();
                // 障害検出器（削除済み）
                
                // 最終メモリクリーンアップ
                MemoryOptimizer.ForceGarbageCollection();
            }
            catch { }
            base.OnClosed(e);
        }
    }

    public class WifiConnectionResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}