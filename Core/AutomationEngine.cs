using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    public static class AutomationEngine
    {
        private static readonly string AutomationPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter", "Automation");

        private static readonly Dictionary<string, AutomationRule> _activeRules = new();
        private static readonly Timer _automationTimer;
        private static int _scheduledExecutionActive = 0;
        private static readonly SemaphoreSlim _executionSemaphore = new(1, 1);

        static AutomationEngine()
        {
            Directory.CreateDirectory(AutomationPath);
            _automationTimer = new Timer(ExecuteScheduledTasks, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            LoadAutomationRules();
        }

        public static async Task<string> CreateAutomationRule(string name, AutomationTrigger trigger, List<AutomationAction> actions)
        {
            var rule = new AutomationRule
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Trigger = trigger,
                Actions = actions,
                CreatedAt = DateTime.Now,
                IsEnabled = true,
                ExecutionCount = 0
            };

            _activeRules[rule.Id] = rule;
            await SaveAutomationRule(rule);

            return rule.Id;
        }

        public static async Task<bool> EnableRule(string ruleId, bool enabled = true)
        {
            if (_activeRules.TryGetValue(ruleId, out var rule))
            {
                rule.IsEnabled = enabled;
                await SaveAutomationRule(rule);
                return true;
            }
            return false;
        }

        public static async Task<bool> DeleteRule(string ruleId)
        {
            if (_activeRules.TryGetValue(ruleId, out var rule))
            {
                _activeRules.Remove(ruleId);
                var filePath = Path.Combine(AutomationPath, $"{ruleId}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                return true;
            }
            return false;
        }

        public static List<AutomationRule> GetActiveRules()
        {
            return _activeRules.Values.ToList();
        }

        public static async Task<AutomationExecutionResult> ExecuteRule(string ruleId)
        {
            if (!_activeRules.TryGetValue(ruleId, out var rule))
            {
                return new AutomationExecutionResult
                {
                    RuleId = ruleId,
                    Success = false,
                    Error = "Rule not found"
                };
            }

            return await ExecuteRuleInternal(rule);
        }

        public static async Task<List<AutomationExecutionResult>> CheckAndExecuteTriggers()
        {
            var results = new List<AutomationExecutionResult>();

            foreach (var rule in _activeRules.Values.Where(r => r.IsEnabled))
            {
                try
                {
                    if (await ShouldExecuteRule(rule))
                    {
                        var result = await ExecuteRuleInternal(rule);
                        results.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    await ErrorHandler.LogError(ex, $"Error checking trigger for rule {rule.Name}");
                }
            }

            return results;
        }

        public static async Task<List<AutomationExecutionResult>> ExecuteAllEnabledRules()
        {
            var targets = _activeRules.Values
                .Where(r => r.IsEnabled)
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var results = new List<AutomationExecutionResult>();

            foreach (var rule in targets)
            {
                try
                {
                    var result = await ExecuteRuleInternal(rule);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    await ErrorHandler.LogError(ex, $"Error executing automation rule {rule.Name}");
                    results.Add(new AutomationExecutionResult
                    {
                        RuleId = rule.Id,
                        RuleName = rule.Name,
                        Success = false,
                        Error = ex.Message
                    });
                }
            }

            return results;
        }

        public static async Task<string> CreateSmartConnectionRule()
        {
            var actions = new List<AutomationAction>
            {
                new() { Type = "ScanNetworks", Description = "Scan for available networks" },
                new() { Type = "CheckSignalStrength", Parameter1 = "50", Description = "Check if signal > 50%" },
                new() { Type = "ConnectToBest", Description = "Connect to highest priority network with good signal" },
                new() { Type = "LogResult", Description = "Log connection result" }
            };

            var trigger = new AutomationTrigger
            {
                Type = TriggerType.NetworkDisconnected,
                Description = "When network disconnects"
            };

            return await CreateAutomationRule("Smart Auto-Connect", trigger, actions);
        }

        public static async Task<string> CreatePerformanceMonitoringRule()
        {
            var actions = new List<AutomationAction>
            {
                new() { Type = "CheckLatency", Parameter1 = "100", Description = "Check if latency > 100ms" },
                new() { Type = "CheckSignalStrength", Parameter1 = "40", Description = "Check if signal < 40%" },
                new() { Type = "RecommendOptimization", Description = "Suggest performance improvements" },
                new() { Type = "LogPerformanceIssues", Description = "Log any performance issues found" }
            };

            var trigger = new AutomationTrigger
            {
                Type = TriggerType.Scheduled,
                Schedule = "*/15 * * * *", // Every 15 minutes
                Description = "Performance check every 15 minutes"
            };

            return await CreateAutomationRule("Performance Monitoring", trigger, actions);
        }

        public static async Task<string> CreateSecurityAuditRule()
        {
            var actions = new List<AutomationAction>
            {
                new() { Type = "SecurityScan", Description = "Scan for security issues" },
                new() { Type = "CheckOpenNetworks", Description = "Alert on open network connections" },
                new() { Type = "ValidateEncryption", Description = "Verify encryption is enabled" },
                new() { Type = "GenerateSecurityReport", Description = "Create security audit report" }
            };

            var trigger = new AutomationTrigger
            {
                Type = TriggerType.Scheduled,
                Schedule = "0 2 * * *", // Daily at 2 AM
                Description = "Daily security audit"
            };

            return await CreateAutomationRule("Daily Security Audit", trigger, actions);
        }

        public static async Task<string> CreateMaintenanceRule()
        {
            var actions = new List<AutomationAction>
            {
                new() { Type = "CleanupLogs", Parameter1 = "7", Description = "Clean logs older than 7 days" },
                new() { Type = "OptimizeCache", Description = "Clear and optimize cache" },
                new() { Type = "CheckUpdates", Description = "Check for application updates" },
                new() { Type = "GenerateHealthReport", Description = "Generate system health report" }
            };

            var trigger = new AutomationTrigger
            {
                Type = TriggerType.Scheduled,
                Schedule = "0 1 * * 0", // Weekly on Sunday at 1 AM
                Description = "Weekly maintenance"
            };

            return await CreateAutomationRule("Weekly Maintenance", trigger, actions);
        }

        private static async Task<bool> ShouldExecuteRule(AutomationRule rule)
        {
            switch (rule.Trigger.Type)
            {
                case TriggerType.NetworkConnected:
                    var status = await NetworkOperations.GetStatusAsync();
                    return status.Status == "Connected" &&
                           rule.LastExecuted < DateTime.Now.AddMinutes(-5); // Cooldown period

                case TriggerType.NetworkDisconnected:
                    status = await NetworkOperations.GetStatusAsync();
                    return status.Status != "Connected" &&
                           rule.LastExecuted < DateTime.Now.AddMinutes(-2);

                case TriggerType.SignalWeak:
                    status = await NetworkOperations.GetStatusAsync();
                    var threshold = int.Parse(rule.Trigger.Parameter1 ?? "30");
                    return status.Status == "Connected" && status.Signal < threshold &&
                           rule.LastExecuted < DateTime.Now.AddMinutes(-10);

                case TriggerType.Scheduled:
                    return ShouldExecuteScheduledRule(rule);

                case TriggerType.Manual:
                    return false; // Manual triggers don't auto-execute

                default:
                    return false;
            }
        }

        private static bool ShouldExecuteScheduledRule(AutomationRule rule)
        {
            try
            {
                // Simple cron-like schedule parsing
                var schedule = rule.Trigger.Schedule;
                if (string.IsNullOrEmpty(schedule))
                    return false;

                var parts = schedule.Split(' ');
                if (parts.Length != 5)
                    return false;

                var now = DateTime.Now;
                var lastExecuted = rule.LastExecuted ?? DateTime.MinValue;

                // Check if we've already executed this minute
                if (lastExecuted.Year == now.Year &&
                    lastExecuted.Month == now.Month &&
                    lastExecuted.Day == now.Day &&
                    lastExecuted.Hour == now.Hour &&
                    lastExecuted.Minute == now.Minute)
                {
                    return false;
                }

                // Parse schedule parts: minute hour day month dayofweek
                var minute = parts[0];
                var hour = parts[1];
                var day = parts[2];
                var month = parts[3];
                var dayOfWeek = parts[4];

                return MatchesSchedulePart(minute, now.Minute) &&
                       MatchesSchedulePart(hour, now.Hour) &&
                       MatchesSchedulePart(day, now.Day) &&
                       MatchesSchedulePart(month, now.Month) &&
                       MatchesSchedulePart(dayOfWeek, (int)now.DayOfWeek);
            }
            catch
            {
                return false;
            }
        }

        private static bool MatchesSchedulePart(string pattern, int value)
        {
            if (pattern == "*") return true;

            if (pattern.StartsWith("*/"))
            {
                var interval = int.Parse(pattern.Substring(2));
                return value % interval == 0;
            }

            if (int.TryParse(pattern, out var exactValue))
            {
                return value == exactValue;
            }

            return false;
        }

        private static async Task<AutomationExecutionResult> ExecuteRuleInternal(AutomationRule rule)
        {
            await _executionSemaphore.WaitAsync();
            try
            {
                var result = new AutomationExecutionResult
                {
                    RuleId = rule.Id,
                    RuleName = rule.Name,
                    StartTime = DateTime.Now
                };

                var actionResults = new List<AutomationActionResult>();

                foreach (var action in rule.Actions)
                {
                    var actionResult = await ExecuteAction(action);
                    actionResults.Add(actionResult);

                    if (!actionResult.Success && rule.StopOnError)
                    {
                        result.Success = false;
                        result.Error = $"Action '{action.Type}' failed: {actionResult.Error}";
                        break;
                    }
                }

                result.ActionResults = actionResults;
                result.Success = actionResults.All(a => a.Success);
                result.EndTime = DateTime.Now;

                // Update rule execution info
                rule.LastExecuted = DateTime.Now;
                rule.ExecutionCount++;
                await SaveAutomationRule(rule);

                return result;
            }
            finally
            {
                _executionSemaphore.Release();
            }
        }

        private static async Task<AutomationActionResult> ExecuteAction(AutomationAction action)
        {
            var result = new AutomationActionResult
            {
                ActionType = action.Type,
                StartTime = DateTime.Now
            };

            try
            {
                switch (action.Type)
                {
                    case "ScanNetworks":
                        var networks = await NetworkOperations.ScanNetworksAsync();
                        result.Data = $"Found {networks.Count} networks";
                        result.Success = true;
                        break;

                    case "ConnectToBest":
                        var config = await ConfigManager.GetConfig();
                        var bestNetwork = await FindBestAvailableNetwork(config.PreferredNetworks);
                        if (bestNetwork != null)
                        {
                            var connected = await NetworkOperations.ConnectAsync(bestNetwork, null);
                            result.Success = connected;
                            result.Data = connected ? $"Connected to {bestNetwork}" : "Connection failed";
                        }
                        else
                        {
                            result.Success = false;
                            result.Data = "No suitable network found";
                        }
                        break;

                    case "CheckSignalStrength":
                        var status = await NetworkOperations.GetStatusAsync();
                        var threshold = int.Parse(action.Parameter1 ?? "50");
                        result.Success = status.Signal >= threshold;
                        result.Data = $"Signal: {status.Signal}%, Threshold: {threshold}%";
                        break;

                    case "CheckLatency":
                        var latencyThreshold = int.Parse(action.Parameter1 ?? "100");
                        using (var ping = new System.Net.NetworkInformation.Ping())
                        {
                            var reply = await ping.SendPingAsync("8.8.8.8", 5000);
                            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                            {
                                result.Success = reply.RoundtripTime <= latencyThreshold;
                                result.Data = $"Latency: {reply.RoundtripTime}ms, Threshold: {latencyThreshold}ms";
                            }
                            else
                            {
                                result.Success = false;
                                result.Data = "Ping failed";
                            }
                        }
                        break;

                    case "SecurityScan":
                        var securityAudit = await SecurityManager.PerformSecurityAudit();
                        result.Success = securityAudit.SecurityScore >= 70;
                        result.Data = $"Security Score: {securityAudit.SecurityScore:F1}%";
                        break;

                    case "GenerateHealthReport":
                        var healthReport = await EnterpriseFeatures.GenerateHealthReport();
                        result.Success = healthReport.OverallHealthScore >= 60;
                        result.Data = $"Health Score: {healthReport.OverallHealthScore:F1}%";
                        break;

                    case "CleanupLogs":
                        var days = int.Parse(action.Parameter1 ?? "7");
                        var cleaned = await CleanupOldLogs(days);
                        result.Success = true;
                        result.Data = $"Cleaned {cleaned} old log files";
                        break;

                    case "OptimizeCache":
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                        result.Success = true;
                        result.Data = "Cache optimized and garbage collection performed";
                        break;

                    case "LogResult":
                        await Logger.LogInfo(action.Description ?? "Automation rule executed", nameof(AutomationEngine), new Dictionary<string, object>
                        {
                            ["timestamp"] = DateTime.Now,
                            ["ruleAction"] = action.Type,
                            ["parameter1"] = action.Parameter1,
                            ["parameter2"] = action.Parameter2
                        });
                        result.Success = true;
                        result.Data = "Result logged";
                        break;

                    default:
                        result.Success = false;
                        result.Error = $"Unknown action type: {action.Type}";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                await ErrorHandler.LogError(ex, $"Automation action '{action.Type}' failed");
            }

            if (action.DelayAfterMs > 0)
            {
                await Task.Delay(action.DelayAfterMs);
            }

            result.EndTime = DateTime.Now;
            return result;
        }

        private static async Task<string> FindBestAvailableNetwork(Dictionary<string, int> preferredNetworks)
        {
            try
            {
                var availableNetworks = await NetworkOperations.ScanNetworksAsync();

                // Find preferred networks that are available
                var candidates = availableNetworks
                    .Where(n => preferredNetworks.ContainsKey(n.Ssid))
                    .OrderByDescending(n => preferredNetworks[n.Ssid])
                    .ThenByDescending(n => n.Signal)
                    .ToList();

                return candidates.FirstOrDefault()?.Ssid;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<int> CleanupOldLogs(int days)
        {
            try
            {
                var logsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MurtiWifiConnecter", "Logs");

                if (!Directory.Exists(logsPath))
                    return 0;

                var cutoff = DateTime.Now.AddDays(-days);
                var files = Directory.GetFiles(logsPath, "*.log")
                    .Where(f => File.GetCreationTime(f) < cutoff)
                    .ToList();

                foreach (var file in files)
                {
                    File.Delete(file);
                }

                return files.Count;
            }
            catch
            {
                return 0;
            }
        }

        private static void ExecuteScheduledTasks(object state)
        {
            if (Interlocked.Exchange(ref _scheduledExecutionActive, 1) == 1)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await CheckAndExecuteTriggers();
                }
                catch (Exception ex)
                {
                    await ErrorHandler.LogError(ex, "Scheduled automation execution failed");
                }
                finally
                {
                    Interlocked.Exchange(ref _scheduledExecutionActive, 0);
                }
            });
        }

        private static async Task SaveAutomationRule(AutomationRule rule)
        {
            try
            {
                var filePath = Path.Combine(AutomationPath, $"{rule.Id}.json");
                var json = JsonSerializer.Serialize(rule, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, $"Failed to save automation rule {rule.Name}");
            }
        }

        private static void LoadAutomationRules()
        {
            try
            {
                if (!Directory.Exists(AutomationPath))
                    return;

                var files = Directory.GetFiles(AutomationPath, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var rule = JsonSerializer.Deserialize<AutomationRule>(json, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });

                        if (rule != null)
                        {
                            _activeRules[rule.Id] = rule;
                        }
                    }
                    catch
                    {
                        // Skip corrupted rule files
                    }
                }
            }
            catch
            {
                // Ignore initialization errors
            }
        }

        // Data models
        public class AutomationRule
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public AutomationTrigger Trigger { get; set; }
            public List<AutomationAction> Actions { get; set; } = new();
            public bool IsEnabled { get; set; } = true;
            public bool StopOnError { get; set; } = false;
            public DateTime CreatedAt { get; set; }
            public DateTime? LastExecuted { get; set; }
            public int ExecutionCount { get; set; }
        }

        public class AutomationTrigger
        {
            public TriggerType Type { get; set; }
            public string Parameter1 { get; set; }
            public string Parameter2 { get; set; }
            public string Schedule { get; set; } // Cron-like schedule for scheduled triggers
            public string Description { get; set; }
        }

        public class AutomationAction
        {
            public string Type { get; set; }
            public string Parameter1 { get; set; }
            public string Parameter2 { get; set; }
            public string Description { get; set; }
            public int DelayAfterMs { get; set; } = 0;
        }

        public class AutomationExecutionResult
        {
            public bool Success { get; set; }
            public string RuleId { get; set; }
            public string RuleName { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public List<AutomationActionResult> ActionResults { get; set; } = new();
            public string Error { get; set; }
        }

        public class AutomationActionResult
        {
            public bool Success { get; set; }
            public string ActionType { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string Data { get; set; }
            public string Error { get; set; }
        }

        public enum TriggerType
        {
            Manual,
            NetworkConnected,
            NetworkDisconnected,
            SignalWeak,
            Scheduled
        }
    }
}