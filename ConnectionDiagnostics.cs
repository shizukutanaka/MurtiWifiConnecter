using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Threading;

namespace MurtiWifiConnecter
{
    public class ConnectionDiagnostics : IDisposable
    {
        private readonly ConnectionLogger _logger;
        private readonly Timer _diagnosticTimer;
        private readonly Dictionary<string, DiagnosticResult> _recentDiagnoses = new();
        private bool _disposed = false;
        
        public event EventHandler<DiagnosticEventArgs>? DiagnosticCompleted;
        
        private const int DiagnosticIntervalMs = 30000; // 30秒間隔
        private const int MaxDiagnosticHistory = 50;
        
        public ConnectionDiagnostics(ConnectionLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _diagnosticTimer = new Timer(PerformPeriodicDiagnostic, null, DiagnosticIntervalMs, DiagnosticIntervalMs);
        }
        
        public async Task<DiagnosticResult> DiagnoseConnectionIssueAsync(Exception? exception = null, string? ssid = null)
        {
            var result = new DiagnosticResult
            {
                Timestamp = DateTime.Now,
                SSID = ssid ?? await FastWifiConnector.GetCurrentConnectedSSIDAsync() ?? "",
                TriggerException = exception
            };
            
            try
            {
                _logger.Log(ConnectionLogger.LogLevel.Info, "Diagnostics", $"WiFi接続診断を開始: {result.SSID}");
                
                var diagnosticChecks = new List<Task<DiagnosticCheck>>
                {
                    CheckNetworkAdapterAsync(result.SSID),
                    CheckWifiServiceAsync(),
                    CheckNetworkConnectivityAsync(),
                    CheckDnsResolutionAsync(),
                    CheckSignalStrengthAsync(result.SSID),
                    CheckNetworkProfileAsync(result.SSID),
                    CheckIpConfigurationAsync()
                };
                
                var checks = await Task.WhenAll(diagnosticChecks);
                result.Checks.AddRange(checks);
                
                result.OverallSeverity = DetermineSeverity(result.Checks);
                result.PrimaryIssue = IdentifyPrimaryIssue(result.Checks, exception);
                result.RecommendedActions = GenerateRecommendations(result.Checks, result.PrimaryIssue);
                
                StoreRecentDiagnosis(result);
                OnDiagnosticCompleted(new DiagnosticEventArgs { Result = result });
                
                _logger.Log(ConnectionLogger.LogLevel.Info, "Diagnostics", 
                    $"診断完了: {result.PrimaryIssue?.IssueType} (重要度: {result.OverallSeverity})");
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionDiagnostics.DiagnoseConnectionIssueAsync", ex, _logger);
                result.HasDiagnosticError = true;
                result.DiagnosticError = ex.Message;
            }
            
            return result;
        }
        
        private async Task<DiagnosticCheck> CheckNetworkAdapterAsync(string ssid)
        {
            var check = new DiagnosticCheck
            {
                CheckName = "NetworkAdapter",
                Description = "ネットワークアダプターの状態確認"
            };
            
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                var wifiInterface = interfaces.FirstOrDefault(i => 
                    i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
                
                if (wifiInterface == null)
                {
                    check.Status = DiagnosticStatus.Critical;
                    check.Issue = "WiFiアダプターが見つかりません";
                    check.Recommendation = "WiFiアダプターが正しく取り付けられ、有効になっているか確認してください";
                    return check;
                }
                
                if (wifiInterface.OperationalStatus != OperationalStatus.Up)
                {
                    check.Status = DiagnosticStatus.Critical;
                    check.Issue = $"WiFiアダプターが無効です ({wifiInterface.OperationalStatus})";
                    check.Recommendation = "デバイスマネージャーでWiFiアダプターを有効にしてください";
                    return check;
                }
                
                var stats = wifiInterface.GetIPStatistics();
                if (stats.BytesReceived == 0 && stats.BytesSent == 0)
                {
                    check.Status = DiagnosticStatus.Warning;
                    check.Issue = "ネットワークアクティビティが検出されません";
                    check.Recommendation = "WiFi接続を一度無効にして再度有効にしてください";
                }
                else
                {
                    check.Status = DiagnosticStatus.Healthy;
                    check.Details = $"アダプター: {wifiInterface.Name}, 状態: {wifiInterface.OperationalStatus}";
                }
            }
            catch (Exception ex)
            {
                check.Status = DiagnosticStatus.Error;
                check.Issue = $"アダプター確認エラー: {ex.Message}";
                check.Recommendation = "システムを再起動してください";
            }
            
            return check;
        }
        
