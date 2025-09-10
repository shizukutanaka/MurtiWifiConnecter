using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;
using MurtiWifiConnecter.Infrastructure.Security;
using MurtiWifiConnecter.Infrastructure.Performance;

namespace MurtiWifiConnecter.Testing.Mocks
{
    /// <summary>
    /// WiFiサービスのモック実装
    /// テスタブルな動作をシミュレート
    /// </summary>
    public class MockWifiService : IWifiService
    {
        private readonly List<WifiNetwork> _simulatedNetworks;
        private readonly Dictionary<string, bool> _connectionResults;
        private readonly Dictionary<string, TimeSpan> _connectionDelays;
        
        public string? CurrentlyConnectedSSID { get; set; }
        public List<string> ConnectionHistory { get; } = new();
        public List<string> ScanHistory { get; } = new();
        public int ScanCount { get; private set; }
        public int ConnectionAttempts { get; private set; }

        public MockWifiService()
        {
            _simulatedNetworks = new List<WifiNetwork>();
            _connectionResults = new Dictionary<string, bool>();
            _connectionDelays = new Dictionary<string, TimeSpan>();
            
            SetupDefaultNetworks();
        }

        private void SetupDefaultNetworks()
        {
            _simulatedNetworks.AddRange(new[]
            {
                new WifiNetwork { SSID = "TestNetwork1", SignalStrength = 85, SecurityType = "WPA2", IsConnected = false },
                new WifiNetwork { SSID = "TestNetwork2", SignalStrength = 65, SecurityType = "WPA2", IsConnected = false },
                new WifiNetwork { SSID = "OpenNetwork", SignalStrength = 45, SecurityType = "Open", IsConnected = false },
                new WifiNetwork { SSID = "WeakNetwork", SignalStrength = 25, SecurityType = "WPA2", IsConnected = false }
            });

            // デフォルトの接続結果設定
            _connectionResults["TestNetwork1"] = true;
            _connectionResults["TestNetwork2"] = true;
            _connectionResults["OpenNetwork"] = true;
            _connectionResults["WeakNetwork"] = false; // 弱いネットワークは接続失敗

            // デフォルトの遅延設定
            _connectionDelays["TestNetwork1"] = TimeSpan.FromMilliseconds(1000);
            _connectionDelays["TestNetwork2"] = TimeSpan.FromMilliseconds(1500);
            _connectionDelays["OpenNetwork"] = TimeSpan.FromMilliseconds(500);
            _connectionDelays["WeakNetwork"] = TimeSpan.FromMilliseconds(3000);
        }

        #region IWifiService Implementation

        public async Task<List<WifiNetwork>> ScanNetworksAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(200, cancellationToken); // スキャン遅延をシミュレート
            
            ScanCount++;
            ScanHistory.Add($"Scan at {DateTime.Now:HH:mm:ss}");
            
            // 現在接続中のネットワークを更新
            foreach (var network in _simulatedNetworks)
            {
                network.IsConnected = network.SSID == CurrentlyConnectedSSID;
            }
            
            return new List<WifiNetwork>(_simulatedNetworks);
        }

        public async Task<WifiConnectionResult> ConnectToNetworkAsync(string ssid, string password, bool saveProfile = false, CancellationToken cancellationToken = default)
        {
            ConnectionAttempts++;
            ConnectionHistory.Add($"Connect to {ssid} at {DateTime.Now:HH:mm:ss}");

            var network = _simulatedNetworks.FirstOrDefault(n => n.SSID == ssid);
            if (network == null)
            {
                return new WifiConnectionResult
                {
                    Success = false,
                    ErrorMessage = "Network not found"
                };
            }

            // 接続遅延をシミュレート
            var delay = _connectionDelays.GetValueOrDefault(ssid, TimeSpan.FromMilliseconds(1000));
            await Task.Delay(delay, cancellationToken);

            var success = _connectionResults.GetValueOrDefault(ssid, true);
            
            if (success)
            {
                // 他のネットワークを切断
                foreach (var net in _simulatedNetworks)
                {
                    net.IsConnected = false;
                }
                
                network.IsConnected = true;
                CurrentlyConnectedSSID = ssid;
            }

            return new WifiConnectionResult
            {
                Success = success,
                ConnectedSSID = success ? ssid : null,
                Message = success ? "Connection successful" : "Connection failed"
            };
        }

