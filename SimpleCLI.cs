using System;
using System.Threading.Tasks;
using System.Linq;
using MurtiWifiConnecter.Services;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// シンプルなコマンドラインインターフェース
    /// </summary>
    public class SimpleCLI
    {
        private readonly OptimizedWifiManager _wifiManager;
        private readonly AutoReconnectService _autoReconnect;
        private readonly ConnectionHistory _history;
        private readonly ComprehensiveBackupManager _backupManager;
        private readonly NetworkPriorityManager _priorityManager;
        private readonly EnhancedWifiScanner _scanner;
        private readonly WiFiTroubleshooter _troubleshooter;
        private bool _running = true;

        public SimpleCLI()
        {
            // サービス初期化
            var wifiService = new WifiService();
            var connectionService = new ConnectionManagementService(
                null, // logger removed
                new ConnectionRetryManager(),
                new AutoConnectManager(),
                null  // monitor removed - will create lightweight version
            );
            var monitoring = new LightweightMonitoringService();
            
            _wifiManager = new OptimizedWifiManager(
                wifiService,
                connectionService,
                monitoring
            );
            
            _autoReconnect = new AutoReconnectService(_wifiManager);
            _history = new ConnectionHistory();
            _priorityManager = new NetworkPriorityManager();
            _backupManager = new ComprehensiveBackupManager(null, _priorityManager);
            _scanner = new EnhancedWifiScanner(_priorityManager);
            _troubleshooter = new WiFiTroubleshooter(_scanner);
        }

        public async Task RunAsync(string[] args)
        {
            // コマンドライン引数処理
            if (args != null && args.Length > 0)
            {
                await ProcessCommand(args);
                return;
            }

            // インタラクティブモード
            Console.WriteLine("=== MurtiWifi Connector CLI ===");
            Console.WriteLine("Type 'help' for available commands\n");

            while (_running)
            {
                Console.Write("> ");
                var input = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                await ProcessCommand(parts);
            }
        }

        private async Task ProcessCommand(string[] args)
        {
            if (args.Length == 0) return;

            var command = args[0].ToLower();

            try
            {
                switch (command)
                {
                    case "help":
                    case "?":
                        ShowHelp();
                        break;

                    case "scan":
                        await ScanNetworksAsync();
                        break;

                    case "connect":
                        if (args.Length < 3)
                        {
                            Console.WriteLine("Usage: connect <SSID> <password>");
                            break;
                        }
                        await ConnectAsync(args[1], args[2]);
                        break;

                    case "disconnect":
                        await DisconnectAsync();
                        break;

                    case "status":
                        await ShowStatusAsync();
                        break;

                    case "speed":
                        await TestSpeedAsync();
                        break;

                    case "history":
                        ShowHistory();
                        break;

                    case "auto":
                        if (args.Length > 1)
                        {
                            SetAutoReconnect(args[1]);
                        }
                        else
                        {
                            Console.WriteLine($"Auto-reconnect: {(_autoReconnect.IsEnabled ? "Enabled" : "Disabled")}");
                        }
                        break;

                    case "monitor":
                        ShowMonitoringInfo();
                        break;

                    case "backup":
                        if (args.Length > 1)
                            await CreateBackupAsync(args[1]);
                        else
                            await CreateBackupAsync();
                        break;

                    case "restore":
                        if (args.Length > 1)
                            await RestoreBackupAsync(args[1]);
                        else
                            await ShowBackupsAsync();
                        break;

                    case "backups":
                        await ShowBackupsAsync();
                        break;

                    case "priority":
                        if (args.Length > 2)
                            await SetPriorityAsync(args[1], args[2]);
                        else
                            await ShowPrioritiesAsync();
                        break;

                    case "diagnose":
                        await RunDiagnosticsAsync();
                        break;

                    case "quality":
                        await ShowConnectionQualityAsync();
                        break;

                    case "cache":
                        if (args.Length > 1 && args[1].ToLower() == "clear")
                            ClearCache();
                        else
                            ShowCacheStats();
                        break;

                    case "troubleshoot":
                        await RunTroubleshootingAsync();
                        break;

                    case "clear":
                        Console.Clear();
                        break;

                    case "exit":
                    case "quit":
                        _running = false;
                        Console.WriteLine("Goodbye!");
                        break;

                    default:
                        Console.WriteLine($"Unknown command: {command}. Type 'help' for available commands.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private void ShowHelp()
        {
            Console.WriteLine("\nAvailable Commands:");
            Console.WriteLine("\n=== Connection ===");
            Console.WriteLine("  scan                 - Scan for available WiFi networks");
            Console.WriteLine("  connect <SSID> <pw>  - Connect to a WiFi network");
            Console.WriteLine("  disconnect           - Disconnect from current network");
            Console.WriteLine("  status               - Show connection status");
            
            Console.WriteLine("\n=== Diagnostics ===");
            Console.WriteLine("  diagnose             - Run comprehensive network diagnosis");
            Console.WriteLine("  troubleshoot         - Auto-diagnose and fix common issues");
            Console.WriteLine("  quality              - Show detailed connection quality");
            Console.WriteLine("  speed                - Test internet speed");
            
            Console.WriteLine("\n=== Management ===");
            Console.WriteLine("  priority <SSID> <num> - Set network priority (0-100)");
            Console.WriteLine("  priority             - Show network priorities");
            Console.WriteLine("  backup [name]        - Create system backup");
            Console.WriteLine("  restore [name]       - Restore from backup");
            Console.WriteLine("  backups              - List available backups");
            Console.WriteLine("  history              - Show connection history");
            Console.WriteLine("  auto [on/off]        - Enable/disable auto-reconnect");
            
            Console.WriteLine("\n=== System ===");
            Console.WriteLine("  cache [clear]        - Show cache stats or clear cache");
            Console.WriteLine("  monitor              - Show system monitoring info");
            Console.WriteLine("  clear                - Clear screen");
            Console.WriteLine("  help                 - Show this help");
            Console.WriteLine("  exit                 - Exit application");
            Console.WriteLine();
        }

        private async Task ScanNetworksAsync()
        {
            Console.WriteLine("Scanning for WiFi networks...");
            
            var networks = await _wifiManager.ScanNetworksAsync();
            
            if (networks == null || networks.Count == 0)
            {
                Console.WriteLine("No networks found.");
                return;
            }

            Console.WriteLine($"\nFound {networks.Count} network(s):");
            Console.WriteLine("----------------------------------------");
            
            foreach (var network in networks.OrderByDescending(n => n.SignalStrength))
            {
                var signal = network.SignalStrength > 0 
                    ? $"{network.SignalStrength}%" 
                    : "N/A";
                    
                var status = network.IsConnected ? " [CONNECTED]" : "";
                
                Console.WriteLine($"  {network.SSID,-30} Signal: {signal,5}{status}");
            }
            
            Console.WriteLine();
        }

        private async Task ConnectAsync(string ssid, string password)
        {
            Console.WriteLine($"Connecting to '{ssid}'...");
            
            var result = await _wifiManager.ConnectAsync(ssid, password);
            
            if (result.Success)
            {
                Console.WriteLine($"Successfully connected to '{ssid}'");
                
                // 履歴に追加
                _history.AddEntry(ssid, true);
                
                // 自動再接続用に保存
                _autoReconnect.SaveCredentials(ssid, password);
            }
            else
            {
                Console.WriteLine($"Failed to connect: {result.ErrorMessage}");
                _history.AddEntry(ssid, false);
            }
        }

        private async Task DisconnectAsync()
        {
            var currentSSID = await _wifiManager.GetCurrentSSIDAsync();
            
            if (string.IsNullOrEmpty(currentSSID))
            {
                Console.WriteLine("Not connected to any network.");
                return;
            }

            Console.WriteLine($"Disconnecting from '{currentSSID}'...");
            
            if (await _wifiManager.DisconnectAsync())
            {
                Console.WriteLine("Disconnected successfully.");
            }
            else
            {
                Console.WriteLine("Failed to disconnect.");
            }
        }

        private async Task ShowStatusAsync()
        {
            var currentSSID = await _wifiManager.GetCurrentSSIDAsync();
            var status = _wifiManager.GetStatus();
            
            Console.WriteLine("\n=== Connection Status ===");
            
            if (!string.IsNullOrEmpty(currentSSID))
            {
                Console.WriteLine($"Connected to: {currentSSID}");
                Console.WriteLine($"Signal: {status.WifiSignalStrength}%");
                
                // インターネット接続テスト
                var hasInternet = await _wifiManager.TestInternetConnectionAsync();
                Console.WriteLine($"Internet: {(hasInternet ? "Connected" : "No access")}");
            }
            else
            {
                Console.WriteLine("Status: Not connected");
            }
            
            Console.WriteLine($"\n=== System Status ===");
            Console.WriteLine($"Health: {(status.IsHealthy ? "Good" : "Issues detected")}");
            Console.WriteLine($"Memory: {status.MemoryUsageMB:F1} MB");
            Console.WriteLine($"Uptime: {status.GetUptimeString()}");
            
            var reconnectStatus = _autoReconnect.GetStatus();
            Console.WriteLine($"\n=== Auto-Reconnect ===");
            Console.WriteLine(reconnectStatus.GetStatusText());
            
            Console.WriteLine();
        }

        private async Task TestSpeedAsync()
        {
            Console.WriteLine("Running speed test...");
            
            var result = await _wifiManager.RunSpeedTestAsync();
            
            if (result.Success)
            {
                Console.WriteLine($"\nSpeed Test Results:");
                Console.WriteLine($"  Download: {result.DownloadSpeedMbps:F1} Mbps");
                Console.WriteLine($"  Test duration: {result.Duration.TotalSeconds:F1} seconds");
            }
            else
            {
                Console.WriteLine($"Speed test failed: {result.Message}");
            }
        }

        private void ShowHistory()
        {
            var recentEntries = _history.GetRecentEntries(10);
            
            if (recentEntries == null || recentEntries.Count == 0)
            {
                Console.WriteLine("No connection history available.");
                return;
            }

            Console.WriteLine("\n=== Recent Connections ===");
            Console.WriteLine("Time                 SSID                     Success");
            Console.WriteLine("-------------------------------------------------------");
            
            foreach (var entry in recentEntries)
            {
                var status = entry.Success ? "Success" : "Failed ";
                Console.WriteLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}  {entry.SSID,-25} {status}");
            }
            
            Console.WriteLine();
            
            // 統計表示
            var stats = _history.GetStatistics();
            if (stats.Count > 0)
            {
                Console.WriteLine("=== Statistics ===");
                foreach (var stat in stats.OrderByDescending(s => s.Value))
                {
                    Console.WriteLine($"  {stat.Key}: {stat.Value} connections");
                }
                Console.WriteLine();
            }
        }

        private void SetAutoReconnect(string state)
        {
            switch (state.ToLower())
            {
                case "on":
                case "enable":
                case "true":
                    _autoReconnect.Start();
                    Console.WriteLine("Auto-reconnect enabled.");
                    break;
                    
                case "off":
                case "disable":
                case "false":
                    _autoReconnect.Stop();
                    Console.WriteLine("Auto-reconnect disabled.");
                    break;
                    
                default:
                    Console.WriteLine("Usage: auto [on/off]");
                    break;
            }
        }

        private void ShowMonitoringInfo()
        {
            var status = _wifiManager.GetStatus();
            
            Console.WriteLine("\n=== System Monitoring ===");
            Console.WriteLine(status.HealthSummary);
            Console.WriteLine();
        }

        private async Task CreateBackupAsync(string name = null)
        {
            Console.WriteLine("Creating backup...");
            
            try
            {
                var result = await _backupManager.CreateFullBackupAsync(name);
                
                if (result.Success)
                {
                    Console.WriteLine($"Backup created successfully: {result.BackupName}");
                    Console.WriteLine($"  Profiles: {(result.ProfileBackupSuccess ? $"{result.ProfileCount} backed up" : "Failed")}");
                    Console.WriteLine($"  Priorities: {(result.PriorityBackupSuccess ? "Backed up" : "Failed")}");
                    Console.WriteLine($"  Settings: {(result.SettingsBackupSuccess ? "Backed up" : "Failed")}");
                    Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F1} seconds");
                }
                else
                {
                    Console.WriteLine("Backup failed:");
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"  - {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Backup error: {ex.Message}");
            }
        }

        private async Task RestoreBackupAsync(string name)
        {
            Console.WriteLine($"Restoring from backup: {name}");
            
            try
            {
                var options = new RestoreOptions
                {
                    RestoreProfiles = true,
                    RestorePriorities = true,
                    RestoreSettings = true,
                    OverwriteExistingProfiles = false
                };
                
                var result = await _backupManager.RestoreFromBackupAsync(name, options);
                
                if (result.Success)
                {
                    Console.WriteLine("Restore completed successfully:");
                    Console.WriteLine($"  Profiles: {(result.ProfileRestoreSuccess ? $"{result.ProfilesRestored} restored" : "Skipped")}");
                    Console.WriteLine($"  Priorities: {(result.PriorityRestoreSuccess ? "Restored" : "Skipped")}");
                    Console.WriteLine($"  Settings: {(result.SettingsRestoreSuccess ? "Restored" : "Skipped")}");
                    Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F1} seconds");
                }
                else
                {
                    Console.WriteLine("Restore failed:");
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"  - {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Restore error: {ex.Message}");
            }
        }

        private async Task ShowBackupsAsync()
        {
            Console.WriteLine("Available backups:");
            
            try
            {
                var backups = await _backupManager.GetAvailableBackupsAsync();
                
                if (backups.Count == 0)
                {
                    Console.WriteLine("  No backups found.");
                    return;
                }
                
                Console.WriteLine($"{"Name",-30} {"Date",-20} {"Size",-10} {"Profiles",-8}");
                Console.WriteLine(new string('-', 70));
                
                foreach (var backup in backups)
                {
                    var size = backup.Size > 1024 * 1024 ? $"{backup.Size / 1024 / 1024:F1}MB" : $"{backup.Size / 1024:F0}KB";
                    Console.WriteLine($"{backup.Name,-30} {backup.CreatedAt:yyyy-MM-dd HH:mm,-20} {size,-10} {backup.ProfileCount,-8}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing backups: {ex.Message}");
            }
        }

        private async Task SetPriorityAsync(string ssid, string priorityStr)
        {
            try
            {
                if (!int.TryParse(priorityStr, out var priority))
                {
                    Console.WriteLine("Priority must be a number between 0 and 100.");
                    return;
                }
                
                await _priorityManager.SetPriorityAsync(ssid, priority);
                Console.WriteLine($"Set priority {priority} for network: {ssid}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting priority: {ex.Message}");
            }
        }

        private async Task ShowPrioritiesAsync()
        {
            Console.WriteLine("Network priorities:");
            
            try
            {
                var priorities = await _priorityManager.GetPriorityListAsync();
                
                if (priorities.Count == 0)
                {
                    Console.WriteLine("  No network priorities set.");
                    return;
                }
                
                Console.WriteLine($"{"SSID",-25} {"Priority",-8} {"Auto",-5} {"Connections",-11}");
                Console.WriteLine(new string('-', 55));
                
                foreach (var priority in priorities)
                {
                    Console.WriteLine($"{priority.SSID,-25} {priority.Priority,-8} {(priority.AutoConnect ? "Yes" : "No"),-5} {priority.ConnectionCount,-11}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing priorities: {ex.Message}");
            }
        }

        private async Task RunDiagnosticsAsync()
        {
            Console.WriteLine("Running network diagnostics...");
            
            try
            {
                var diagnostics = await NetworkDiagnostics.RunBasicDiagnosticsAsync();
                
                Console.WriteLine($"\n=== Network Diagnostics ({diagnostics.TestTime:HH:mm:ss}) ===");
                Console.WriteLine($"Overall Status: {diagnostics.GetStatusDescription()}");
                Console.WriteLine($"Summary: {diagnostics.Summary}");
                
                Console.WriteLine("\nDetailed Results:");
                foreach (var result in diagnostics.Results)
                {
                    var status = result.IsSuccess ? "✓" : "✗";
                    Console.WriteLine($"  {status} {result.TestName}: {result.Message}");
                }
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Diagnostics failed: {ex.Message}");
            }
        }

        private async Task ShowConnectionQualityAsync()
        {
            Console.WriteLine("Analyzing connection quality...");
            
            try
            {
                var quality = await QuickStatusChecker.AssessConnectionQualityAsync();
                
                Console.WriteLine($"\n=== Connection Quality ({quality.TestTime:HH:mm:ss}) ===");
                if (quality.HasValidData)
                {
                    Console.WriteLine($"Signal Strength: {quality.SignalStrength}%");
                    Console.WriteLine($"Average Latency: {quality.AverageLatency}ms");
                    Console.WriteLine($"Overall Score: {quality.OverallScore}/100");
                }
                else
                {
                    Console.WriteLine("Unable to assess connection quality - no active connection");
                }
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Quality assessment failed: {ex.Message}");
            }
        }

        private void ShowCacheStats()
        {
            Console.WriteLine("\n=== Cache Statistics ===");
            try
            {
                var stats = ConnectionCache.GetStatistics();
                Console.WriteLine($"Total Entries: {stats.TotalEntries}");
                Console.WriteLine($"Valid Entries: {stats.ValidEntries}");
                Console.WriteLine($"Expired Entries: {stats.ExpiredEntries}");
                Console.WriteLine($"Total Hits: {stats.TotalHits}");
                Console.WriteLine($"Average Hits: {stats.AverageHits:F1}");
                Console.WriteLine($"Hit Ratio: {stats.HitRatio:P1}");
                Console.WriteLine($"Last Cleanup: {(stats.LastCleanup == DateTime.MinValue ? "Never" : stats.LastCleanup.ToString("yyyy-MM-dd HH:mm:ss"))}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting cache stats: {ex.Message}");
            }
            Console.WriteLine();
        }

        private void ClearCache()
        {
            try
            {
                ConnectionCache.Clear();
                Console.WriteLine("Cache cleared successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing cache: {ex.Message}");
            }
        }

        private async Task RunTroubleshootingAsync()
        {
            Console.WriteLine("Running WiFi troubleshooting...");
            
            try
            {
                var report = await _troubleshooter.DiagnoseAsync();
                
                Console.WriteLine($"\n=== Troubleshooting Report ({report.DiagnosisTime:HH:mm:ss}) ===");
                Console.WriteLine($"Overall Health: {report.OverallHealth}");
                
                if (report.Issues.Any())
                {
                    Console.WriteLine($"\nIssues Found ({report.Issues.Count}):");
                    foreach (var issue in report.Issues)
                    {
                        var severity = issue.Severity switch
                        {
                            IssueSeverity.High => "[HIGH]",
                            IssueSeverity.Medium => "[MED]",
                            IssueSeverity.Low => "[LOW]",
                            _ => "[?]"
                        };
                        
                        Console.WriteLine($"  {severity} {issue.Title}");
                        Console.WriteLine($"    Problem: {issue.Description}");
                        Console.WriteLine($"    Solution: {issue.Solution}");
                        Console.WriteLine();
                    }
                }
                else
                {
                    Console.WriteLine("\nNo issues detected!");
                }
                
                if (report.Recommendations.Any())
                {
                    Console.WriteLine("Recommendations:");
                    foreach (var rec in report.Recommendations)
                    {
                        Console.WriteLine($"  • {rec}");
                    }
                }
                
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Troubleshooting failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _autoReconnect?.Dispose();
            _wifiManager?.Dispose();
            _history?.Dispose();
            _backupManager?.Dispose();
            _priorityManager?.Dispose();
            _scanner?.Dispose();
        }
    }
}