        private async Task<DiagnosticCheck> CheckWifiServiceAsync()
        {
            var check = new DiagnosticCheck
            {
                CheckName = "WifiService",
                Description = "Windows WiFiサービスの状態確認"
            };
            
            try
            {
                var serviceNames = new[] { "Wlansvc", "Netman", "Dhcp" };
                var serviceIssues = new List<string>();
                
                foreach (var serviceName in serviceNames)
                {
                    using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Service WHERE Name = '{serviceName}'");
                    using var results = searcher.Get();
                    
                    foreach (ManagementObject service in results)
                    {
                        var state = service["State"].ToString();
                        var startMode = service["StartMode"].ToString();
                        
                        if (state != "Running")
                        {
                            serviceIssues.Add($"{serviceName}サービスが停止中 ({state})");
                        }
                    }
                }
                
                if (serviceIssues.Any())
                {
                    check.Status = DiagnosticStatus.Critical;
                    check.Issue = string.Join(", ", serviceIssues);
                    check.Recommendation = "管理者権限でサービス管理ツールを開き、必要なサービスを開始してください";
                }
                else
                {
                    check.Status = DiagnosticStatus.Healthy;
                    check.Details = "WiFi関連サービスは正常に動作しています";
                }
            }
            catch (Exception ex)
            {
                check.Status = DiagnosticStatus.Error;
                check.Issue = $"サービス確認エラー: {ex.Message}";
                check.Recommendation = "管理者権限で実行してください";
            }
            
            return check;
        }
        
        private async Task<DiagnosticCheck> CheckNetworkConnectivityAsync()
        {
            var check = new DiagnosticCheck
            {
                CheckName = "NetworkConnectivity",
                Description = "インターネット接続の確認"
            };
            
            try
            {
                using var ping = new Ping();
                var targets = new[] { "8.8.8.8", "1.1.1.1", "208.67.222.222" };
                var successCount = 0;
                var latencies = new List<long>();
                
                foreach (var target in targets)
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(target, 3000);
                        if (reply.Status == IPStatus.Success)
                        {
                            successCount++;
                            latencies.Add(reply.RoundtripTime);
                        }
                    }
                    catch
                    {
                        // 個別のpingエラーは無視
                    }
                }
                
                if (successCount == 0)
                {
                    check.Status = DiagnosticStatus.Critical;
                    check.Issue = "インターネット接続が確認できません";
                    check.Recommendation = "モデム/ルーターの電源を入れ直し、ケーブル接続を確認してください";
                }
                else if (successCount < targets.Length)
                {
                    check.Status = DiagnosticStatus.Warning;
                    check.Issue = $"一部のサーバーに接続できません ({successCount}/{targets.Length})";
                    check.Recommendation = "DNSサーバー設定を確認してください";
                }
                else
                {
                    var avgLatency = latencies.Average();
                    check.Status = DiagnosticStatus.Healthy;
                    check.Details = $"平均レイテンシ: {avgLatency:F0}ms";
                    
                    if (avgLatency > 200)
                    {
                        check.Status = DiagnosticStatus.Warning;
                        check.Issue = "レイテンシが高い状態です";
                        check.Recommendation = "ルーターに近づくか、他のネットワークデバイスの使用を確認してください";
                    }
                }
            }
            catch (Exception ex)
            {
                check.Status = DiagnosticStatus.Error;
                check.Issue = $"接続確認エラー: {ex.Message}";
                check.Recommendation = "ネットワーク設定をリセットしてください";
            }
            