        public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(100, cancellationToken);
            
            if (CurrentlyConnectedSSID != null)
            {
                ConnectionHistory.Add($"Disconnect from {CurrentlyConnectedSSID} at {DateTime.Now:HH:mm:ss}");
                
                var network = _simulatedNetworks.FirstOrDefault(n => n.SSID == CurrentlyConnectedSSID);
                if (network != null)
                {
                    network.IsConnected = false;
                }
                
                CurrentlyConnectedSSID = null;
                return true;
            }
            
            return false;
        }

        public async Task<string?> GetCurrentConnectedSSIDAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken);
            return CurrentlyConnectedSSID;
        }

        public async Task<bool> ForgetNetworkAsync(string ssid, CancellationToken cancellationToken = default)
        {
            await Task.Delay(100, cancellationToken);
            
            var network = _simulatedNetworks.FirstOrDefault(n => n.SSID == ssid);
            if (network != null)
            {
                _simulatedNetworks.Remove(network);
                if (CurrentlyConnectedSSID == ssid)
                {
                    CurrentlyConnectedSSID = null;
                }
                return true;
            }
            
            return false;
        }

        #endregion

        #region Test Helper Methods

        /// <summary>
        /// テスト用ネットワークの追加
        /// </summary>
        public void AddTestNetwork(WifiNetwork network)
        {
            _simulatedNetworks.Add(network);
        }

        /// <summary>
        /// 接続結果の設定
        /// </summary>
        public void SetConnectionResult(string ssid, bool success)
        {
            _connectionResults[ssid] = success;
        }

        /// <summary>
        /// 接続遅延の設定
        /// </summary>
        public void SetConnectionDelay(string ssid, TimeSpan delay)
        {
            _connectionDelays[ssid] = delay;
        }

        /// <summary>
        /// ネットワークの信号強度変更
        /// </summary>
        public void UpdateSignalStrength(string ssid, int newStrength)
        {
            var network = _simulatedNetworks.FirstOrDefault(n => n.SSID == ssid);
            if (network != null)
            {
                network.SignalStrength = newStrength;
            }
        }

        /// <summary>
        /// モック状態のリセット
        /// </summary>
        public void Reset()
        {
            CurrentlyConnectedSSID = null;
            ConnectionHistory.Clear();
            ScanHistory.Clear();
            ScanCount = 0;
            ConnectionAttempts = 0;
            
            _simulatedNetworks.Clear();
            SetupDefaultNetworks();
        }

        #endregion
    }

    /// <summary>
    /// ロギングサービスのモック実装
    /// </summary>
    public class MockLoggingService : ILoggingService
    {
        public List<LogEntry> LogEntries { get; } = new();
        public int ConnectionLogCount { get; private set; }
        public int DisconnectionLogCount { get; private set; }
        public int ScanLogCount { get; private set; }

        public void LogConnection(string ssid, bool success, int signalStrength, string? errorMessage = null)
        {
            ConnectionLogCount++;
            LogEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Type = "Connection",
                SSID = ssid,
                Success = success,
                SignalStrength = signalStrength,
                Message = errorMessage
            });
        }

        public void LogDisconnection(string ssid, string reason)
        {
            DisconnectionLogCount++;
            LogEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Type = "Disconnection",
                SSID = ssid,
                Message = reason
            });
        }

        public void LogNetworkScan(int networksFound, long scanTimeMs, string? additionalInfo = null)
        {
            ScanLogCount++;
            LogEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Type = "Scan",
                NetworksFound = networksFound,
                ScanTimeMs = scanTimeMs,
                Message = additionalInfo
            });
        }

        public async Task<List<string>> GetRecentLogsAsync(int count = 100)
        {
            await Task.Delay(10); // 非同期動作をシミュレート
            
            return LogEntries
                .TakeLast(count)
                .Select(e => $"[{e.Timestamp:HH:mm:ss}] {e.Type}: {e.SSID} - {e.Message}")
                .ToList();
        }

        public void ClearLogs()
        {
            LogEntries.Clear();
            ConnectionLogCount = 0;
            DisconnectionLogCount = 0;
            ScanLogCount = 0;
        }
    }

    /// <summary>
    /// プロファイルサービスのモック実装
    /// </summary>
    public class MockProfileService : IProfileService
    {
        private readonly Dictionary<string, string> _savedProfiles = new();
        
        public int ProfilesSaved { get; private set; }
        public int ProfilesRetrieved { get; private set; }
        public int ProfilesRemoved { get; private set; }

        public void SaveProfile(string ssid, string password)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                throw new ArgumentException("SSID cannot be null or empty", nameof(ssid));
                
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be null or empty", nameof(password));

            _savedProfiles[ssid] = password;
            ProfilesSaved++;
        }

        public string? GetSavedPassword(string ssid)
        {
            ProfilesRetrieved++;
            return _savedProfiles.GetValueOrDefault(ssid);
        }

        public void RemoveProfile(string ssid)
        {
            if (_savedProfiles.Remove(ssid))
            {
                ProfilesRemoved++;
            }
        }

        public List<string> GetSavedProfiles()
        {
            return _savedProfiles.Keys.ToList();
        }

        public void ClearAllProfiles()
        {
            _savedProfiles.Clear();
        }

        public bool HasProfile(string ssid)
        {
            return _savedProfiles.ContainsKey(ssid);
        }
    }

    /// <summary>
    /// 統計サービスのモック実装
    /// </summary>
    public class MockStatisticsService : IStatisticsService
    {
        private readonly List<ConnectionAttempt> _connectionAttempts = new();
        private readonly List<NetworkScanRecord> _scanRecords = new();

        public void RecordConnectionAttempt(string ssid, bool success)
        {
            _connectionAttempts.Add(new ConnectionAttempt
            {
                SSID = ssid,
                Success = success,
                Timestamp = DateTime.UtcNow
            });
        }

        public void RecordNetworkScan(int networksFound, long scanTimeMs)
        {
            _scanRecords.Add(new NetworkScanRecord
            {
                NetworksFound = networksFound,
                ScanTimeMs = scanTimeMs,
                Timestamp = DateTime.UtcNow
            });
        }

        public ConnectionStatisticsSummary GetSummary()
        {
            var successful = _connectionAttempts.Count(a => a.Success);
            var total = _connectionAttempts.Count;

            return new ConnectionStatisticsSummary
            {
                TotalConnectionAttempts = total,
                SuccessfulConnections = successful,
                FailedConnections = total - successful,
                SuccessRate = total > 0 ? (double)successful / total * 100 : 0,
                TotalScans = _scanRecords.Count,
                AverageScanTime = _scanRecords.Count > 0 ? 
                    _scanRecords.Average(s => s.ScanTimeMs) : 0,
                LastConnectionAttempt = _connectionAttempts.LastOrDefault()?.Timestamp,
                LastScan = _scanRecords.LastOrDefault()?.Timestamp
            };
        }

        public List<ConnectionAttempt> GetConnectionHistory(int count = 50)
        {
            return _connectionAttempts.TakeLast(count).ToList();
        }

        public void ClearStatistics()
        {
            _connectionAttempts.Clear();
            _scanRecords.Clear();
        }
    }

    /// <summary>
    /// セキュリティサービスのモック実装
    /// </summary>
    public class MockSecurityService : ISecurityService
    {
        private readonly Dictionary<string, SecureString> _storedCredentials = new();
        private readonly List<SecurityAssessment> _assessments = new();
        private readonly List<string> _validationHistory = new();

        public bool IsMonitoring { get; private set; }
        public int CredentialStoreCount { get; private set; }
        public int CredentialRetrieveCount { get; private set; }
        public int SecurityAssessmentCount { get; private set; }

        public async Task<bool> ValidateAndStoreCredentialsAsync(string ssid, string password)
        {
            await Task.Delay(50); // 処理時間をシミュレート
            
            if (string.IsNullOrWhiteSpace(ssid) || string.IsNullOrWhiteSpace(password))
                return false;

            // 危険なネットワーク名のチェック
            var dangerousSSIDs = new[] { "FreeWiFi", "PublicNetwork", "HackerTrap" };
            if (dangerousSSIDs.Contains(ssid))
                return false;

            // SecureStringに変換
            var securePassword = new SecureString();
            foreach (char c in password)
            {
                securePassword.AppendChar(c);
            }
            securePassword.MakeReadOnly();

            _storedCredentials[ssid] = securePassword;
            CredentialStoreCount++;
            
            return true;
        }

        public async Task<SecureString?> GetSecureCredentialsAsync(string ssid)
        {
            await Task.Delay(25);
            
            CredentialRetrieveCount++;
            return _storedCredentials.GetValueOrDefault(ssid);
        }

        public NetworkSecurityAssessment AssessNetworkSecurity(string ssid, string? bssid = null, int? signalStrength = null)
        {
            SecurityAssessmentCount++;
            
            var assessment = new NetworkSecurityAssessment
            {
                SSID = ssid,
                BSSID = bssid ?? "",
                AssessmentTime = DateTime.UtcNow
            };

            // シミュレートされたセキュリティ評価
            if (ssid.Contains("Free") || ssid.Contains("Open"))
            {
                assessment.RiskLevel = NetworkRiskLevel.High;
                assessment.RiskFactors.Add("オープンネットワーク");
                assessment.SecurityRecommendations.Add("VPN使用を推奨");
            }
            else if (signalStrength > 90)
            {
                assessment.RiskLevel = NetworkRiskLevel.Medium;
                assessment.RiskFactors.Add("異常に強い信号");
                assessment.SecurityRecommendations.Add("ネットワークの正当性を確認");
            }
            else
            {
                assessment.RiskLevel = NetworkRiskLevel.Low;
                assessment.SecurityRecommendations.Add("標準的なセキュリティレベル");
            }

            _assessments.Add(new SecurityAssessment
            {
                SSID = ssid,
                RiskLevel = assessment.RiskLevel,
                Timestamp = DateTime.UtcNow
            });

            return assessment;
        }

        public bool ValidateNetworkOperation(string operation, string parameters)
        {
            _validationHistory.Add($"{operation}: {parameters}");
            
            // 危険なコマンドのチェック
            var dangerousCommands = new[] { "format", "delete", "rm -rf", "cmd.exe" };
            var input = $"{operation} {parameters}".ToLower();
            
            return !dangerousCommands.Any(cmd => input.Contains(cmd));
        }

        public SecurityStatusReport GenerateSecurityReport()
        {
            var highRiskAssessments = _assessments.Count(a => a.RiskLevel == NetworkRiskLevel.High);
            var totalAssessments = _assessments.Count;

            return new SecurityStatusReport
            {
                GeneratedAt = DateTime.UtcNow,
                OverallRiskLevel = highRiskAssessments > totalAssessments / 2 ? 
                    RiskLevel.High : RiskLevel.Low,
                SecurityMetrics = new SecurityMetrics
                {
                    TotalSecurityEvents = _assessments.Count,
                    SecurityViolationsTotal = highRiskAssessments,
                    MonitoringActive = IsMonitoring
                },
                Recommendations = new List<SecurityRecommendation>
                {
                    new SecurityRecommendation
                    {
                        Priority = RecommendationPriority.Medium,
                        Title = "定期的なセキュリティ評価",
                        Description = "ネットワークの安全性を定期的に確認してください"
                    }
                }
            };
        }

        public void StartSecurityMonitoring()
        {
            IsMonitoring = true;
        }

        public void StopSecurityMonitoring()
        {
            IsMonitoring = false;
        }

        public void Dispose()
        {
            foreach (var secureString in _storedCredentials.Values)
            {
                secureString?.Dispose();
            }
            _storedCredentials.Clear();
        }

        // テストヘルパーメソッド
        public void ClearData()
        {
            Dispose();
            _assessments.Clear();
            _validationHistory.Clear();
            CredentialStoreCount = 0;
            CredentialRetrieveCount = 0;
            SecurityAssessmentCount = 0;
        }
    }

    #region Supporting Classes

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = string.Empty;
        public string SSID { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int SignalStrength { get; set; }
        public int NetworksFound { get; set; }
        public long ScanTimeMs { get; set; }
        public string? Message { get; set; }
    }

    public class ConnectionAttempt
    {
        public string SSID { get; set; } = string.Empty;
        public bool Success { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class NetworkScanRecord
    {
        public int NetworksFound { get; set; }
        public long ScanTimeMs { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class SecurityAssessment
    {
        public string SSID { get; set; } = string.Empty;
        public NetworkRiskLevel RiskLevel { get; set; }
        public DateTime Timestamp { get; set; }
    }


    #endregion
}