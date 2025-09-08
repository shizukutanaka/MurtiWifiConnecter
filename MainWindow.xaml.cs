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
        private readonly SemaphoreSlim _uiUpdateSemaphore = new(1, 1);
        private readonly ConnectionHistory _connectionHistory = new();
        private readonly WifiProfileManager _profileManager = new();
        private DateTime _lastUIUpdate = DateTime.MinValue;
        private const int UIUpdateThrottleMs = 500; // UI更新の制限間隔
        // Network monitoring is now handled by NetworkUtils
        private readonly ConnectionStatistics _connectionStats = new();
        private readonly ConnectionLogger _connectionLogger = new();
        private readonly ConnectionRecoveryManager _recoveryManager;
        private readonly SystemTrayManager _systemTrayManager;
        private readonly SmartConnectionManager _smartConnectionManager;
        private readonly ConnectionHealthChecker _healthChecker;
        private readonly ConnectionQualityMonitor _qualityMonitor;
        private readonly NetworkPerformanceTracker _performanceTracker;
        private readonly ConnectionDiagnostics _connectionDiagnostics;
        
        private bool _isInitialized = false;

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
            
            // システム初期化が完了
            
            // ネットワーク監視の初期化
            NetworkUtils.NetworkStatusChanged += OnNetworkStatusChanged;
            NetworkUtils.StartNetworkMonitoring();
            
            // 新機能の初期化
            _systemTrayManager = new SystemTrayManager(this);
            _systemTrayManager.ShowMainWindowRequested += SystemTrayManager_ShowMainWindowRequested;
            _systemTrayManager.ExitApplicationRequested += SystemTrayManager_ExitApplicationRequested;
            _systemTrayManager.QuickConnectRequested += SystemTrayManager_QuickConnectRequested;
            
            _recoveryManager = new ConnectionRecoveryManager(_connectionStats, _connectionLogger);
            
            // スマート接続管理の初期化
            _healthChecker = new ConnectionHealthChecker(_connectionLogger);
            _smartConnectionManager = new SmartConnectionManager(_connectionHistory, _connectionStats, _connectionLogger, _healthChecker);
            _qualityMonitor = new ConnectionQualityMonitor(_connectionStats, _connectionLogger);
            _performanceTracker = new NetworkPerformanceTracker(_connectionLogger);
            _connectionDiagnostics = new ConnectionDiagnostics(_connectionLogger);
            
            _recoveryManager.RecoveryStarted += OnRecoveryStarted;
            _recoveryManager.RecoveryCompleted += OnRecoveryCompleted;
            _recoveryManager.RecoveryFailed += OnRecoveryFailed;
            
            // スマート接続管理イベント
            _smartConnectionManager.ConnectionSwitchRecommended += OnConnectionSwitchRecommended;
            _smartConnectionManager.ConnectionSwitched += OnConnectionSwitched;
            _healthChecker.ConnectionDegraded += OnConnectionDegraded;
            _healthChecker.ConnectionRecovered += OnConnectionRecovered;
            _qualityMonitor.QualityChanged += OnConnectionQualityChanged;
            _performanceTracker.PerformanceChanged += OnPerformanceChanged;
            _connectionDiagnostics.DiagnosticCompleted += OnDiagnosticCompleted;

            // 設定から言語復元（統一設定管理に移行）
            string savedLang = QuickSettingsManager.GetSetting("preferred_language", "en");
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
                // 起動最適化の実行
                _ = Task.Run(async () =>
                {
                    await SystemManager.OptimizeStartupAsync();
                    
                    // 推奨設定の適用
                    if (!QuickSettingsManager.GetSetting("portable_mode_enabled", false))
                    {
                        // システム診断結果に基づく推奨設定を適用（簡略化）
                        var currentHealth = SystemManager.GetCurrentHealth();
                        if (currentHealth.Status == HealthStatus.Warning || currentHealth.Status == HealthStatus.Critical)
                        {
                            // パフォーマンス問題がある場合の推奨設定
                            QuickSettingsManager.SetSettingAndSave("refresh_interval_seconds", 20);
                            QuickSettingsManager.SetSettingAndSave("max_displayed_networks", 20);
                        }
                    }
                });
                
                await LoadWifiNetworksAsync();
                // 動的リフレッシュ間隔の設定
                _refreshTimer = new System.Windows.Threading.DispatcherTimer();
                UpdateRefreshInterval();
                _refreshTimer.Tick += async (s, args) => 
                {
                    if (_scanSemaphore.CurrentCount == 0) return; // スキャン中はスキップ
                    
                    await LoadWifiNetworksAsync();
                    
                    // ネットワーク数に応じてリフレッシュ間隔を動的調整
                    UpdateRefreshInterval();
                    
                    // 定期的なメモリ最適化と自動調整
                    if (_refreshTimer.Tag is int tickCount)
                    {
                        tickCount++;
                        if (tickCount % 10 == 0) // 10回に1回メモリ最適化
                        {
                            SystemManager.OptimizeMemory(); // 軽量処理のため直接実行
                        }
                        if (tickCount % 30 == 0) // 30回に1回健全性チェック（約7-9分間隔）
                        {
                            Task.Run(async () => await PerformSystemHealthCheckAsync());
                        }
                        if (tickCount % 50 == 0) // 50回に1回自動調整（約12-15分間隔）
                        {
                            Task.Run(() => PerformPeriodicAutoTuning());
                        }
                        _refreshTimer.Tag = tickCount;
                    }
                    else
                    {
                        _refreshTimer.Tag = 1;
                    }
                };
                _refreshTimer.Start();

                // 起動後のバックグラウンドタスクを遅延実行（軽量化）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // メモリ最適化を開始
                        _ = Task.Run(async () => await SystemManager.RunPeriodicOptimizationAsync(_cancellationTokenSource.Token));
                        
                        // 起動完了を待つ
                        await Task.Delay(QuickSettingsManager.Constants.StartupDelayMs);
                        
                        // プロファイルクリーンアップ
                        if (QuickSettingsManager.GetSetting("auto_cleanup_profiles", true))
                        {
                            await _profileManager.CleanupOldProfilesAsync(QuickSettingsManager.GetSetting("max_profile_history", 30));
                        }
                        
                        // 古い統計データクリーンアップ（30日以上前）
                        _connectionStats.CleanupOldData(TimeSpan.FromDays(30));
                        
                        // 接続履歴の最適化
                        _connectionHistory.OptimizeStorage();
                        _connectionHistory.CleanupOldEntries(90);
                        
                        // 障害検出器を初期化（削除済み）
                        
                        // 初期ガベージコレクション実行
                        SystemManager.OptimizeMemory();
                    }
                    catch (OutOfMemoryException ex)
                    {
                        ErrorHandler.LogError("MainWindow.StartupBackgroundTasks.OutOfMemory", ex, _connectionLogger);
                        // 緊急メモリクリーンアップ
                        SystemManager.OptimizeMemory();
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        ErrorHandler.LogError("MainWindow.StartupBackgroundTasks.AccessDenied", ex, _connectionLogger);
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler.LogError("MainWindow.StartupBackgroundTasks", ex, _connectionLogger);
                    }
                });
                
                // ウィンドウ設定の復元
                RestoreWindowSettings();
            }
            catch (UnauthorizedAccessException)
            {
                System.Windows.MessageBox.Show(
                    "初期化に失敗しました。管理者権限が必要な可能性があります。\n" +
                    "アプリケーションを管理者として実行してみてください。",
                    "権限エラー", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
            }
            catch (System.Management.ManagementException ex)
            {
                System.Windows.MessageBox.Show(
                    $"WiFiアダプタへのアクセスに失敗しました。\n" +
                    $"WiFiアダプタが有効になっているか確認してください。\n詳細: {ex.Message}",
                    "WiFiアダプタエラー", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"予期しないエラーが発生しました: {ex.Message}\n\n" +
                    "アプリケーションを再起動してください。",
                    "初期化エラー", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_scanSemaphore.CurrentCount == 0)
            {
                // ノンブロッキング通知
                _ = Task.Run(() => Dispatcher.InvokeAsync(() => 
                    _systemTrayManager?.ShowBalloonTip("スキャン中", "WiFiスキャンが実行中です", System.Windows.Forms.ToolTipIcon.Info)));
                return;
            }
            
            // UI状態を非同期で更新
            await SetRefreshUIStateAsync(true);
            
            try
            {
                await LoadWifiNetworksAsync();
            }
            finally
            {
                await SetRefreshUIStateAsync(false);
            }
        }
        
        private async Task SetRefreshUIStateAsync(bool isRefreshing)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                RefreshButton.IsEnabled = !isRefreshing;
                RefreshButton.Content = isRefreshing ? "更新中..." : "更新";
                Cursor = isRefreshing ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        public async Task LoadWifiNetworksAsync()
        {
            if (!await _scanSemaphore.WaitAsync(100))
                return; // 他のスキャンが実行中

            var scanStartTime = DateTime.Now;
            try
            {
                string connectedSsid = await NetworkUtils.GetCurrentConnectedSSIDAsync(_cancellationTokenSource.Token);
                var wifiList = new List<WifiNetwork>(50); // 初期容量設定でメモリ効率化
                
                await Task.Run(() =>
                {
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM MSNdis_80211_BSSIList");
                    using var results = searcher.Get();
                    var obj = results.Cast<ManagementBaseObject>().FirstOrDefault();
                    if (obj == null) return;
                    
                    var bssilist = (ManagementBaseObject[])obj["Ndis80211BSSIList"];
                    if (bssilist == null) return;
                    
                    var ssidSet = new HashSet<string>(bssilist.Length, StringComparer.OrdinalIgnoreCase); // 初期容量設定
                    var tempWifiList = new List<(string ssid, int signal, bool isConnected)>(bssilist.Length);
                    
                    // CPUコア数に応じて最適な並列度を設定
                    var parallelOptions = new ParallelOptions
                    {
                        CancellationToken = _cancellationTokenSource.Token,
                        MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4) // 最大4並列に制限
                    };
                    
                    var lockObj = new object();
                    Parallel.ForEach(bssilist, parallelOptions, bss =>
                    {
                        if (_cancellationTokenSource.Token.IsCancellationRequested) return;
                        
                        string ssid = NetworkUtils.GetSsidFromBss(bss);
                        if (string.IsNullOrWhiteSpace(ssid) || ssid.Length > 32) return;
                        
                        lock (lockObj)
                        {
                            if (!ssidSet.Add(ssid)) return;
                        }
                        
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
                        
                        lock (lockObj)
                        {
                            tempWifiList.Add((ssid, signal, isConnected));
                        }
                    });

                    // 履歴とデータ処理の最適化：一括取得で処理を高速化
                    var historySsids = _connectionHistory.GetAllConnectedNetworks();
                    var historyLookup = new HashSet<string>(historySsids, StringComparer.OrdinalIgnoreCase);
                    
                    // バッチ処理で統計更新の効率化
                    var signalStrengthBatch = new Dictionary<string, int>(tempWifiList.Count);
                    var networkProcessingBatch = new List<(string ssid, int signal, bool isConnected, bool hasHistory, int connectionCount)>(tempWifiList.Count);
                    
                    // データ収集フェーズ：I/O操作を最小化
                    foreach (var (ssid, signal, isConnected) in tempWifiList)
                    {
                        bool hasConnectedBefore = historyLookup.Contains(ssid);
                        signalStrengthBatch[ssid] = signal;
                        
                        var historyEntry = _connectionHistory.GetEntry(ssid);
                        var connectionCount = historyEntry?.ConnectionCount ?? 0;
                        
                        networkProcessingBatch.Add((ssid, signal, isConnected, hasConnectedBefore, connectionCount));
                    }
                    
                    // バッチ統計更新
                    _connectionStats.RecordSignalStrengthBatch(signalStrengthBatch);
                    
                    // ネットワークオブジェクト作成フェーズ
                    foreach (var (ssid, signal, isConnected, hasConnectedBefore, connectionCount) in networkProcessingBatch)
                    {
                        var recommendation = _connectionStats.AnalyzeNetworkQuality(ssid, signal, hasConnectedBefore, connectionCount);
                        
                        var wifiNetwork = new WifiNetwork 
                        { 
                            SSID = ssid, 
                            SignalStrength = signal, 
                            IsConnected = isConnected,
                            HasConnectedBefore = hasConnectedBefore,
                            SignalQuality = recommendation.RecommendationText ?? GetSignalQualityDescription(signal)
                        };
                        
                        wifiList.Add(wifiNetwork);
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

                // UI更新の制限とスロットリング
                if (!_cancellationTokenSource.Token.IsCancellationRequested && 
                    App.Current?.Dispatcher != null &&
                    await _uiUpdateSemaphore.WaitAsync(50))
                {
                    try
                    {
                        // UI更新頻度制限
                        var now = DateTime.Now;
                        if ((now - _lastUIUpdate).TotalMilliseconds < UIUpdateThrottleMs)
                        {
                            return; // 更新間隔が短すぎる場合はスキップ
                        }
                        _lastUIUpdate = now;
                        
                        await App.Current.Dispatcher.InvokeAsync(() =>
                        {
                            // 差分更新による UI効率化
                            var displayNetworks = sorted.Take(QuickSettingsManager.GetSetting("max_displayed_networks", 50)).ToList();
                            
                            // より効率的な差分チェック
                            UpdateNetworkListEfficiently(displayNetworks);
                            
                            // クイック接続ボタンの表示制御（キャッシュ済みデータを使用）
                            var hasHistory = displayNetworks.Any(w => w.HasConnectedBefore);
                            QuickConnectButton.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }
                    finally
                    {
                        _uiUpdateSemaphore.Release();
                    }
                }
                
                // パフォーマンス監視: スキャン完了を記録
                var scanDuration = DateTime.Now - scanStartTime;
                var networksFound = sorted?.Count ?? 0;
                SystemManager.RecordNetworkScan(networksFound, scanDuration);
            }
            catch (OperationCanceledException)
            {
                // キャンセルされた場合は無視
            }
            catch (System.Management.ManagementException ex)
            {
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // ノンブロッキングエラー通知
                    _ = ShowNonBlockingErrorAsync("WiFiアダプタエラー", 
                        $"WiFiアダプタにアクセスできません。WiFiアダプタが有効になっているか確認してください。\n詳細: {ex.Message}");
                }
                ErrorHandler.LogError("MainWindow.LoadWifiNetworksAsync.ManagementException", ex, _connectionLogger);
            }
            catch (UnauthorizedAccessException ex)
            {
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // ノンブロッキングエラー通知
                    _ = ShowNonBlockingErrorAsync("権限エラー", 
                        "WiFi情報にアクセスする権限がありません。管理者として実行してください。");
                }
                ErrorHandler.LogError("MainWindow.LoadWifiNetworksAsync.UnauthorizedAccess", ex, _connectionLogger);
            }
            catch (OutOfMemoryException ex)
            {
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // ノンブロッキングエラー通知
                    _ = ShowNonBlockingErrorAsync("メモリ不足", 
                        "メモリが不足しています。他のアプリケーションを終了してください。");
                }
                ErrorHandler.LogError("MainWindow.LoadWifiNetworksAsync.OutOfMemory", ex, _connectionLogger);
                // 非同期メモリ最適化
                _ = Task.Run(() => SystemManager.OptimizeMemory());
            }
            catch (Exception ex)
            {
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // ノンブロッキングエラー通知
                    _ = ShowNonBlockingErrorAsync("スキャンエラー", 
                        $"WiFiネットワークスキャンに失敗しました。詳細: {ex.Message}");
                }
                ErrorHandler.LogError("MainWindow.LoadWifiNetworksAsync.General", ex, _connectionLogger);
            }
            finally
            {
                _scanSemaphore.Release();
            }
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
            // UI要素を非同期で無効化
            ConnectButton.IsEnabled = false;
            ConnectButton.Content = "接続中...";
            PasswordBox.IsEnabled = false;
            WifiListBox.IsEnabled = false;
            
            // プログレス表示用のカーソル変更
            Cursor = System.Windows.Input.Cursors.Wait;
            
            try
            {
                var result = await FastWifiConnector.ConnectAsync(selected.SSID, password, _cancellationTokenSource.Token);
                
                if (result.Success)
                {
                    _connectionHistory.AddSuccessfulConnection(selected.SSID);
                    
                    // WiFi分析に接続成功を記録
                    var connectionTime = TimeSpan.FromSeconds(10); // 推定接続時間
                    _connectionStats.RecordConnectionAttempt(selected.SSID, true, connectionTime);
                    
                    // パフォーマンス追跡開始
                    _performanceTracker.StartTracking();
                    
                    System.Windows.MessageBox.Show($"{selected.SSID}に正常に接続しました。");
                    PasswordBox.Password = ""; // パスワードをクリア
                    await LoadWifiNetworksAsync(); // ステータス更新
                }
                else
                {
                    // WiFi分析に接続失敗を記録
                    _connectionStats.RecordConnectionAttempt(selected.SSID, false);
                    
                    string errorDetail = string.IsNullOrEmpty(result.ErrorMessage) ? 
                        "不明なエラー" : result.ErrorMessage;
                    
                    // 自動復旧を試行
                    var shouldAttemptRecovery = _recoveryManager.AutoRecoveryEnabled && 
                                               !_cancellationTokenSource.Token.IsCancellationRequested;
                    
                    if (shouldAttemptRecovery)
                    {
                        var recoveryChoice = System.Windows.MessageBox.Show(
                            $"{selected.SSID}への接続に失敗しました。\n詳細: {errorDetail}\n\n自動復旧を実行しますか？",
                            "接続失敗",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                            
                        if (recoveryChoice == MessageBoxResult.Yes)
                        {
                            try
                            {
                                ConnectButton.Content = "復旧中...";
                                ConnectButton.IsEnabled = false;
                                
                                var recoveryResult = await _recoveryManager.AttemptRecoveryAsync(
                                    selected.SSID, password, _cancellationTokenSource.Token);
                                
                                if (recoveryResult.Success)
                                {
                                    System.Windows.MessageBox.Show(
                                        $"復旧成功: {selected.SSID}に接続しました。\n{recoveryResult.Message}",
                                        "復旧成功",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information);
                                    
                                    _connectionHistory.AddSuccessfulConnection(selected.SSID);
                                    await LoadWifiNetworksAsync();
                                }
                                else
                                {
                                    // 復旧失敗時は自動診断を実行
                                    Task.Run(async () =>
                                    {
                                        try
                                        {
                                            var diagnosticResult = await _connectionDiagnostics.DiagnoseConnectionIssueAsync(
                                                new Exception($"復旧失敗: {recoveryResult.Message}"), selected.SSID);
                                            
                                            if (diagnosticResult.OverallSeverity >= DiagnosticSeverity.Medium)
                                            {
                                                await Dispatcher.InvokeAsync(() =>
                                                {
                                                    var primaryIssue = diagnosticResult.PrimaryIssue?.Description ?? "不明な問題";
                                                    var recommendation = diagnosticResult.RecommendedActions.FirstOrDefault() ?? "設定を確認してください";
                                                    
                                                    var diagResult = System.Windows.MessageBox.Show(
                                                        $"復旧失敗: {recoveryResult.Message}\n\n診断結果: {primaryIssue}\n推奨対処: {recommendation}\n\n詳細な診断を確認しますか？",
                                                        "復旧失敗 - 診断結果",
                                                        MessageBoxButton.YesNo,
                                                        MessageBoxImage.Error);
                                                    
                                                    if (diagResult == MessageBoxResult.Yes)
                                                    {
                                                        Task.Run(async () => await ShowDetailedDiagnosticsAsync());
                                                    }
                                                });
                                            }
                                            else
                                            {
                                                await Dispatcher.InvokeAsync(() =>
                                                {
                                                    System.Windows.MessageBox.Show(
                                                        $"復旧失敗: {recoveryResult.Message}",
                                                        "復旧失敗",
                                                        MessageBoxButton.OK,
                                                        MessageBoxImage.Error);
                                                });
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            ErrorHandler.LogError("MainWindow.DiagnosticsOnRecoveryFailure", ex, _connectionLogger);
                                            await Dispatcher.InvokeAsync(() =>
                                            {
                                                System.Windows.MessageBox.Show(
                                                    $"復旧失敗: {recoveryResult.Message}",
                                                    "復旧失敗",
                                                    MessageBoxButton.OK,
                                                    MessageBoxImage.Error);
                                            });
                                        }
                                    });
                                }
                            }
                            catch (Exception recoveryEx)
                            {
                                ErrorHandler.LogError("MainWindow.ConnectionRecovery", recoveryEx, _connectionLogger);
                                System.Windows.MessageBox.Show(
                                    $"復旧処理中にエラーが発生しました: {recoveryEx.Message}",
                                    "復旧エラー",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                            }
                            finally
                            {
                                ConnectButton.Content = "接続";
                                ConnectButton.IsEnabled = true;
                            }
                        }
                        else
                        {
                            // ユーザーが復旧を選択しなかった場合も診断を提案
                            var diagChoice = System.Windows.MessageBox.Show(
                                $"{selected.SSID}への接続に失敗しました。\n詳細: {errorDetail}\n\n問題の診断を実行しますか？",
                                "接続失敗",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);
                                
                            if (diagChoice == MessageBoxResult.Yes)
                            {
                                Task.Run(async () => await TriggerManualDiagnosticsAsync());
                            }
                        }
                    }
                    else
                    {
                        // 自動復旧が無効な場合も診断を提案
                        var diagChoice = System.Windows.MessageBox.Show(
                            $"{selected.SSID}への接続に失敗しました。\n詳細: {errorDetail}\n\n問題の診断を実行しますか？",
                            "接続失敗",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                            
                        if (diagChoice == MessageBoxResult.Yes)
                        {
                            Task.Run(async () => await TriggerManualDiagnosticsAsync());
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                System.Windows.MessageBox.Show("接続がキャンセルされました。");
            }
            finally
            {
                // UI要素を非同期で再有効化
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = "接続";
                PasswordBox.IsEnabled = true;
                WifiListBox.IsEnabled = true;
                
                // カーソルを元に戻す
                Cursor = System.Windows.Input.Cursors.Arrow;
            }
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
                    ConnectButton.Content = "接続";
                    PasswordPlaceholderText.Text = Properties.Resources.PasswordPlaceholder;
                    // 言語設定保存
                    QuickSettingsManager.SetSettingAndSave("preferred_language", langCode);
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
            // 修飾キーの状態を確認
            var isCtrlPressed = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
            
            switch (e.Key)
            {
                case System.Windows.Input.Key.F5:
                    if (!RefreshButton.IsEnabled) return;
                    RefreshButton_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                    
                case System.Windows.Input.Key.Escape:
                    if (ConnectButton.Content.ToString() == "接続中..." || 
                        QuickConnectButton.Content.ToString() == "⏳")
                    {
                        _cancellationTokenSource.Cancel();
                        e.Handled = true;
                    }
                    break;
                    
                case System.Windows.Input.Key.R:
                    if (isCtrlPressed && RefreshButton.IsEnabled)
                    {
                        RefreshButton_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;
                    
                case System.Windows.Input.Key.Q:
                    if (isCtrlPressed && QuickConnectButton.IsEnabled && QuickConnectButton.Visibility == Visibility.Visible)
                    {
                        QuickConnectButton_Click(sender, new RoutedEventArgs());
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
                WifiListBox.IsEnabled = false;
                ConnectButton.IsEnabled = false;
                
                // プログレス表示
                Cursor = System.Windows.Input.Cursors.Wait;
                
                var result = await FastWifiConnector.ConnectAsync(availableRecent.SSID, "", _cancellationTokenSource.Token);
                
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
                    System.Windows.MessageBox.Show($"{availableRecent.SSID}への自動接続に失敗しました。\nパスワードを入力してください。", "接続失敗", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                var userFriendlyMessage = ErrorHandler.GetUserFriendlyErrorMessage(ex);
                System.Windows.MessageBox.Show($"クイック接続エラー: {userFriendlyMessage}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                ErrorHandler.LogError("MainWindow.QuickConnect", ex, _connectionLogger);
            }
            finally
            {
                QuickConnectButton.IsEnabled = true;
                QuickConnectButton.Content = "⚡";
                WifiListBox.IsEnabled = true;
                ConnectButton.IsEnabled = true;
                
                // カーソルを元に戻す
                Cursor = System.Windows.Input.Cursors.Arrow;
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


        private void RestoreWindowSettings()
        {
            try
            {
                var windowWidth = QuickSettingsManager.GetSetting("window_width", 900.0);
                var windowHeight = QuickSettingsManager.GetSetting("window_height", 500.0);
                if (windowWidth > 0 && windowHeight > 0)
                {
                    Width = windowWidth;
                    Height = windowHeight;
                }
                
                var windowLeft = QuickSettingsManager.GetSetting("window_left", -1.0);
                var windowTop = QuickSettingsManager.GetSetting("window_top", -1.0);
                if (windowLeft >= 0 && windowTop >= 0)
                {
                    Left = windowLeft;
                    Top = windowTop;
                }
                
                var savedWindowState = QuickSettingsManager.GetSetting("window_state", "Normal");
                if (Enum.TryParse<WindowState>(savedWindowState, out var windowState))
                {
                    WindowState = windowState;
                }
                
                if (QuickSettingsManager.GetSetting("start_minimized", false))
                {
                    WindowState = WindowState.Minimized;
                }
            }
            catch { }
        }
        
        private void UpdateRefreshInterval()
        {
            try
            {
                // ネットワーク数に基づいて動的にリフレッシュ間隔を調整
                var networkCount = WifiNetworks.Count;
                var baseInterval = QuickSettingsManager.GetSetting("refresh_interval_seconds", 15);
                
                var interval = networkCount switch
                {
                    < 5 => TimeSpan.FromSeconds(baseInterval),
                    < 15 => TimeSpan.FromSeconds(baseInterval + 5),
                    < 30 => TimeSpan.FromSeconds(baseInterval + 10),
                    _ => TimeSpan.FromSeconds(baseInterval + 15)
                };
                
                if (_refreshTimer.Interval != interval)
                {
                    _refreshTimer.Interval = interval;
                }
            }
            catch { }
        }
        
        private void SaveWindowSettings()
        {
            try
            {
                QuickSettingsManager.SetSettingAndSave("window_width", ActualWidth);
                QuickSettingsManager.SetSettingAndSave("window_height", ActualHeight);
                QuickSettingsManager.SetSettingAndSave("window_left", Left);
                QuickSettingsManager.SetSettingAndSave("window_top", Top);
                QuickSettingsManager.SetSettingAndSave("window_state", WindowState.ToString());
            }
            catch { }
        }


        protected override void OnClosed(EventArgs e)
        {
            try
            {
                SaveWindowSettings();
                
                // タイマー停止
                _refreshTimer?.Stop();
                
                // キャンセレーション実行
                _cancellationTokenSource?.Cancel();
                
                // 履歴とプロファイル管理の強制保存
                _connectionHistory?.ForceSave();
                QuickSettingsManager.SaveSettings();
                
                // イベントハンドラの解除（メモリリーク防止）
                NetworkUtils.NetworkStatusChanged -= OnNetworkStatusChanged;
                NetworkUtils.StopNetworkMonitoring();
                
                // システムトレイの解除
                if (_systemTrayManager != null)
                {
                    _systemTrayManager.ShowMainWindowRequested -= SystemTrayManager_ShowMainWindowRequested;
                    _systemTrayManager.ExitApplicationRequested -= SystemTrayManager_ExitApplicationRequested;
                    _systemTrayManager.QuickConnectRequested -= SystemTrayManager_QuickConnectRequested;
                    _systemTrayManager.Dispose();
                }
                
                // リソースの解放
                _scanSemaphore?.Dispose();
                _cancellationTokenSource?.Dispose();
                _connectionLogger?.Dispose();
                _recoveryManager?.Dispose();
                _smartConnectionManager?.Dispose();
                _healthChecker?.Dispose();
                _qualityMonitor?.Dispose();
                _performanceTracker?.Dispose();
                _connectionDiagnostics?.Dispose();
                
                // WiFiネットワークリストのクリア
                WifiNetworks?.Clear();
                
                // 最終メモリクリーンアップ（同期で実行）
                SystemManager.OptimizeMemory();
            }
            catch { }
            base.OnClosed(e);
        }
        
        private void ApplyRecommendedSettings(AppConfiguration recommendedSettings)
        {
            try
            {
                if (recommendedSettings == null) return;
                
                // パフォーマンスに影響する設定のみを自動適用
                if (recommendedSettings.RefreshIntervalSeconds != QuickSettingsManager.GetSetting("refresh_interval_seconds", 15))
                {
                    QuickSettingsManager.SetSettingAndSave("refresh_interval_seconds", recommendedSettings.RefreshIntervalSeconds);
                }
                
                if (recommendedSettings.MaxDisplayedNetworks != QuickSettingsManager.GetSetting("max_displayed_networks", 50))
                {
                    QuickSettingsManager.SetSettingAndSave("max_displayed_networks", recommendedSettings.MaxDisplayedNetworks);
                }
                
                if (recommendedSettings.EnableDetailedLogging != QuickSettingsManager.GetSetting("detailed_logging", false))
                {
                    QuickSettingsManager.SetSettingAndSave("detailed_logging", recommendedSettings.EnableDetailedLogging);
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.ApplyRecommendedSettings", ex);
            }
        }
        
        private void PerformPeriodicAutoTuning()
        {
            try
            {
                // 簡略化された自動調整処理
                var networkCount = WifiNetworks.Count;
                var currentHealth = SystemManager.GetCurrentHealth();
                
                // メモリ使用量に基づく自動調整
                if (currentHealth.Status == HealthStatus.Warning)
                {
                    var currentRefresh = QuickSettingsManager.GetSetting("refresh_interval_seconds", 15);
                    if (currentRefresh < 20)
                    {
                        QuickSettingsManager.SetSettingAndSave("refresh_interval_seconds", 20);
                        _systemTrayManager?.ShowBalloonTip("設定最適化", "リフレッシュ間隔を20秒に調整しました", System.Windows.Forms.ToolTipIcon.Info);
                    }
                }
                
                // ネットワーク数に基づく表示制限調整
                if (networkCount > 40)
                {
                    QuickSettingsManager.SetSettingAndSave("max_displayed_networks", 30);
                    _systemTrayManager?.ShowBalloonTip("設定最適化", "表示ネットワーク数を30に制限しました", System.Windows.Forms.ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.PerformPeriodicAutoTuning", ex);
            }
        }
        
        private async void OnConnectionSwitchRecommended(object? sender, ConnectionSwitchEventArgs e)
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    var result = System.Windows.MessageBox.Show(
                        $"{e.Recommendation.GetRecommendationText()}\n\n切り替えを実行しますか？",
                        "接続切り替え推奨",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Task.Run(async () =>
                        {
                            await _smartConnectionManager.ExecuteSmartSwitchAsync(e.Recommendation.RecommendedSSID);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.OnConnectionSwitchRecommended", ex, _connectionLogger);
            }
        }
        
        private async void OnConnectionSwitched(object? sender, ConnectionSwitchEventArgs e)
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _systemTrayManager?.ShowBalloonTip(
                        "接続切り替え完了",
                        $"{e.Recommendation.RecommendedSSID}に切り替えました",
                        System.Windows.Forms.ToolTipIcon.Info);
                });
                
                // WiFiリストの更新
                await LoadWifiNetworksAsync();
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.OnConnectionSwitched", ex, _connectionLogger);
            }
        }
        
        private async void OnConnectionDegraded(object? sender, ConnectionHealthEventArgs e)
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (QuickSettingsManager.GetSetting("tray_notifications", true))
                    {
                        _systemTrayManager?.ShowBalloonTip(
                            "接続品質低下",
                            $"現在の接続品質: {e.Health.GetQualityDescription()}",
                            System.Windows.Forms.ToolTipIcon.Warning);
                    }
                });
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.OnConnectionDegraded", ex, _connectionLogger);
            }
        }
        
        private async void OnConnectionRecovered(object? sender, ConnectionHealthEventArgs e)
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (QuickSettingsManager.GetSetting("tray_notifications", true))
                    {
                        _systemTrayManager?.ShowBalloonTip(
                            "接続品質回復",
                            $"接続品質が回復しました: {e.Health.GetQualityDescription()}",
                            System.Windows.Forms.ToolTipIcon.Info);
                    }
                });
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.OnConnectionRecovered", ex, _connectionLogger);
            }
        }
        
        private void OnConnectionQualityChanged(object? sender, QualityChangedEventArgs e)
        {
            try
            {
                Dispatcher.InvokeAsync(() =>
                {
                    // タイトルバーに接続品質情報を表示
                    var qualityDesc = e.Quality switch
                    {
                        ConnectionQuality.Excellent => "★★★★★",
                        ConnectionQuality.Good => "★★★★☆",
                        ConnectionQuality.Fair => "★★★☆☆",
                        ConnectionQuality.Poor => "★★☆☆☆",
                        ConnectionQuality.VeryPoor => "★☆☆☆☆",
                        ConnectionQuality.Disconnected => "未接続",
                        ConnectionQuality.Error => "エラー",
                        _ => "不明"
                    };
                    
                    if (!string.IsNullOrEmpty(e.SSID))
                    {
                        Title = $"Murti WiFi Connecter - {e.SSID} ({e.SignalStrength}% {qualityDesc})";
                        
                        // 品質が大幅に変化した場合は通知
                        if (Math.Abs((int)e.Quality - (int)e.PreviousQuality) >= 2)
                        {
                            var message = e.Quality > e.PreviousQuality ? 
                                $"接続品質が改善されました: {qualityDesc}" : 
                                $"接続品質が低下しました: {qualityDesc}";
                                
                            if (QuickSettingsManager.GetSetting("tray_notifications", true))
                            {
                                var iconType = e.Quality >= ConnectionQuality.Good ? 
                                    System.Windows.Forms.ToolTipIcon.Info : 
                                    System.Windows.Forms.ToolTipIcon.Warning;
                                    
                                _systemTrayManager?.ShowBalloonTip("接続品質変化", message, iconType);
                            }
                        }
                    }
                    else
                    {
                        Title = "Murti WiFi Connecter";
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.OnConnectionQualityChanged", ex, _connectionLogger);
            }
        }
        
        private void OnPerformanceChanged(object? sender, PerformanceChangedEventArgs e)
        {
            try
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var performance = e.Performance;
                    
                    // 詳細パフォーマンス情報をタイトルバーに追加
                    if (!string.IsNullOrEmpty(performance.SSID) && performance.IsConnected)
                    {
                        var latencyMs = (int)performance.Latency.TotalMilliseconds;
                        var latencyDesc = latencyMs switch
                        {
                            <= 30 => "高速",
                            <= 60 => "良好", 
                            <= 100 => "普通",
                            <= 200 => "遅い",
                            _ => "非常に遅い"
                        };
                        
                        var dataRateDesc = _performanceTracker.CurrentPerformance.DataRateKbps switch
                        {
                            >= 1000 => "高速転送",
                            >= 100 => "中速転送",
                            >= 10 => "低速転送",
                            _ => "待機中"
                        };
                        
                        // 現在のタイトルに詳細情報を追加
                        if (Title.Contains(" ("))
                        {
                            var baseTitle = Title.Substring(0, Title.IndexOf(" ("));
                            Title = $"{baseTitle} ({performance.SignalStrength}% | {latencyMs}ms {latencyDesc} | {dataRateDesc})";
                        }
                        
                        // パフォーマンスが大幅に変化した場合は通知
                        if (latencyMs > 500 && QuickSettingsManager.GetSetting("performance_notifications", true))
                        {
                            _systemTrayManager?.ShowBalloonTip("接続性能低下", 
                                $"レイテンシが高くなっています: {latencyMs}ms", 
                                System.Windows.Forms.ToolTipIcon.Warning);
                        }
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.OnPerformanceChanged", ex, _connectionLogger);
            }
        }
        
        private void OnDiagnosticCompleted(object? sender, DiagnosticEventArgs e)
        {
            try
            {
                var result = e.Result;
                
                if (result.OverallSeverity >= DiagnosticSeverity.Medium)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        var severity = result.OverallSeverity switch
                        {
                            DiagnosticSeverity.Critical => "重要",
                            DiagnosticSeverity.High => "高",
                            DiagnosticSeverity.Medium => "中",
                            _ => "低"
                        };
                        
                        var issueDescription = result.PrimaryIssue?.Description ?? "接続品質の問題が検出されました";
                        var recommendations = string.Join("\n• ", result.RecommendedActions);
                        
                        if (result.OverallSeverity == DiagnosticSeverity.Critical)
                        {
                            var diagnosticResult = System.Windows.MessageBox.Show(
                                $"重要な接続問題が検出されました:\n\n{issueDescription}\n\n推奨対処法:\n• {recommendations}\n\n詳細な診断を実行しますか？",
                                $"接続診断 - 重要度: {severity}",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);
                                
                            if (diagnosticResult == MessageBoxResult.Yes)
                            {
                                Task.Run(async () => await ShowDetailedDiagnosticsAsync());
                            }
                        }
                        else if (QuickSettingsManager.GetSetting("diagnostic_notifications", true))
                        {
                            _systemTrayManager?.ShowBalloonTip($"診断結果 - 重要度: {severity}", 
                                issueDescription, 
                                System.Windows.Forms.ToolTipIcon.Info);
                        }
                        
                        UpdateDiagnosticStatusInTitle(result);
                        
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.OnDiagnosticCompleted", ex, _connectionLogger);
            }
        }
        
        private void UpdateDiagnosticStatusInTitle(DiagnosticResult result)
        {
            if (result.OverallSeverity >= DiagnosticSeverity.Medium)
            {
                var statusIcon = result.OverallSeverity switch
                {
                    DiagnosticSeverity.Critical => "⚠",
                    DiagnosticSeverity.High => "!",
                    DiagnosticSeverity.Medium => "?",
                    _ => ""
                };
                
                if (!string.IsNullOrEmpty(statusIcon) && !Title.Contains(statusIcon))
                {
                    var baseTitle = Title.Contains(" - ") ? Title.Substring(0, Title.IndexOf(" - ")) : Title;
                    if (Title.Contains(" ("))
                    {
                        var perfInfo = Title.Substring(Title.IndexOf(" ("));
                        Title = $"{baseTitle} - {statusIcon}診断{perfInfo}";
                    }
                    else
                    {
                        Title = $"{baseTitle} - {statusIcon}診断";
                    }
                }
            }
        }
        
        public async Task ShowDetailedDiagnosticsAsync()
        {
            try
            {
                var currentSSID = await FastWifiConnector.GetCurrentConnectedSSIDAsync();
                var result = await _connectionDiagnostics.DiagnoseConnectionIssueAsync(null, currentSSID);
                
                await Dispatcher.InvokeAsync(() =>
                {
                    var diagnosticInfo = $"診断時刻: {result.Timestamp:HH:mm:ss}\n";
                    diagnosticInfo += $"対象SSID: {result.SSID}\n";
                    diagnosticInfo += $"総合重要度: {result.OverallSeverity}\n\n";
                    
                    diagnosticInfo += "詳細チェック結果:\n";
                    foreach (var check in result.Checks)
                    {
                        var statusIcon = check.Status switch
                        {
                            DiagnosticStatus.Healthy => "✓",
                            DiagnosticStatus.Warning => "!",
                            DiagnosticStatus.Critical => "✗",
                            DiagnosticStatus.Error => "?",
                            _ => ""
                        };
                        
                        diagnosticInfo += $"{statusIcon} {check.Description}: ";
                        if (check.Status == DiagnosticStatus.Healthy)
                        {
                            diagnosticInfo += check.Details ?? "正常";
                        }
                        else
                        {
                            diagnosticInfo += check.Issue ?? "問題あり";
                        }
                        diagnosticInfo += "\n";
                    }
                    
                    if (result.PrimaryIssue != null)
                    {
                        diagnosticInfo += $"\n主要問題: {result.PrimaryIssue.Description}\n";
                    }
                    
                    if (result.RecommendedActions.Any())
                    {
                        diagnosticInfo += "\n推奨対処法:\n";
                        foreach (var action in result.RecommendedActions)
                        {
                            diagnosticInfo += $"• {action}\n";
                        }
                    }
                    
                    System.Windows.MessageBox.Show(
                        diagnosticInfo,
                        "詳細診断結果",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.ShowDetailedDiagnosticsAsync", ex, _connectionLogger);
                
                await Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show(
                        $"診断の実行中にエラーが発生しました: {ex.Message}",
                        "診断エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }
        
        public async Task TriggerManualDiagnosticsAsync()
        {
            try
            {
                var currentSSID = await FastWifiConnector.GetCurrentConnectedSSIDAsync();
                if (string.IsNullOrEmpty(currentSSID))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        System.Windows.MessageBox.Show(
                            "現在WiFiに接続されていません。接続後に診断を実行してください。",
                            "診断実行不可",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                    return;
                }
                
                await Dispatcher.InvokeAsync(() =>
                {
                    LoadingPanel.Visibility = Visibility.Visible;
                    LoadingText.Text = "接続診断を実行中...";
                });
                
                var result = await _connectionDiagnostics.DiagnoseConnectionIssueAsync(null, currentSSID);
                
                await Dispatcher.InvokeAsync(() =>
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                });
                
                await ShowDiagnosticResultAsync(result);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.TriggerManualDiagnosticsAsync", ex, _connectionLogger);
                
                await Dispatcher.InvokeAsync(() =>
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    System.Windows.MessageBox.Show(
                        $"診断の実行中にエラーが発生しました: {ex.Message}",
                        "診断エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }
        
        private async Task ShowDiagnosticResultAsync(DiagnosticResult result)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var summary = result.OverallSeverity switch
                {
                    DiagnosticSeverity.Low => "接続に問題は見つかりませんでした。",
                    DiagnosticSeverity.Medium => "軽微な問題が検出されました。",
                    DiagnosticSeverity.High => "改善が必要な問題が検出されました。",
                    DiagnosticSeverity.Critical => "重要な問題が検出されました。すぐに対処してください。",
                    _ => "診断が完了しました。"
                };
                
                var message = $"{summary}\n\n対象ネットワーク: {result.SSID}";
                
                if (result.PrimaryIssue != null)
                {
                    message += $"\n\n主要問題: {result.PrimaryIssue.Description}";
                }
                
                if (result.RecommendedActions.Any())
                {
                    message += "\n\n推奨対処法:";
                    var topRecommendations = result.RecommendedActions.Take(3);
                    foreach (var action in topRecommendations)
                    {
                        message += $"\n• {action}";
                    }
                    
                    if (result.RecommendedActions.Count > 3)
                    {
                        message += $"\n（他 {result.RecommendedActions.Count - 3} 件の推奨事項があります）";
                    }
                }
                
                var icon = result.OverallSeverity switch
                {
                    DiagnosticSeverity.Critical => MessageBoxImage.Error,
                    DiagnosticSeverity.High => MessageBoxImage.Warning,
                    DiagnosticSeverity.Medium => MessageBoxImage.Information,
                    _ => MessageBoxImage.Information
                };
                
                var diagnosticResult = System.Windows.MessageBox.Show(
                    message + "\n\n詳細な診断情報を表示しますか？",
                    "接続診断結果",
                    MessageBoxButton.YesNo,
                    icon);
                    
                if (diagnosticResult == MessageBoxResult.Yes)
                {
                    Task.Run(async () => await ShowDetailedDiagnosticsAsync());
                }
            });
        }
        
        public async Task<bool> TriggerSmartConnectionAnalysisAsync()
        {
            try
            {
                var recommendation = await _smartConnectionManager.AnalyzeAndRecommendSwitchAsync();
                if (recommendation != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var result = System.Windows.MessageBox.Show(
                            $"{recommendation.GetRecommendationText()}\n\n切り替えを実行しますか？",
                            "スマート接続分析結果",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            Task.Run(async () =>
                            {
                                await _smartConnectionManager.ExecuteSmartSwitchAsync(recommendation.RecommendedSSID);
                            });
                        }
                    });
                    return true;
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        System.Windows.MessageBox.Show(
                            "現在の接続は最適です。切り替えの必要はありません。",
                            "スマート接続分析結果",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                    return false;
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.TriggerSmartConnectionAnalysis", ex, _connectionLogger);
                return false;
            }
        }
        
        public ConnectionHealthStatus GetCurrentConnectionHealth()
        {
            return _healthChecker?.GetLastHealthStatus() ?? new ConnectionHealthStatus { Quality = ConnectionQuality.Unknown };
        }
        
        private async Task PerformSystemHealthCheckAsync()
        {
            try
            {
                var healthStatus = await ErrorHandler.PerformSystemHealthCheckAsync(_connectionLogger);
                
                // 健全性に問題がある場合は通知
                if (healthStatus.OverallHealth == HealthLevel.Critical)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (QuickSettingsManager.GetSetting("tray_notifications", true))
                        {
                            _systemTrayManager?.ShowBalloonTip(
                                "システム健全性警告",
                                $"システムの健全性に問題があります: {healthStatus.GetHealthSummary()}",
                                System.Windows.Forms.ToolTipIcon.Warning);
                        }
                    });
                    
                    // メモリ圧迫時は緊急クリーンアップ
                    if (healthStatus.MemoryPressure)
                    {
                        SystemManager.OptimizeMemory();
                        GC.Collect(2, GCCollectionMode.Forced);
                        GC.WaitForPendingFinalizers();
                    }
                }
                else if (healthStatus.OverallHealth == HealthLevel.Warning && QuickSettingsManager.GetSetting("detailed_logging", false))
                {
                    _connectionLogger.Log(ConnectionLogger.LogLevel.Info, "HealthCheck", 
                        $"システム健全性: {healthStatus.GetHealthSummary()}");
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.PerformSystemHealthCheck", ex, _connectionLogger);
            }
        }
        
        // Event Handlers (統合)
        private void OnPerformanceAlert(object sender, PerformanceAlertEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var iconType = e.Severity switch
                {
                    AlertSeverity.Critical => MessageBoxImage.Error,
                    AlertSeverity.Warning => MessageBoxImage.Warning,
                    _ => MessageBoxImage.Information
                };
                
                if (e.Severity == AlertSeverity.Critical)
                {
                    System.Windows.MessageBox.Show(e.Message, "パフォーマンス警告", MessageBoxButton.OK, iconType);
                }
                else
                {
                    _systemTrayManager?.ShowBalloonTip("パフォーマンス", e.Message, System.Windows.Forms.ToolTipIcon.Info);
                }
            });
        }
        
        private void OnReportGenerated(object sender, PerformanceReportEventArgs e)
        {
            _connectionLogger?.Log(ConnectionLogger.LogLevel.Info, "Performance", 
                $"パフォーマンスレポート生成: スコア {e.Report.PerformanceScore}/100, CPU平均 {e.Report.AverageCpuUsage:F1}%, メモリ平均 {e.Report.AverageMemoryUsage:F1}%");
        }
        
        private void OnRecoveryStarted(object sender, RecoveryEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _systemTrayManager?.ShowBalloonTip("復旧開始", 
                    $"{e.SSID} の接続復旧を開始しました (戦略: {e.Strategy})", System.Windows.Forms.ToolTipIcon.Info);
            });
        }
        
        private void OnRecoveryCompleted(object sender, RecoveryEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _systemTrayManager?.ShowBalloonTip("復旧成功", 
                    $"{e.SSID} の接続復旧が成功しました", System.Windows.Forms.ToolTipIcon.Info);
            });
        }
        
        private void OnRecoveryFailed(object sender, RecoveryEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _connectionLogger?.Log(ConnectionLogger.LogLevel.Warning, "Recovery", 
                    $"復旧失敗: {e.SSID} - {e.ErrorMessage}");
            });
        }
        
        
        private void OnBatchOperationCompleted(object sender, EventArgs e)
        {
            // バッチ操作完了処理
        }
        
        private void SystemTrayManager_ShowMainWindowRequested(object sender, EventArgs e)
        {
            try
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Focus();
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.SystemTrayManager_ShowMainWindowRequested", ex);
            }
        }
        
        private void SystemTrayManager_ExitApplicationRequested(object sender, EventArgs e)
        {
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.SystemTrayManager_ExitApplicationRequested", ex);
            }
        }
        
        private async void SystemTrayManager_QuickConnectRequested(object sender, EventArgs e)
        {
            try
            {
                if (QuickConnectButton.IsEnabled && QuickConnectButton.Visibility == Visibility.Visible)
                {
                    await QuickConnectButton_Click(sender, new RoutedEventArgs());
                }
                else
                {
                    _systemTrayManager?.ShowBalloonTip("クイック接続", "利用可能な履歴接続先がありません", System.Windows.Forms.ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.SystemTrayManager_QuickConnectRequested", ex);
            }
        }
        
        private void OnNetworkStatusChanged(object? sender, NetworkStatusChangedEventArgs e)
        {
            // システムトレイのステータス更新
            _systemTrayManager?.UpdateConnectionStatus(e.IsConnected, e.ConnectedSSID);
            
            // ネットワーク接続状態変更時にWiFiリスト更新
            App.Current?.Dispatcher?.InvokeAsync(async () =>
            {
                try
                {
                    await LoadWifiNetworksAsync();
                    
                    // 接続成功時の通知
                    if (e.IsConnected && !string.IsNullOrEmpty(e.ConnectedSSID))
                    {
                        // タイトルバーを更新
                        Title = $"Murti WiFi Connecter - 接続中: {e.ConnectedSSID}";
                        
                        // システムトレイ通知
                        _systemTrayManager?.ShowBalloonTip(
                            "WiFi接続完了", 
                            $"{e.ConnectedSSID} に接続しました", 
                            System.Windows.Forms.ToolTipIcon.Info);
                        
                        // 接続統計を記録
                        _connectionStats.RecordConnectionAttempt(e.ConnectedSSID, true, TimeSpan.FromSeconds(5));
                    }
                    else
                    {
                        Title = "Murti WiFi Connecter";
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
                Hide();
            }
        }
        
        /// <summary>
        /// 効率的なネットワークリスト更新
        /// </summary>
        private void UpdateNetworkListEfficiently(List<WifiNetwork> newNetworks)
        {
            try
            {
                // 仮想化対応：大量のネットワークリストに対する最適化
                if (newNetworks.Count > 100)
                {
                    // 100個を超える場合は信号強度の高いものだけを表示
                    newNetworks = newNetworks
                        .OrderByDescending(n => n.IsConnected)
                        .ThenByDescending(n => n.HasConnectedBefore)
                        .ThenByDescending(n => n.SignalStrength)
                        .Take(50)
                        .ToList();
                }
                
                // 既存リストとの高効率差分チェック
                if (WifiNetworks.Count == newNetworks.Count)
                {
                    bool hasChanges = false;
                    for (int i = 0; i < newNetworks.Count; i++)
                    {
                        if (i < WifiNetworks.Count)
                        {
                            var existing = WifiNetworks[i];
                            var newItem = newNetworks[i];
                            if (!string.Equals(existing.SSID, newItem.SSID, StringComparison.OrdinalIgnoreCase) ||
                                existing.SignalStrength != newItem.SignalStrength ||
                                existing.IsConnected != newItem.IsConnected)
                            {
                                hasChanges = true;
                                break;
                            }
                        }
                    }
                    
                    if (!hasChanges) return; // 変更がない場合は更新をスキップ
                }
                
                // バッチ更新でUI再描画を最小化
                WifiNetworks.Clear();
                foreach (var network in newNetworks)
                {
                    WifiNetworks.Add(network);
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("MainWindow.UpdateNetworkListEfficiently", ex, _connectionLogger);
            }
        }
        
        /// <summary>
        /// ノンブロッキングエラー通知
        /// </summary>
        private async Task ShowNonBlockingErrorAsync(string title, string message)
        {
            try
            {
                // システムトレイ通知を優先
                _systemTrayManager?.ShowBalloonTip(title, message, System.Windows.Forms.ToolTipIcon.Warning);
                
                // ログにも記録
                _connectionLogger?.Log(ConnectionLogger.LogLevel.Warning, "UserNotification", $"{title}: {message}");
                
                // 重要なエラーの場合のみダイアログ表示（バックグラウンドで）
                if (title.Contains("メモリ") || title.Contains("権限"))
                {
                    _ = Task.Delay(1000).ContinueWith(_ => 
                        Dispatcher.InvokeAsync(() => 
                        {
                            if (IsActive) // ウィンドウがアクティブな場合のみ
                            {
                                System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }, System.Windows.Threading.DispatcherPriority.Background));
                }
            }
            catch (Exception ex)
            {
                // エラー通知でエラーが発生した場合は無視
                ErrorHandler.LogError("MainWindow.ShowNonBlockingErrorAsync", ex);
            }
        }
        
        /// <summary>
        /// 進捗表示付きの長時間操作実行
        /// </summary>
        private async Task<T> ExecuteWithProgressAsync<T>(Func<CancellationToken, Task<T>> operation, string operationName)
        {
            var progressTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            
            var dots = "";
            progressTimer.Tick += (s, e) =>
            {
                dots = dots.Length >= 3 ? "" : dots + ".";
                if (ConnectButton.Content.ToString()?.StartsWith(operationName) == true)
                {
                    ConnectButton.Content = $"{operationName}{dots}";
                }
            };
            
            progressTimer.Start();
            
            try
            {
                return await operation(_cancellationTokenSource.Token);
            }
            finally
            {
                progressTimer.Stop();
                progressTimer = null;
            }
        }
        
        /// <summary>
        /// 信号強度に基づく品質説明を取得
        /// </summary>
        private static string GetSignalQualityDescription(int signalStrength)
        {
            return signalStrength switch
            {
                >= 80 => "優秀",
                >= 60 => "良好", 
                >= 40 => "普通",
                >= 20 => "弱い",
                _ => "非常に弱い"
            };
        }
    }


    public class WifiNetworkComparer : IEqualityComparer<WifiNetwork>
    {
        public bool Equals(WifiNetwork x, WifiNetwork y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            
            return string.Equals(x.SSID, y.SSID, StringComparison.OrdinalIgnoreCase) &&
                   x.SignalStrength == y.SignalStrength &&
                   x.IsConnected == y.IsConnected &&
                   x.HasConnectedBefore == y.HasConnectedBefore;
        }

        public int GetHashCode(WifiNetwork obj)
        {
            if (obj is null) return 0;
            return HashCode.Combine(
                obj.SSID?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0,
                obj.SignalStrength,
                obj.IsConnected,
                obj.HasConnectedBefore
            );
        }
        
        
    }
}