            return check;
        }
        
        private async Task<DiagnosticCheck> CheckDnsResolutionAsync()
        {
            var check = new DiagnosticCheck
            {
                CheckName = "DnsResolution",
                Description = "DNS名前解決の確認"
            };
            
            try
            {
                var testDomains = new[] { "google.com", "microsoft.com", "cloudflare.com" };
                var successCount = 0;
                var totalTime = 0L;
                var sw = Stopwatch.StartNew();
                
                foreach (var domain in testDomains)
                {
                    try
                    {
                        sw.Restart();
                        var addresses = await System.Net.Dns.GetHostAddressesAsync(domain);
                        totalTime += sw.ElapsedMilliseconds;
                        
                        if (addresses.Length > 0)
                        {
                            successCount++;
                        }
                    }
                    catch
                    {
                        // 個別のDNS解決エラーは無視
                    }
                }
                
                if (successCount == 0)
                {
                    check.Status = DiagnosticStatus.Critical;
                    check.Issue = "DNS名前解決が全く機能していません";
                    check.Recommendation = "DNSサーバーを8.8.8.8や1.1.1.1に変更してください";
                }
                else if (successCount < testDomains.Length)
                {
                    check.Status = DiagnosticStatus.Warning;
                    check.Issue = $"一部のドメインが解決できません ({successCount}/{testDomains.Length})";
                    check.Recommendation = "代替DNSサーバーの設定を検討してください";
                }
                else
                {
                    var avgTime = totalTime / successCount;
                    check.Status = DiagnosticStatus.Healthy;
                    check.Details = $"平均DNS解決時間: {avgTime}ms";
                    
                    if (avgTime > 500)
                    {
                        check.Status = DiagnosticStatus.Warning;
                        check.Issue = "DNS解決が遅い状態です";
                        check.Recommendation = "高速DNSサーバー（8.8.8.8、1.1.1.1）の使用を検討してください";
                    }
                }
            }
            catch (Exception ex)
            {
                check.Status = DiagnosticStatus.Error;
                check.Issue = $"DNS確認エラー: {ex.Message}";
                check.Recommendation = "ネットワーク設定をリセットしてください";
            }
            
            return check;
        }
        
        private async Task<DiagnosticCheck> CheckSignalStrengthAsync(string ssid)
        {
            var check = new DiagnosticCheck
            {
                CheckName = "SignalStrength",
                Description = "WiFi信号強度の確認"
            };
            
            try
            {
                if (string.IsNullOrEmpty(ssid))
                {
                    check.Status = DiagnosticStatus.Warning;
                    check.Issue = "接続中のネットワークが特定できません";
                    check.Recommendation = "WiFi接続を確認してください";
                    return check;
                }
                
                var signalStrength = NetworkUtils.GetSignalStrength(ssid);
                
                if (signalStrength == 0)
                {
                    check.Status = DiagnosticStatus.Critical;
                    check.Issue = "信号強度を測定できません";
                    check.Recommendation = "WiFiアダプターまたはネットワーク設定を確認してください";
                }
                else if (signalStrength < 30)
                {
                    check.Status = DiagnosticStatus.Critical;
                    check.Issue = $"信号強度が非常に弱い状態です ({signalStrength}%)";
                    check.Recommendation = "ルーターに近づくか、WiFi中継器の導入を検討してください";
                }
                else if (signalStrength < 60)
                {
                    check.Status = DiagnosticStatus.Warning;
                    check.Issue = $"信号強度が弱い状態です ({signalStrength}%)";
                    check.Recommendation = "可能であればルーターに近い場所に移動してください";
                }
                else
                {
                    check.Status = DiagnosticStatus.Healthy;
                    check.Details = $"信号強度: {signalStrength}% - 良好な状態です";
                }
            }
            catch (Exception ex)
            {
                check.Status = DiagnosticStatus.Error;
                check.Issue = $"信号強度確認エラー: {ex.Message}";
                check.Recommendation = "WiFiアダプターの状態を確認してください";
            }
            
            return check;
        }
        
        private async Task<DiagnosticCheck> CheckNetworkProfileAsync(string ssid)
        {
            var check = new DiagnosticCheck
            {
                CheckName = "NetworkProfile",
                Description = "WiFiプロファイルの確認"
            };
            
            try
            {
                if (string.IsNullOrEmpty(ssid))
                {
                    check.Status = DiagnosticStatus.Warning;
                    check.Issue = "確認対象のSSIDが指定されていません";
                    check.Recommendation = "有効なWiFiネットワークに接続してください";
                    return check;
                }
                
                var profiles = await NetworkUtils.GetSavedWifiNetworksAsync();
                var profile = profiles.FirstOrDefault(p => p.SSID.Equals(ssid, StringComparison.OrdinalIgnoreCase));
                
                if (profile == null)
                {
                    check.Status = DiagnosticStatus.Warning;
                    check.Issue = "ネットワークプロファイルが見つかりません";
                    check.Recommendation = "ネットワークを削除して再接続してください";
                }
                else
                {
                    check.Status = DiagnosticStatus.Healthy;
                    check.Details = $"プロファイル確認済み: {profile.SSID} ({profile.AuthenticationType})";
                    
                    // プロファイルが古い場合は警告
                    if (DateTime.Now - profile.DateLastConnected > TimeSpan.FromDays(30))
                    {
                        check.Status = DiagnosticStatus.Warning;
                        check.Issue = "プロファイルが古い可能性があります";
                        check.Recommendation = "プロファイルを削除して再度接続してください";
                    }
                }
            }
            catch (Exception ex)
            {
                check.Status = DiagnosticStatus.Error;
                check.Issue = $"プロファイル確認エラー: {ex.Message}";
                check.Recommendation = "WiFiプロファイルをリセットしてください";
            }
            
            return check;
        }
        
        private async Task<DiagnosticCheck> CheckIpConfigurationAsync()
        {
            var check = new DiagnosticCheck
            {
                CheckName = "IpConfiguration",
                Description = "IP設定の確認"
            };
            
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                var wifiInterface = interfaces.FirstOrDefault(i => 
                    i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                    i.OperationalStatus == OperationalStatus.Up);
                
                if (wifiInterface == null)
                {
                    check.Status = DiagnosticStatus.Critical;
                    check.Issue = "アクティブなWiFi接続が見つかりません";
                    check.Recommendation = "WiFi接続を確認してください";
                    return check;
                }
                
                var properties = wifiInterface.GetIPProperties();
                var ipv4Properties = properties.GetIPv4Properties();
                
                if (properties.UnicastAddresses.Count == 0)
                {
                    check.Status = DiagnosticStatus.Critical;
                    check.Issue = "IPアドレスが割り当てられていません";
                    check.Recommendation = "「ipconfig /release」「ipconfig /renew」を実行してください";
                }
                else
                {
                    var ipAddress = properties.UnicastAddresses
                        .FirstOrDefault(addr => addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?
                        .Address.ToString();
                    
                    if (string.IsNullOrEmpty(ipAddress))
                    {
                        check.Status = DiagnosticStatus.Warning;
                        check.Issue = "IPv4アドレスが見つかりません";
                        check.Recommendation = "IPv4設定を確認してください";
                    }
                    else if (ipAddress.StartsWith("169.254"))
                    {
                        check.Status = DiagnosticStatus.Critical;
                        check.Issue = "APIPA（自動プライベートIP）が設定されています";
                        check.Recommendation = "DHCPサーバーとの接続を確認し、IPアドレスを更新してください";
                    }
                    else
                    {
                        check.Status = DiagnosticStatus.Healthy;
                        check.Details = $"IPアドレス: {ipAddress}";
                    }
                }
                
                if (properties.DnsAddresses.Count == 0)
                {
                    check.Status = Math.Max(check.Status, DiagnosticStatus.Warning);
                    check.Issue += (string.IsNullOrEmpty(check.Issue) ? "" : "; ") + "DNSサーバーが設定されていません";
                    check.Recommendation += (string.IsNullOrEmpty(check.Recommendation) ? "" : "; ") + "DNSサーバー設定を確認してください";
                }
            }
            catch (Exception ex)
            {
                check.Status = DiagnosticStatus.Error;
                check.Issue = $"IP設定確認エラー: {ex.Message}";
                check.Recommendation = "ネットワーク設定をリセットしてください";
            }
            
            return check;
        }
        
        private DiagnosticSeverity DetermineSeverity(List<DiagnosticCheck> checks)
        {
            if (checks.Any(c => c.Status == DiagnosticStatus.Critical))
                return DiagnosticSeverity.Critical;
            if (checks.Any(c => c.Status == DiagnosticStatus.Error))
                return DiagnosticSeverity.High;
            if (checks.Any(c => c.Status == DiagnosticStatus.Warning))
                return DiagnosticSeverity.Medium;
            return DiagnosticSeverity.Low;
        }
        
        private ConnectionIssue? IdentifyPrimaryIssue(List<DiagnosticCheck> checks, Exception? triggerException)
        {
            var criticalChecks = checks.Where(c => c.Status == DiagnosticStatus.Critical).ToList();
            if (criticalChecks.Any())
            {
                var check = criticalChecks.First();
                return new ConnectionIssue
                {
                    IssueType = DetermineIssueType(check.CheckName, triggerException),
                    Description = check.Issue,
                    CheckName = check.CheckName
                };
            }
            
            var errorChecks = checks.Where(c => c.Status == DiagnosticStatus.Error).ToList();
            if (errorChecks.Any())
            {
                var check = errorChecks.First();
                return new ConnectionIssue
                {
                    IssueType = IssueType.SystemError,
                    Description = check.Issue,
                    CheckName = check.CheckName
                };
            }
            
            return null;
        }
        
        private IssueType DetermineIssueType(string checkName, Exception? exception)
        {
            return checkName switch
            {
                "NetworkAdapter" => IssueType.AdapterIssue,
                "WifiService" => IssueType.ServiceIssue,
                "NetworkConnectivity" => IssueType.ConnectivityIssue,
                "DnsResolution" => IssueType.DnsIssue,
                "SignalStrength" => IssueType.SignalIssue,
                "NetworkProfile" => IssueType.ProfileIssue,
                "IpConfiguration" => IssueType.IpConfigurationIssue,
                _ => exception != null ? 
                    ErrorHandler.CategorizeError(exception) switch
                    {
                        ErrorCategory.Network => IssueType.ConnectivityIssue,
                        ErrorCategory.Security => IssueType.AuthenticationIssue,
                        ErrorCategory.Timeout => IssueType.ConnectivityIssue,
                        _ => IssueType.Unknown
                    } : IssueType.Unknown
            };
        }
        
        private List<string> GenerateRecommendations(List<DiagnosticCheck> checks, ConnectionIssue? primaryIssue)
        {
            var recommendations = new List<string>();
            
            foreach (var check in checks.Where(c => !string.IsNullOrEmpty(c.Recommendation)))
            {
                recommendations.Add($"{check.Description}: {check.Recommendation}");
            }
            
            if (primaryIssue?.IssueType == IssueType.ConnectivityIssue)
            {
                recommendations.Add("モデム/ルーターの電源を入れ直してください");
                recommendations.Add("他のデバイスでインターネット接続を確認してください");
            }
            
            if (!recommendations.Any())
            {
                recommendations.Add("WiFi接続を無効にして再度有効にしてください");
                recommendations.Add("システムを再起動してください");
            }
            
            return recommendations.Distinct().ToList();
        }
        
        private void StoreRecentDiagnosis(DiagnosticResult result)
        {
            var key = $"{result.SSID}_{result.Timestamp:yyyyMMddHHmm}";
            _recentDiagnoses[key] = result;
            
            if (_recentDiagnoses.Count > MaxDiagnosticHistory)
            {
                var oldestKey = _recentDiagnoses.Keys.OrderBy(k => k).First();
                _recentDiagnoses.Remove(oldestKey);
            }
        }
        
        private async void PerformPeriodicDiagnostic(object? state)
        {
            if (_disposed) return;
            
            try
            {
                var currentSSID = await FastWifiConnector.GetCurrentConnectedSSIDAsync();
                if (!string.IsNullOrEmpty(currentSSID))
                {
                    var result = await DiagnoseConnectionIssueAsync(null, currentSSID);
                    
                    if (result.OverallSeverity >= DiagnosticSeverity.Medium)
                    {
                        _logger.Log(ConnectionLogger.LogLevel.Warning, "PeriodicDiagnostic",
                            $"定期診断で問題を検出: {result.PrimaryIssue?.Description}");
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("ConnectionDiagnostics.PerformPeriodicDiagnostic", ex, _logger);
            }
        }
        
        public List<DiagnosticResult> GetRecentDiagnoses(int count = 10)
        {
            return _recentDiagnoses.Values
                .OrderByDescending(d => d.Timestamp)
                .Take(count)
                .ToList();
        }
        
        public DiagnosticSummary GetDiagnosticSummary()
        {
            var recentResults = GetRecentDiagnoses(20);
            
            return new DiagnosticSummary
            {
                TotalDiagnoses = recentResults.Count,
                CriticalIssues = recentResults.Count(d => d.OverallSeverity == DiagnosticSeverity.Critical),
                CommonIssues = recentResults
                    .Where(d => d.PrimaryIssue != null)
                    .GroupBy(d => d.PrimaryIssue!.IssueType)
                    .OrderByDescending(g => g.Count())
                    .Take(5)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count()),
                LastDiagnosis = recentResults.FirstOrDefault()?.Timestamp ?? DateTime.MinValue
            };
        }
        
        private void OnDiagnosticCompleted(DiagnosticEventArgs e) => DiagnosticCompleted?.Invoke(this, e);
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _diagnosticTimer?.Dispose();
            _recentDiagnoses.Clear();
        }
    }
    
    #region Data Classes
    
    public class DiagnosticResult
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string SSID { get; set; } = "";
        public List<DiagnosticCheck> Checks { get; set; } = new();
        public DiagnosticSeverity OverallSeverity { get; set; } = DiagnosticSeverity.Low;
        public ConnectionIssue? PrimaryIssue { get; set; }
        public List<string> RecommendedActions { get; set; } = new();
        public bool HasDiagnosticError { get; set; } = false;
        public string? DiagnosticError { get; set; }
        public Exception? TriggerException { get; set; }
    }
    
    public class DiagnosticCheck
    {
        public string CheckName { get; set; } = "";
        public string Description { get; set; } = "";
        public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Healthy;
        public string? Issue { get; set; }
        public string? Recommendation { get; set; }
        public string? Details { get; set; }
    }
    
    public class ConnectionIssue
    {
        public IssueType IssueType { get; set; } = IssueType.Unknown;
        public string Description { get; set; } = "";
        public string CheckName { get; set; } = "";
    }
    
    public class DiagnosticSummary
    {
        public int TotalDiagnoses { get; set; }
        public int CriticalIssues { get; set; }
        public Dictionary<string, int> CommonIssues { get; set; } = new();
        public DateTime LastDiagnosis { get; set; }
    }
    
    public class DiagnosticEventArgs : EventArgs
    {
        public DiagnosticResult Result { get; set; } = new();
    }
    
    public enum DiagnosticStatus
    {
        Healthy,
        Warning,
        Critical,
        Error
    }
    
    public enum DiagnosticSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
    
    public enum IssueType
    {
        Unknown,
        AdapterIssue,
        ServiceIssue,
        ConnectivityIssue,
        DnsIssue,
        SignalIssue,
        ProfileIssue,
        IpConfigurationIssue,
        AuthenticationIssue,
        SystemError
    }
    
    #endregion
}