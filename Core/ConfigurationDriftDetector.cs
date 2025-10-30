using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 構成ドリフト検知と自動復旧システム
    /// </summary>
    public class ConfigurationDriftDetector
    {
        private readonly Dictionary<string, ConfigurationSnapshot> _baselineSnapshots = new();
        private readonly List<DriftDetectionRule> _driftRules = new();
        private readonly object _lockObject = new();
        private DateTime _lastBaselineUpdate = DateTime.MinValue;
        private readonly TimeSpan _baselineUpdateInterval = TimeSpan.FromHours(1);

        public async Task<ConfigurationDriftReport> DetectDriftAsync()
        {
            var report = new ConfigurationDriftReport
            {
                Timestamp = DateTime.UtcNow,
                DetectedDrifts = new List<ConfigurationDrift>(),
                Recommendations = new List<string>()
            };

            lock (_lockObject)
            {
                // ベースラインを更新
                if (DateTime.UtcNow - _lastBaselineUpdate > _baselineUpdateInterval)
                {
                    UpdateBaselines();
                    _lastBaselineUpdate = DateTime.UtcNow;
                }

                // 現在の構成をチェック
                var currentSnapshots = await CaptureCurrentConfigurationAsync();

                foreach (var current in currentSnapshots)
                {
                    if (_baselineSnapshots.TryGetValue(current.Key, out var baseline))
                    {
                        var drift = DetectDriftBetweenSnapshots(baseline, current);
                        if (drift.HasDrift)
                        {
                            report.DetectedDrifts.Add(drift);

                            // ログ記録
                            Logger.LogWarning($"構成ドリフト検知: {drift.Component}", "ConfigurationDriftDetector",
                                new Dictionary<string, object>
                                {
                                    ["component"] = drift.Component,
                                    ["driftType"] = drift.DriftType.ToString(),
                                    ["severity"] = drift.Severity.ToString()
                                });
                        }
                    }
                    else
                    {
                        // 新しい構成要素をベースラインに追加
                        _baselineSnapshots[current.Key] = current;
                    }
                }

                // 自動復旧の試行
                if (report.DetectedDrifts.Any(d => d.Severity == DriftSeverity.Critical))
                {
                    await AttemptAutoRecoveryAsync(report.DetectedDrifts.Where(d => d.Severity == DriftSeverity.Critical));
                }
            }

            return report;
        }

        private void UpdateBaselines()
        {
            // ベースラインを最新の状態に更新
            var tasks = _baselineSnapshots.Keys.Select(key => CaptureConfigurationSnapshotAsync(key)).ToArray();
            Task.WhenAll(tasks).Wait();

            // ベースラインを更新
            foreach (var task in tasks.Where(t => t.IsCompletedSuccessfully))
            {
                var snapshot = task.Result;
                if (snapshot != null)
                {
                    _baselineSnapshots[snapshot.Key] = snapshot;
                }
            }
        }

        private async Task<Dictionary<string, ConfigurationSnapshot>> CaptureCurrentConfigurationAsync()
        {
            var snapshots = new Dictionary<string, ConfigurationSnapshot>();

            // ネットワーク構成のキャプチャ
            var networkConfig = await CaptureNetworkConfigurationAsync();
            snapshots["NetworkConfiguration"] = networkConfig;

            // セキュリティ構成のキャプチャ
            var securityConfig = await CaptureSecurityConfigurationAsync();
            snapshots["SecurityConfiguration"] = securityConfig;

            // システム構成のキャプチャ
            var systemConfig = await CaptureSystemConfigurationAsync();
            snapshots["SystemConfiguration"] = systemConfig;

            // アプリケーション構成のキャプチャ
            var appConfig = await CaptureApplicationConfigurationAsync();
            snapshots["ApplicationConfiguration"] = appConfig;

            return snapshots;
        }

        private async Task<ConfigurationSnapshot> CaptureNetworkConfigurationAsync()
        {
            var snapshot = new ConfigurationSnapshot
            {
                Component = "NetworkConfiguration",
                CapturedAt = DateTime.UtcNow,
                Checksum = string.Empty,
                ConfigurationData = new Dictionary<string, object>()
            };

            try
            {
                // ネットワークインターフェース情報を収集
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                snapshot.ConfigurationData["Interfaces"] = interfaces.Select(ni => new
                {
                    Name = ni.Name,
                    Type = ni.NetworkInterfaceType.ToString(),
                    Status = ni.OperationalStatus.ToString(),
                    Speed = ni.Speed,
                    MacAddress = ni.GetPhysicalAddress().ToString()
                }).ToList();

                // WiFiプロファイル情報を収集
                var profiles = await NetworkOperations.GetSavedProfilesAsync();
                snapshot.ConfigurationData["WifiProfiles"] = profiles;

                // ネットワーク設定を収集
                snapshot.ConfigurationData["NetworkSettings"] = new
                {
                    DnsServers = GetDnsServers(),
                    ProxySettings = GetProxySettings(),
                    FirewallStatus = GetFirewallStatus()
                };

                // チェックサムを計算
                snapshot.Checksum = ComputeConfigurationChecksum(snapshot.ConfigurationData);
            }
            catch (Exception ex)
            {
                await Logger.LogError(ex, "ネットワーク構成キャプチャ失敗", "ConfigurationDriftDetector");
            }

            return snapshot;
        }

        private async Task<ConfigurationSnapshot> CaptureSecurityConfigurationAsync()
        {
            var snapshot = new ConfigurationSnapshot
            {
                Component = "SecurityConfiguration",
                CapturedAt = DateTime.UtcNow,
                Checksum = string.Empty,
                ConfigurationData = new Dictionary<string, object>()
            };

            try
            {
                // セキュリティポリシーを収集
                var policy = await PolicyEngine.GetActivePolicyAsync();
                snapshot.ConfigurationData["ActivePolicy"] = new
                {
                    PolicyLevel = policy.PolicyLevel,
                    EnforcementMode = policy.EnforcementMode,
                    RateLimits = policy.RateLimits
                };

                // セキュリティマネージャーの状態を収集
                var rateLimitMetrics = SecurityManager.GetRateLimitMetrics();
                snapshot.ConfigurationData["RateLimitMetrics"] = new
                {
                    CommandRejections = rateLimitMetrics.CommandRejections,
                    GlobalRejections = rateLimitMetrics.GlobalRejections,
                    TrackedOperations = rateLimitMetrics.TrackedOperations
                };

                // チェックサムを計算
                snapshot.Checksum = ComputeConfigurationChecksum(snapshot.ConfigurationData);
            }
            catch (Exception ex)
            {
                await Logger.LogError(ex, "セキュリティ構成キャプチャ失敗", "ConfigurationDriftDetector");
            }

            return snapshot;
        }

        private async Task<ConfigurationSnapshot> CaptureSystemConfigurationAsync()
        {
            var snapshot = new ConfigurationSnapshot
            {
                Component = "SystemConfiguration",
                CapturedAt = DateTime.UtcNow,
                Checksum = string.Empty,
                ConfigurationData = new Dictionary<string, object>()
            };

            try
            {
                // システム情報を収集
                snapshot.ConfigurationData["SystemInfo"] = new
                {
                    OsVersion = Environment.OSVersion.ToString(),
                    MachineName = Environment.MachineName,
                    ProcessorCount = Environment.ProcessorCount,
                    SystemUptime = GetSystemUptime()
                };

                // メモリ情報を収集
                var memoryInfo = GetMemoryInformation();
                snapshot.ConfigurationData["MemoryInfo"] = memoryInfo;

                // ディスク情報を収集
                var diskInfo = GetDiskInformation();
                snapshot.ConfigurationData["DiskInfo"] = diskInfo;

                // チェックサムを計算
                snapshot.Checksum = ComputeConfigurationChecksum(snapshot.ConfigurationData);
            }
            catch (Exception ex)
            {
                await Logger.LogError(ex, "システム構成キャプチャ失敗", "ConfigurationDriftDetector");
            }

            return snapshot;
        }

        private async Task<ConfigurationSnapshot> CaptureApplicationConfigurationAsync()
        {
            var snapshot = new ConfigurationSnapshot
            {
                Component = "ApplicationConfiguration",
                CapturedAt = DateTime.UtcNow,
                Checksum = string.Empty,
                ConfigurationData = new Dictionary<string, object>()
            };

            try
            {
                // アプリケーション設定を収集
                var config = await ConfigManager.LoadConfig();
                snapshot.ConfigurationData["ConfigSettings"] = new
                {
                    LogLevel = config?.LogLevel,
                    VerboseOutput = config?.VerboseOutput,
                    RateLimitSettings = config?.RateLimitCommandMaxAttempts != null ? new
                    {
                        CommandMaxAttempts = config.RateLimitCommandMaxAttempts,
                        CommandWindowSeconds = config.RateLimitCommandWindowSeconds,
                        GlobalMaxAttempts = config.RateLimitGlobalMaxAttempts,
                        GlobalWindowSeconds = config.RateLimitGlobalWindowSeconds
                    } : null
                };

                // 実行中のプロセス情報を収集
                var processes = Process.GetProcesses().Where(p => p.ProcessName.Contains("MurtiWifiConnecter"));
                snapshot.ConfigurationData["RunningProcesses"] = processes.Select(p => new
                {
                    ProcessName = p.ProcessName,
                    Id = p.Id,
                    StartTime = p.StartTime,
                    MemoryUsage = p.WorkingSet64
                }).ToList();

                // チェックサムを計算
                snapshot.Checksum = ComputeConfigurationChecksum(snapshot.ConfigurationData);
            }
            catch (Exception ex)
            {
                await Logger.LogError(ex, "アプリケーション構成キャプチャ失敗", "ConfigurationDriftDetector");
            }

            return snapshot;
        }

        private ConfigurationDrift DetectDriftBetweenSnapshots(ConfigurationSnapshot baseline, ConfigurationSnapshot current)
        {
            var drift = new ConfigurationDrift
            {
                Component = current.Component,
                BaselineChecksum = baseline.Checksum,
                CurrentChecksum = current.Checksum,
                DetectedAt = DateTime.UtcNow,
                HasDrift = false
            };

            if (baseline.Checksum != current.Checksum)
            {
                drift.HasDrift = true;
                drift.DriftType = DetermineDriftType(baseline, current);
                drift.Severity = DetermineDriftSeverity(drift.DriftType);
                drift.Description = $"構成ドリフト検知: {drift.Component}";
                drift.Details = CompareConfigurationData(baseline.ConfigurationData, current.ConfigurationData);
            }

            return drift;
        }

        private DriftType DetermineDriftType(ConfigurationSnapshot baseline, ConfigurationSnapshot current)
        {
            // 簡単なドリフトタイプ判定ロジック
            if (baseline.ConfigurationData.Count != current.ConfigurationData.Count)
            {
                return DriftType.StructuralChange;
            }

            foreach (var key in baseline.ConfigurationData.Keys)
            {
                if (!current.ConfigurationData.ContainsKey(key))
                {
                    return DriftType.ElementRemoved;
                }
            }

            return DriftType.ValueChanged;
        }

        private DriftSeverity DetermineDriftSeverity(DriftType driftType)
        {
            return driftType switch
            {
                DriftType.SecurityViolation => DriftSeverity.Critical,
                DriftType.StructuralChange => DriftSeverity.High,
                DriftType.ElementRemoved => DriftSeverity.Medium,
                DriftType.ValueChanged => DriftSeverity.Low,
                _ => DriftSeverity.Medium
            };
        }

        private Dictionary<string, object> CompareConfigurationData(Dictionary<string, object> baseline, Dictionary<string, object> current)
        {
            var differences = new Dictionary<string, object>();

            foreach (var key in baseline.Keys.Union(current.Keys))
            {
                var baselineValue = baseline.ContainsKey(key) ? baseline[key] : null;
                var currentValue = current.ContainsKey(key) ? current[key] : null;

                if (!Equals(baselineValue, currentValue))
                {
                    differences[key] = new
                    {
                        Baseline = baselineValue,
                        Current = currentValue
                    };
                }
            }

            return differences;
        }

        private async Task AttemptAutoRecoveryAsync(IEnumerable<ConfigurationDrift> criticalDrifts)
        {
            foreach (var drift in criticalDrifts)
            {
                try
                {
                    switch (drift.Component)
                    {
                        case "SecurityConfiguration":
                            await RecoverSecurityConfigurationAsync(drift);
                            break;
                        case "NetworkConfiguration":
                            await RecoverNetworkConfigurationAsync(drift);
                            break;
                        case "SystemConfiguration":
                            await RecoverSystemConfigurationAsync(drift);
                            break;
                        default:
                            await Logger.LogWarning($"自動復旧をサポートしていないコンポーネント: {drift.Component}", "ConfigurationDriftDetector");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    await Logger.LogError(ex, $"自動復旧失敗: {drift.Component}", "ConfigurationDriftDetector");
                }
            }
        }

        private async Task RecoverSecurityConfigurationAsync(ConfigurationDrift drift)
        {
            // セキュリティ構成の復旧ロジック
            await Logger.LogInfo($"セキュリティ構成を復旧中: {drift.Component}", "ConfigurationDriftDetector");

            // ポリシーをリロード
            await PolicyEngine.InitializeAsync();

            // セキュリティマネージャーを再初期化
            await SecurityManager.InitializeAsync();

            await Logger.LogInfo($"セキュリティ構成復旧完了", "ConfigurationDriftDetector");
        }

        private async Task RecoverNetworkConfigurationAsync(ConfigurationDrift drift)
        {
            // ネットワーク構成の復旧ロジック
            await Logger.LogInfo($"ネットワーク構成を復旧中: {drift.Component}", "ConfigurationDriftDetector");

            // ネットワーク設定を検証・修正
            await NetworkOperations.ValidateNetworkConfigurationAsync();

            await Logger.LogInfo($"ネットワーク構成復旧完了", "ConfigurationDriftDetector");
        }

        private async Task RecoverSystemConfigurationAsync(ConfigurationDrift drift)
        {
            // システム構成の復旧ロジック
            await Logger.LogInfo($"システム構成を復旧中: {drift.Component}", "ConfigurationDriftDetector");

            // システムヘルスチェックを実行
            await PerformHealthCheckAsync();

            await Logger.LogInfo($"システム構成復旧完了", "ConfigurationDriftDetector");
        }

        private string ComputeConfigurationChecksum(Dictionary<string, object> configData)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(configData, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false
            });

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(hash);
        }

        // ヘルパーメソッド
        private List<string> GetDnsServers() => new();
        private object GetProxySettings() => new();
        private string GetFirewallStatus() => "Enabled";
        private TimeSpan GetSystemUptime() => TimeSpan.FromTicks(Environment.TickCount64);
        private object GetMemoryInformation() => new();
        private object GetDiskInformation() => new();
        private async Task<ConfigurationSnapshot> CaptureConfigurationSnapshotAsync(string component) => null;
    }

    public class ConfigurationSnapshot
    {
        public string Component { get; set; }
        public DateTime CapturedAt { get; set; }
        public string Checksum { get; set; }
        public Dictionary<string, object> ConfigurationData { get; set; } = new();
    }

    public class ConfigurationDrift
    {
        public string Component { get; set; }
        public string BaselineChecksum { get; set; }
        public string CurrentChecksum { get; set; }
        public DateTime DetectedAt { get; set; }
        public bool HasDrift { get; set; }
        public DriftType DriftType { get; set; }
        public DriftSeverity Severity { get; set; }
        public string Description { get; set; }
        public Dictionary<string, object> Details { get; set; }
    }

    public enum DriftType
    {
        ValueChanged,
        ElementRemoved,
        StructuralChange,
        SecurityViolation
    }

    public enum DriftSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class ConfigurationDriftReport
    {
        public DateTime Timestamp { get; set; }
        public List<ConfigurationDrift> DetectedDrifts { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    public class DriftDetectionRule
    {
        public string RuleName { get; set; }
        public string Component { get; set; }
        public DriftType DriftType { get; set; }
        public DriftSeverity Severity { get; set; }
        public Func<ConfigurationSnapshot, ConfigurationSnapshot, bool> DetectionLogic { get; set; }
        public Action<ConfigurationDrift> RecoveryAction { get; set; }
    }
}
