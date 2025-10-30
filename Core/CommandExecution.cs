using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Security;

namespace MurtiWifiConnecter.Core
{
    internal static class CommandExecution
    {
        private const string RateLimitEnvironmentVariable = "MURTIWIFICONNECTER_COMMAND_MIN_INTERVAL_MS";
        private const int RateLimitLowerBoundMs = 50;
        private const int RateLimitUpperBoundMs = 5000;
        private static readonly TimeSpan DefaultMinimumCommandInterval = TimeSpan.FromMilliseconds(300);

        private static readonly object RateLimitLock = new();
        private static DateTime _lastCommandTimestamp = DateTime.MinValue;
        private static readonly TimeSpan MinimumCommandInterval = ResolveMinimumCommandInterval();
        private static readonly SemaphoreSlim ExecutionLock = new(1, 1);
        private const int CommandHistoryCapacity = 50;
        private static readonly LinkedList<CommandHistoryEntry> CommandHistory = new();
        private static readonly object CommandHistoryLock = new();
        private static readonly Dictionary<string, int> CommandUsageCounts = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CommandAnomalyWindow = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan CommandAnomalySuppressionWindow = TimeSpan.FromMinutes(2);
        private const int CommandAnomalyCountThreshold = 25;
        private const int CommandFailureSpikeThreshold = 5;
        private const double CommandFailureRateThreshold = 0.6;
        private const double CommandDurationThresholdMs = 8000;
        private const int CommandDurationSampleThreshold = 3;
        private static readonly TimeSpan CommandAnomalyRetention = TimeSpan.FromMinutes(10);
        private static readonly object CommandAnomalyLock = new();
        private static readonly Dictionary<string, CommandAnomalyTracker> CommandAnomalyTrackers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object CommandTelemetryLock = new();
        private static readonly Dictionary<string, CommandTelemetryTracker> CommandTelemetryTrackers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CommandTelemetryFlushInterval = TimeSpan.FromSeconds(45);
        private const int CommandTelemetryFlushThreshold = 12;
        private const int TelemetryRetentionDays = 60;
        private const int TelemetryMaxArchives = 120;
        private const string TelemetryArchivePrefix = "command_metrics_";
        private const string TelemetryArchiveExtension = ".json";
        private const int MaxLoggedArgumentLength = 256;
        private const int MaxCommandHistoryArgumentLength = 512;
        private const string LoggedArgumentEllipsis = "...";
        private static int _telemetryPendingUpdates;
        private static DateTime _lastTelemetryFlushUtc = DateTime.MinValue;
        private static readonly string TelemetryDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter",
            "telemetry");
        private static readonly string TelemetryFilePath = Path.Combine(TelemetryDirectory, "command_metrics.json");

        public static TimeSpan DefaultCommandAnomalyWindow => CommandAnomalyWindow;
        public static TimeSpan CommandAnomalyRetentionWindow => CommandAnomalyRetention;
        public static string CommandTelemetrySnapshotPath => TelemetryFilePath;
        public static string CommandTelemetryDirectory => TelemetryDirectory;

        public static async Task<int> RunAsync(
            string invokedCommand,
            string canonicalCommand,
            string[] args,
            Func<string[], Task<int>> handler,
            int[]? sensitiveArgumentIndexes = null)
        {
            await ExecutionLock.WaitAsync();
            try
            {
                if (handler == null)
                {
                    throw new ArgumentNullException(nameof(handler));
                }

                if (string.IsNullOrWhiteSpace(canonicalCommand))
                {
                    throw new ArgumentException("Command name cannot be null or whitespace.", nameof(canonicalCommand));
                }

                invokedCommand = string.IsNullOrWhiteSpace(invokedCommand) ? canonicalCommand : invokedCommand;

                // Security-002: 権限境界監査 - 権限昇格検知と不正プロセス委任の遮断
                var privilegeCheckResult = await EvaluatePrivilegeBoundariesAsync(canonicalCommand, args);
                if (!privilegeCheckResult.IsAllowed)
                {
                    await Logger.LogSecurity("Privilege escalation attempt blocked", "PrivilegeBoundaryViolation", new Dictionary<string, object>
                    {
                        ["command"] = canonicalCommand,
                        ["invoked"] = invokedCommand,
                        ["reason"] = privilegeCheckResult.Reason,
                        ["requiredPrivileges"] = string.Join(", ", privilegeCheckResult.RequiredPrivileges),
                        ["currentPrivileges"] = string.Join(", ", privilegeCheckResult.CurrentPrivileges)
                    });

                    await AuditTrail.RecordEventAsync(
                        "Security",
                        "PrivilegeEscalationBlocked",
                        new Dictionary<string, object>
                        {
                            ["command"] = canonicalCommand,
                            ["invoked"] = invokedCommand,
                            ["reason"] = privilegeCheckResult.Reason,
                            ["requiredPrivileges"] = string.Join(", ", privilegeCheckResult.RequiredPrivileges),
                            ["currentPrivileges"] = string.Join(", ", privilegeCheckResult.CurrentPrivileges)
                        },
                        "Critical");

                    Console.WriteLine($"Command blocked: {privilegeCheckResult.Reason}");
                    return 1;
                }

                var timestamp = DateTime.Now;
                var sanitizedArgs = SensitiveDataHelper.RedactArguments(sensitiveArgumentIndexes, args);
                var logArgs = TransformArgumentsForLog(sanitizedArgs);
                var historyArgs = TransformArgumentsForHistory(sanitizedArgs);

                if (IsRateLimited(timestamp, canonicalCommand))
                {
                    await Logger.LogWarning("Command execution throttled", nameof(CommandProcessor), new Dictionary<string, object>
                    {
                        ["command"] = canonicalCommand,
                        ["invoked"] = invokedCommand,
                        ["timestamp"] = timestamp
                    });

                    Console.WriteLine("Command throttled: Please wait momentarily before retrying.");
                    return 1;
                }

                var rateLimitResult = await SecurityManager.CheckRateLimitAsync(canonicalCommand);
                if (!rateLimitResult.Allowed)
                {
                    var metadata = new Dictionary<string, object>
                    {
                        ["command"] = canonicalCommand,
                        ["invoked"] = invokedCommand,
                        ["timestamp"] = timestamp,
                        ["scope"] = rateLimitResult.Scope.ToString(),
                        ["globalViolations"] = rateLimitResult.GlobalViolations,
                        ["commandViolations"] = rateLimitResult.CommandViolations
                    };

                    await Logger.LogWarning("Command rejected by security rate limiter", nameof(CommandProcessor), metadata);

                    await AuditTrail.RecordEventAsync(
                        "Security",
                        rateLimitResult.Scope == RateLimitScope.Global ? "GlobalRateLimitViolation" : "CommandRateLimitViolation",
                        metadata,
                        "Warning");

                    var scopeLabel = rateLimitResult.Scope == RateLimitScope.Global ? "global" : "per-command";
                    Console.WriteLine($"Command rejected: {scopeLabel} rate limit exceeded. Please wait before retrying.");
                    return 1;
                }

                try
                {
                    CommandArgumentGuard.EnsureSafeArguments(args);
                }
                catch (ArgumentException ex)
                {
                    await Logger.LogWarning("Command execution blocked due to unsafe argument", nameof(CommandProcessor), new Dictionary<string, object>
                    {
                        ["command"] = canonicalCommand,
                        ["invoked"] = invokedCommand,
                        ["timestamp"] = timestamp,
                        ["reason"] = ex.Message
                    });

                    await AuditTrail.RecordEventAsync(
                        "Command",
                        "ArgumentRejected",
                        new Dictionary<string, object>
                        {
                            ["command"] = canonicalCommand,
                            ["invoked"] = invokedCommand,
                            ["timestamp"] = timestamp,
                            ["reason"] = ex.Message
                        },
                        "Warning");

                    Console.WriteLine($"Command blocked: {ex.Message}");
                    return 1;
                }

                await Logger.LogInfo("Command received", nameof(CommandProcessor), new Dictionary<string, object>
                {
                    ["command"] = canonicalCommand,
                    ["invoked"] = invokedCommand,
                    ["args"] = logArgs,
                    ["timestamp"] = timestamp
                });

                var stopwatch = Stopwatch.StartNew();

                try
                {
                    var result = await handler(args);
                    stopwatch.Stop();
                    var completedAt = DateTime.Now;

                    await Logger.LogInfo("Command completed", nameof(CommandProcessor), new Dictionary<string, object>
                    {
                        ["command"] = canonicalCommand,
                        ["invoked"] = invokedCommand,
                        ["result"] = result,
                        ["durationMs"] = stopwatch.ElapsedMilliseconds,
                        ["timestamp"] = completedAt
                    });

                    await AuditTrail.RecordEventAsync(
                        "Command",
                        "Execute",
                        new Dictionary<string, object>
                        {
                            ["command"] = canonicalCommand,
                            ["invoked"] = invokedCommand,
                            ["args"] = string.Join(' ', logArgs),
                            ["result"] = result,
                            ["durationMs"] = stopwatch.ElapsedMilliseconds,
                            ["completedAt"] = completedAt
                        },
                        result == 0 ? "Info" : "Warning");

                    RecordCommand(canonicalCommand, invokedCommand, historyArgs, result);
                    RecordCommandUsage(canonicalCommand);

                    await EvaluateCommandAnomaliesAsync(canonicalCommand, completedAt, result, stopwatch.ElapsedMilliseconds).ConfigureAwait(false);
                    await UpdateCommandTelemetryAsync(canonicalCommand, completedAt, result, stopwatch.ElapsedMilliseconds).ConfigureAwait(false);

                    return result;
                }
                catch (OperationCanceledException ex)
                {
                    stopwatch.Stop();
                    var completedAt = DateTime.Now;
                    await Logger.LogWarning("Command execution cancelled", nameof(CommandProcessor), new Dictionary<string, object>
                    {
                        ["command"] = canonicalCommand,
                        ["invoked"] = invokedCommand,
                        ["durationMs"] = stopwatch.ElapsedMilliseconds,
                        ["timestamp"] = completedAt
                    }, ex).ConfigureAwait(false);

                    await AuditTrail.RecordEventAsync(
                        "Command",
                        "Cancelled",
                        new Dictionary<string, object>
                        {
                            ["command"] = canonicalCommand,
                            ["invoked"] = invokedCommand,
                            ["durationMs"] = stopwatch.ElapsedMilliseconds,
                            ["completedAt"] = completedAt
                        },
                        "Warning").ConfigureAwait(false);

                    RecordCommand(canonicalCommand, invokedCommand, historyArgs, 1);
                    RecordCommandUsage(canonicalCommand);

                    return 1;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    var completedAt = DateTime.Now;
                    Console.WriteLine($"Error executing {canonicalCommand}: {ex.Message}");

                    await Logger.LogError("Command execution failed", nameof(CommandProcessor), new Dictionary<string, object>
                    {
                        ["command"] = canonicalCommand,
                        ["invoked"] = invokedCommand,
                        ["args"] = logArgs,
                        ["durationMs"] = stopwatch.ElapsedMilliseconds,
                        ["timestamp"] = completedAt
                    }, ex);

                    await AuditTrail.RecordEventAsync(
                        "Command",
                        "Error",
                        new Dictionary<string, object>
                        {
                            ["command"] = canonicalCommand,
                            ["invoked"] = invokedCommand,
                            ["args"] = string.Join(' ', logArgs),
                            ["message"] = ex.Message,
                            ["durationMs"] = stopwatch.ElapsedMilliseconds,
                            ["completedAt"] = completedAt
                        },
                        "Error");

                    RecordCommand(canonicalCommand, invokedCommand, historyArgs, 1);
                    RecordCommandUsage(canonicalCommand);

                    await EvaluateCommandAnomaliesAsync(canonicalCommand, completedAt, 1, stopwatch.ElapsedMilliseconds).ConfigureAwait(false);
                    await UpdateCommandTelemetryAsync(canonicalCommand, completedAt, 1, stopwatch.ElapsedMilliseconds).ConfigureAwait(false);

                    return 1;
                }
            }
            finally
            {
                ExecutionLock.Release();
            }
        }

        private static string[] TransformArgumentsForLog(string[] sanitizedArgs)
        {
            if (sanitizedArgs == null || sanitizedArgs.Length == 0)
            {
                return Array.Empty<string>();
            }

            var output = new string[sanitizedArgs.Length];
            for (var i = 0; i < sanitizedArgs.Length; i++)
            {
                output[i] = SanitizeLogValue(sanitizedArgs[i], MaxLoggedArgumentLength);
            }

            return output;
        }

        private static string[] TransformArgumentsForHistory(string[] sanitizedArgs)
        {
            if (sanitizedArgs == null || sanitizedArgs.Length == 0)
            {
                return Array.Empty<string>();
            }

            var output = new string[sanitizedArgs.Length];
            for (var i = 0; i < sanitizedArgs.Length; i++)
            {
                output[i] = SanitizeLogValue(sanitizedArgs[i], MaxCommandHistoryArgumentLength);
            }

            return output;
        }

        private static string SanitizeLogValue(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(Math.Min(trimmed.Length, maxLength));
            foreach (var ch in trimmed)
            {
                if (builder.Length >= maxLength)
                {
                    break;
                }

                builder.Append(char.IsControl(ch) ? ' ' : ch);
            }

            var sanitized = builder.ToString();
            if (trimmed.Length > maxLength)
            {
                sanitized += LoggedArgumentEllipsis;
            }

            return sanitized;
        }

        private static bool IsRateLimited(DateTime requestedAt, string command)
        {
            lock (RateLimitLock)
            {
                if (_lastCommandTimestamp == DateTime.MinValue)
                {
                    _lastCommandTimestamp = requestedAt;
                    return false;
                }

                var elapsed = requestedAt - _lastCommandTimestamp;
                if (elapsed < MinimumCommandInterval)
                {
                    return true;
                }

                _lastCommandTimestamp = requestedAt;
                return false;
            }
        }

        public static IReadOnlyList<(DateTime Timestamp, string CanonicalCommand, string InvokedCommand, string Arguments, int Result)> GetRecentCommands(int count)
        {
            if (count <= 0)
            {
                return Array.Empty<(DateTime, string, string, string, int)>();
            }

            lock (CommandHistoryLock)
            {
                if (CommandHistory.Count == 0)
                {
                    return Array.Empty<(DateTime, string, string, string, int)>();
                }

                var actualCount = Math.Min(count, CommandHistory.Count);
                var output = new List<(DateTime, string, string, string, int)>(actualCount);

                var node = CommandHistory.Last;
                while (node != null && output.Count < actualCount)
                {
                    var entry = node.Value;
                    output.Add((entry.Timestamp, entry.CanonicalCommand, entry.InvokedCommand, entry.Arguments, entry.Result));
                    node = node.Previous;
                }

                return output;
            }
        }

        public static int ClearRecentCommands(int keepLatest = 0)
        {
            if (keepLatest < 0)
            {
                keepLatest = 0;
            }

            lock (CommandHistoryLock)
            {
                if (CommandHistory.Count == 0)
                {
                    return 0;
                }

                if (keepLatest == 0)
                {
                    var removedAll = CommandHistory.Count;
                    CommandHistory.Clear();
                    return removedAll;
                }

                if (CommandHistory.Count <= keepLatest)
                {
                    return 0;
                }

                var removed = 0;
                while (CommandHistory.Count > keepLatest)
                {
                    CommandHistory.RemoveFirst();
                    removed++;
                }

                return removed;
            }
        }

        public static IReadOnlyList<(string Command, int Count)> GetMostUsedCommands(int top = 5)
        {
            if (top <= 0)
            {
                return Array.Empty<(string, int)>();
            }

            lock (CommandHistoryLock)
            {
                if (CommandUsageCounts.Count == 0)
                {
                    return Array.Empty<(string, int)>();
                }

                return CommandUsageCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(top)
                    .Select(kvp => (kvp.Key, kvp.Value))
                    .ToArray();
            }
        }

        private static void RecordCommand(string canonicalCommand, string invokedCommand, IReadOnlyList<string> safeArgs, int result)
        {
            var arguments = string.Empty;
            if (safeArgs != null && safeArgs.Count > 1)
            {
                var argumentSlice = safeArgs.Skip(1).Where(arg => !string.IsNullOrWhiteSpace(arg));
                arguments = string.Join(' ', argumentSlice);
                arguments = SanitizeLogValue(arguments, MaxCommandHistoryArgumentLength);
            }

            var entry = new CommandHistoryEntry(DateTime.Now, canonicalCommand, invokedCommand, arguments, result);

            lock (CommandHistoryLock)
            {
                CommandHistory.AddLast(entry);
                if (CommandHistory.Count > CommandHistoryCapacity)
                {
                    CommandHistory.RemoveFirst();
                }
            }
        }

        private static void RecordCommandUsage(string canonicalCommand)
        {
            if (string.IsNullOrEmpty(canonicalCommand))
            {
                return;
            }

            lock (CommandHistoryLock)
            {
                if (!CommandUsageCounts.TryGetValue(canonicalCommand, out var count))
                {
                    CommandUsageCounts[canonicalCommand] = 1;
                }
                else
                {
                    CommandUsageCounts[canonicalCommand] = count + 1;
                }
            }
        }

        private readonly struct CommandHistoryEntry
        {
            public CommandHistoryEntry(DateTime timestamp, string canonicalCommand, string invokedCommand, string arguments, int result)
            {
                Timestamp = timestamp;
                CanonicalCommand = canonicalCommand;
                InvokedCommand = invokedCommand;
                Arguments = arguments;
                Result = result;
            }

            public DateTime Timestamp { get; }
            public string CanonicalCommand { get; }
            public string InvokedCommand { get; }
            public string Arguments { get; }
            public int Result { get; }
        }

        private static async Task EvaluateCommandAnomaliesAsync(string canonicalCommand, DateTime observedAt, int result, long durationMs)
        {
            if (string.IsNullOrWhiteSpace(canonicalCommand))
            {
                return;
            }

            CommandAnomalyAlert? alertToPublish = null;

            lock (CommandAnomalyLock)
            {
                if (!CommandAnomalyTrackers.TryGetValue(canonicalCommand, out var tracker))
                {
                    tracker = new CommandAnomalyTracker();
                    CommandAnomalyTrackers[canonicalCommand] = tracker;
                }

                tracker.Prune(observedAt - CommandAnomalyRetention);
                tracker.Add(new CommandAnomalyEntry(observedAt, result != 0, durationMs));

                var windowStats = tracker.ComputeWindowStats(CommandAnomalyWindow, observedAt);
                var attemptCount = windowStats.Attempts;
                var failureCount = windowStats.Failures;
                var failureRate = windowStats.Attempts == 0 ? 0 : (double)windowStats.Failures / windowStats.Attempts;
                var averageDuration = windowStats.AverageDurationMs;

                if (attemptCount >= CommandAnomalyCountThreshold && tracker.CanEmit(observedAt))
                {
                    tracker.MarkAlert(observedAt, "HighInvocationVolume");
                    alertToPublish = new CommandAnomalyAlert(canonicalCommand, attemptCount, failureCount, failureRate, averageDuration, "HighInvocationVolume");
                }
                else if (failureCount >= CommandFailureSpikeThreshold && failureRate >= CommandFailureRateThreshold && tracker.CanEmit(observedAt))
                {
                    tracker.MarkAlert(observedAt, "FailureSpike");
                    alertToPublish = new CommandAnomalyAlert(canonicalCommand, attemptCount, failureCount, failureRate, averageDuration, "FailureSpike");
                }
                else if (attemptCount >= CommandDurationSampleThreshold && averageDuration >= CommandDurationThresholdMs && tracker.CanEmit(observedAt))
                {
                    tracker.MarkAlert(observedAt, "SlowExecution");
                    alertToPublish = new CommandAnomalyAlert(canonicalCommand, attemptCount, failureCount, failureRate, averageDuration, "SlowExecution");
                }
            }

            if (!alertToPublish.HasValue)
            {
                return;
            }

            var alert = alertToPublish.Value;
            var metadata = new Dictionary<string, object>
            {
                ["command"] = alert.Command,
                ["windowSeconds"] = CommandAnomalyWindow.TotalSeconds,
                ["attempts"] = alert.Attempts,
                ["failures"] = alert.Failures,
                ["failureRate"] = Math.Round(alert.FailureRate, 3),
                ["averageDurationMs"] = Math.Round(alert.AverageDurationMs, 2),
                ["trigger"] = alert.Trigger,
                ["timestamp"] = observedAt
            };

            try
            {
                await Logger.LogSecurity("Command anomaly detected", alert.Trigger, metadata).ConfigureAwait(false);
                await AuditTrail.RecordEventAsync("Security", "CommandAnomalyDetected", metadata, "Warning").ConfigureAwait(false);
            }
            catch
            {
                // Swallow secondary errors to avoid cascading failures during anomaly reporting.
            }
        }

        public static IReadOnlyList<CommandAnomalySnapshot> GetCommandAnomalySnapshots(TimeSpan? window = null)
        {
            var effectiveWindow = window.HasValue && window.Value > TimeSpan.Zero
                ? window.Value
                : CommandAnomalyWindow;

            var cutoff = DateTime.Now - effectiveWindow;
            lock (CommandAnomalyLock)
            {
                if (CommandAnomalyTrackers.Count == 0)
                {
                    return Array.Empty<CommandAnomalySnapshot>();
                }

                var snapshots = new List<CommandAnomalySnapshot>(CommandAnomalyTrackers.Count);
                foreach (var kvp in CommandAnomalyTrackers)
                {
                    var tracker = kvp.Value;
                    tracker.Prune(cutoff);
                    if (tracker.AttemptCount == 0)
                    {
                        continue;
                    }

                    snapshots.Add(tracker.ToSnapshot(kvp.Key));
                }

                return snapshots
                    .OrderByDescending(s => s.AlertCount)
                    .ThenByDescending(s => s.FailureRate)
                    .ThenByDescending(s => s.AttemptCount)
                    .ThenBy(s => s.Command, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        public static int ResetCommandAnomalyMetrics(string command = null)
        {
            lock (CommandAnomalyLock)
            {
                if (string.IsNullOrWhiteSpace(command))
                {
                    var count = CommandAnomalyTrackers.Count;
                    CommandAnomalyTrackers.Clear();
                    return count;
                }

                return CommandAnomalyTrackers.Remove(command) ? 1 : 0;
            }
        }

        private sealed class CommandAnomalyTracker
        {
            private readonly Queue<CommandAnomalyEntry> _entries = new();
            private int _failureCount;
            private long _durationTotalMs;
            private DateTime _lastObserved;
            private int _alertCount;
            private string _lastAlertTrigger = string.Empty;

            public DateTime LastAlertUtc { get; private set; } = DateTime.MinValue;

            public int AttemptCount => _entries.Count;
            public int FailureCount => _failureCount;
            public double FailureRate => AttemptCount == 0 ? 0 : (double)_failureCount / AttemptCount;
            public double AverageDurationMs => AttemptCount == 0 ? 0 : (double)_durationTotalMs / AttemptCount;
            public DateTime LastObserved => _lastObserved;
            public int AlertCount => _alertCount;
            public string LastAlertTrigger => _lastAlertTrigger;

            public void Prune(DateTime cutoff)
            {
                while (_entries.Count > 0 && _entries.Peek().Timestamp < cutoff)
                {
                    var removed = _entries.Dequeue();
                    if (removed.Failed)
                    {
                        _failureCount--;
                    }

                    _durationTotalMs -= removed.DurationMs;
                    if (_durationTotalMs < 0)
                    {
                        _durationTotalMs = 0;
                    }
                }

                if (_entries.Count == 0)
                {
                    _lastObserved = DateTime.MinValue;
                }
            }

            public void Add(CommandAnomalyEntry entry)
            {
                _entries.Enqueue(entry);
                if (entry.Failed)
                {
                    _failureCount++;
                }

                _durationTotalMs += entry.DurationMs;
                _lastObserved = entry.Timestamp;
            }

            public bool CanEmit(DateTime observedAt)
            {
                return observedAt - LastAlertUtc >= CommandAnomalySuppressionWindow;
            }

            public void MarkAlert(DateTime observedAt, string trigger)
            {
                LastAlertUtc = observedAt;
                _alertCount++;
                _lastAlertTrigger = trigger;
            }

            public CommandAnomalySnapshot ToSnapshot(string command)
            {
                return new CommandAnomalySnapshot(
                    command,
                    AttemptCount,
                    FailureCount,
                    FailureRate,
                    AverageDurationMs,
                    LastObserved,
                    AlertCount,
                    LastAlertUtc,
                    LastAlertTrigger);
            }
        }

        private readonly struct CommandAnomalyEntry
        {
            public CommandAnomalyEntry(DateTime timestamp, bool failed, long durationMs)
            {
                Timestamp = timestamp;
                Failed = failed;
                DurationMs = durationMs;
            }

            public DateTime Timestamp { get; }
            public bool Failed { get; }
            public long DurationMs { get; }
        }

        private readonly struct CommandAnomalyAlert
        {
            public CommandAnomalyAlert(string command, int attempts, int failures, double failureRate, double averageDurationMs, string trigger)
            {
                Command = command;
                Attempts = attempts;
                Failures = failures;
                FailureRate = failureRate;
                AverageDurationMs = averageDurationMs;
                Trigger = trigger;
            }

            public string Command { get; }
            public int Attempts { get; }
            public int Failures { get; }
            public double FailureRate { get; }
            public double AverageDurationMs { get; }
            public string Trigger { get; }
        }

        public readonly struct CommandAnomalySnapshot
        {
            public CommandAnomalySnapshot(
                string command,
                int attempts,
                int failures,
                double failureRate,
                double averageDurationMs,
                DateTime lastObserved,
                int alertCount,
                DateTime lastAlertUtc,
                string lastTrigger)
            {
                Command = command;
                Attempts = attempts;
                Failures = failures;
                FailureRate = failureRate;
                AverageDurationMs = averageDurationMs;
                LastObserved = lastObserved;
                AlertCount = alertCount;
                LastAlertUtc = lastAlertUtc;
                LastTrigger = lastTrigger;
            }

            public string Command { get; }
            public int Attempts { get; }
            public int Failures { get; }
            public double FailureRate { get; }
            public double AverageDurationMs { get; }
            public DateTime LastObserved { get; }
            public int AlertCount { get; }
            public DateTime LastAlertUtc { get; }
            public string LastTrigger { get; }
        }

        public static IReadOnlyList<CommandTelemetrySnapshot> GetCommandTelemetrySnapshots()
        {
            lock (CommandTelemetryLock)
            {
                if (CommandTelemetryTrackers.Count == 0)
                {
                    return Array.Empty<CommandTelemetrySnapshot>();
                }

                return CommandTelemetryTrackers
                    .Values
                    .Select(tracker => tracker.ToSnapshot())
                    .OrderByDescending(snapshot => snapshot.TotalCount)
                    .ThenBy(snapshot => snapshot.Command, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        public static int ResetCommandTelemetry(string command = null)
        {
            lock (CommandTelemetryLock)
            {
                if (string.IsNullOrWhiteSpace(command))
                {
                    var count = CommandTelemetryTrackers.Count;
                    CommandTelemetryTrackers.Clear();
                    _telemetryPendingUpdates = 0;
                    _lastTelemetryFlushUtc = DateTime.MinValue;
                    return count;
                }

                if (CommandTelemetryTrackers.Remove(command))
                {
                    _telemetryPendingUpdates = 0;
                    _lastTelemetryFlushUtc = DateTime.MinValue;
                    return 1;
                }

                return 0;
            }
        }

        public static Task PersistCommandTelemetryAsync()
        {
            List<CommandTelemetrySnapshot> snapshot;
            lock (CommandTelemetryLock)
            {
                snapshot = CommandTelemetryTrackers
                    .Values
                    .Select(tracker => tracker.ToSnapshot())
                    .ToList();
                _telemetryPendingUpdates = 0;
                _lastTelemetryFlushUtc = DateTime.UtcNow;
            }

            return PersistTelemetrySnapshotAsync(snapshot);
        }

        private static async Task UpdateCommandTelemetryAsync(string canonicalCommand, DateTime observedAtLocal, int result, long durationMs)
        {
            if (string.IsNullOrWhiteSpace(canonicalCommand))
            {
                return;
            }

            var observedAtUtc = observedAtLocal.ToUniversalTime();
            bool shouldFlush = false;
            List<CommandTelemetrySnapshot> snapshotToPersist = null;

            lock (CommandTelemetryLock)
            {
                if (!CommandTelemetryTrackers.TryGetValue(canonicalCommand, out var tracker))
                {
                    tracker = new CommandTelemetryTracker(canonicalCommand);
                    CommandTelemetryTrackers[canonicalCommand] = tracker;
                }

                tracker.Record(observedAtUtc, result, durationMs);
                _telemetryPendingUpdates++;

                if (observedAtUtc - _lastTelemetryFlushUtc >= CommandTelemetryFlushInterval ||
                    _telemetryPendingUpdates >= CommandTelemetryFlushThreshold)
                {
                    shouldFlush = true;
                    _telemetryPendingUpdates = 0;
                    _lastTelemetryFlushUtc = observedAtUtc;
                    snapshotToPersist = CommandTelemetryTrackers
                        .Values
                        .Select(entry => entry.ToSnapshot())
                        .ToList();
                }
            }

            if (shouldFlush && snapshotToPersist != null)
            {
                await PersistTelemetrySnapshotAsync(snapshotToPersist).ConfigureAwait(false);
            }
        }

        private static async Task PersistTelemetrySnapshotAsync(IReadOnlyCollection<CommandTelemetrySnapshot> snapshot)
        {
            try
            {
                Directory.CreateDirectory(TelemetryDirectory);
                await SecurityManager.EnsureSecureDirectoryAclAsync(TelemetryDirectory).ConfigureAwait(false);

                await ArchiveExistingTelemetryAsync().ConfigureAwait(false);

                var ordered = snapshot
                    .OrderByDescending(entry => entry.TotalCount)
                    .ThenBy(entry => entry.Command, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var json = JsonSerializer.Serialize(ordered, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(TelemetryFilePath, json).ConfigureAwait(false);
                await SecurityManager.EnsureSecureFileAclAsync(TelemetryFilePath).ConfigureAwait(false);

                await PruneTelemetryArchivesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Logger.LogWarning("Failed to persist command telemetry", nameof(CommandExecution), new Dictionary<string, object>
                {
                    ["path"] = TelemetryFilePath,
                    ["error"] = ex.Message
                }).ConfigureAwait(false);
            }
        }

        private static async Task ArchiveExistingTelemetryAsync()
        {
            try
            {
                if (!File.Exists(TelemetryFilePath))
                {
                    return;
                }

                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var archiveName = $"{TelemetryArchivePrefix}{timestamp}{TelemetryArchiveExtension}";
                var archivePath = Path.Combine(TelemetryDirectory, archiveName);

                File.Copy(TelemetryFilePath, archivePath, overwrite: false);
                await SecurityManager.EnsureSecureFileAclAsync(archivePath).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Ignore duplicate timestamps; caller will continue with primary write.
            }
            catch (Exception ex)
            {
                await Logger.LogWarning("Failed to archive prior telemetry snapshot", nameof(CommandExecution), new Dictionary<string, object>
                {
                    ["path"] = TelemetryFilePath,
                    ["error"] = ex.Message
                }).ConfigureAwait(false);
            }
        }

        private static async Task PruneTelemetryArchivesAsync()
        {
            try
            {
                if (!Directory.Exists(TelemetryDirectory))
                {
                    return;
                }

                var archives = Directory.GetFiles(TelemetryDirectory, $"{TelemetryArchivePrefix}*{TelemetryArchiveExtension}", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(info => info.Exists)
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .ToList();

                if (archives.Count == 0)
                {
                    return;
                }

                if (TelemetryRetentionDays > 0)
                {
                    var cutoffUtc = DateTime.UtcNow.AddDays(-TelemetryRetentionDays);
                    foreach (var archive in archives.Where(a => a.LastWriteTimeUtc < cutoffUtc).ToList())
                    {
                        archives.Remove(archive);
                        await SecurityManager.SecureDeleteFileAsync(archive.FullName).ConfigureAwait(false);
                    }
                }

                for (int i = TelemetryMaxArchives; i < archives.Count; i++)
                {
                    await SecurityManager.SecureDeleteFileAsync(archives[i].FullName).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await Logger.LogWarning("Failed to prune telemetry archives", nameof(CommandExecution), new Dictionary<string, object>
                {
                    ["directory"] = TelemetryDirectory,
                    ["error"] = ex.Message
                }).ConfigureAwait(false);
            }
        }

        private sealed class CommandTelemetryTracker
        {
            private readonly string _command;
            private long _totalCount;
            private long _failureCount;
            private long _totalDurationMs;
            private long _maxDurationMs;
            private DateTime _firstObservedUtc = DateTime.MinValue;
            private DateTime _lastObservedUtc = DateTime.MinValue;
            private int _lastResult;
            private long _lastDurationMs;

            public CommandTelemetryTracker(string command)
            {
                _command = string.IsNullOrWhiteSpace(command) ? "(unknown)" : command;
            }

            public void Record(DateTime observedAtUtc, int result, long durationMs)
            {
                if (_firstObservedUtc == DateTime.MinValue)
                {
                    _firstObservedUtc = observedAtUtc;
                }

                _lastObservedUtc = observedAtUtc;

                _totalCount++;
                if (result != 0)
                {
                    _failureCount++;
                }

                var safeDuration = Math.Max(0, durationMs);
                _totalDurationMs += safeDuration;
                if (safeDuration > _maxDurationMs)
                {
                    _maxDurationMs = safeDuration;
                }

                _lastResult = result;
                _lastDurationMs = safeDuration;
            }

            public CommandTelemetrySnapshot ToSnapshot()
            {
                var failureRate = _totalCount == 0 ? 0 : (double)_failureCount / _totalCount;
                var averageDuration = _totalCount == 0 ? 0 : (double)_totalDurationMs / _totalCount;

                return new CommandTelemetrySnapshot(
                    _command,
                    _totalCount,
                    _failureCount,
                    failureRate,
                    averageDuration,
                    _maxDurationMs,
                    _firstObservedUtc,
                    _lastObservedUtc,
                    _lastResult,
                    _lastDurationMs);
            }
        }

        public readonly struct CommandTelemetrySnapshot
        {
            public CommandTelemetrySnapshot(
                string command,
                long totalCount,
                long failureCount,
                double failureRate,
                double averageDurationMs,
                long maxDurationMs,
                DateTime firstObservedUtc,
                DateTime lastObservedUtc,
                int lastResult,
                long lastDurationMs)
            {
                Command = command;
                TotalCount = totalCount;
                FailureCount = failureCount;
                FailureRate = failureRate;
                AverageDurationMs = averageDurationMs;
                MaxDurationMs = maxDurationMs;
                FirstObservedUtc = firstObservedUtc;
                LastObservedUtc = lastObservedUtc;
                LastResult = lastResult;
                LastDurationMs = lastDurationMs;
            }

            public string Command { get; }
            public long TotalCount { get; }
            public long FailureCount { get; }
            public double FailureRate { get; }
            public double AverageDurationMs { get; }
            public long MaxDurationMs { get; }
            public DateTime FirstObservedUtc { get; }
            public DateTime LastObservedUtc { get; }
            public int LastResult { get; }
            public long LastDurationMs { get; }
        }

        private static TimeSpan ResolveMinimumCommandInterval()
        {
            try
            {
                var rawValue = Environment.GetEnvironmentVariable(RateLimitEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return DefaultMinimumCommandInterval;
                }

                if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
                {
                    Console.WriteLine($"Warning: Invalid value for {RateLimitEnvironmentVariable}. Using default {DefaultMinimumCommandInterval.TotalMilliseconds}ms.");
                    return DefaultMinimumCommandInterval;
                }

                milliseconds = Math.Clamp(milliseconds, RateLimitLowerBoundMs, RateLimitUpperBoundMs);
                Console.WriteLine($"Using command rate limit of {milliseconds}ms.");
                return TimeSpan.FromMilliseconds(milliseconds);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to resolve command rate limit. {ex.Message}");
                return DefaultMinimumCommandInterval;
            }
        }

        /// <summary>
        /// Security-002: 権限境界監査 - 権限昇格検知と不正プロセス委任の遮断
        /// </summary>
        private static async Task<PrivilegeBoundaryCheckResult> EvaluatePrivilegeBoundariesAsync(string canonicalCommand, string[] args)
        {
            var result = new PrivilegeBoundaryCheckResult
            {
                IsAllowed = true,
                CurrentPrivileges = new List<string>(),
                RequiredPrivileges = new List<string>(),
                Reason = string.Empty
            };

            try
            {
                // 現在のプロセス権限を評価
                result.CurrentPrivileges = await GetCurrentProcessPrivilegesAsync();

                // コマンド別の必要権限を決定
                result.RequiredPrivileges = DetermineRequiredPrivileges(canonicalCommand, args);

                // 権限昇格の検知
                var privilegeEscalation = DetectPrivilegeEscalation(result.CurrentPrivileges, result.RequiredPrivileges);
                if (privilegeEscalation.HasValue)
                {
                    result.IsAllowed = false;
                    result.Reason = privilegeEscalation.Value.Reason;
                    return result;
                }

                // 不正プロセス委任の検知
                var processDelegation = await DetectUnauthorizedProcessDelegationAsync(canonicalCommand, args);
                if (processDelegation.HasValue)
                {
                    result.IsAllowed = false;
                    result.Reason = processDelegation.Value.Reason;
                    return result;
                }

                // コンテキスト境界の検証
                var contextBoundary = await ValidateExecutionContextAsync(canonicalCommand);
                if (!contextBoundary.IsValid)
                {
                    result.IsAllowed = false;
                    result.Reason = contextBoundary.Reason;
                    return result;
                }

                return result;
            }
            catch (Exception ex)
            {
                // 権限評価エラー時は安全側に倒す
                result.IsAllowed = false;
                result.Reason = $"Privilege evaluation failed: {ex.Message}";
                return result;
            }
        }

        private static async Task<List<string>> GetCurrentProcessPrivilegesAsync()
        {
            var privileges = new List<string>();

            try
            {
                // Windows固有の権限チェック
                if (OperatingSystem.IsWindows())
                {
                    using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    if (identity != null)
                    {
                        privileges.Add("User: " + identity.Name);

                        var principal = new System.Security.Principal.WindowsPrincipal(identity);
                        if (principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                        {
                            privileges.Add("Administrator");
                        }
                        if (principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.SystemOperator))
                        {
                            privileges.Add("SystemOperator");
                        }
                    }
                }
                else
                {
                    // Unix系OSでの基本的な権限情報
                    privileges.Add("UnixUser: " + Environment.UserName);

                    // sudo/su の検知
                    var elevatedMarkers = new[] { "SUDO_UID", "SUDO_USER", "SUDO_COMMAND" };
                    if (elevatedMarkers.Any(marker => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(marker))))
                    {
                        privileges.Add("Elevated");
                    }
                }

                // プロセス所有者情報
                var process = System.Diagnostics.Process.GetCurrentProcess();
                privileges.Add($"ProcessOwner: {process.Id}");

                // 親プロセス情報の取得
                try
                {
                    var parentProcess = GetParentProcess(process);
                    if (parentProcess != null)
                    {
                        privileges.Add($"ParentProcess: {parentProcess.ProcessName} ({parentProcess.Id})");
                    }
                }
                catch
                {
                    // 親プロセス取得失敗時は無視
                }
            }
            catch (Exception ex)
            {
                privileges.Add($"PrivilegeDetectionError: {ex.Message}");
            }

            return privileges;
        }

        private static List<string> DetermineRequiredPrivileges(string canonicalCommand, string[] args)
        {
            var required = new List<string>();

            // コマンド別の必要権限マッピング
            switch (canonicalCommand.ToLowerInvariant())
            {
                case "connect":
                case "disconnect":
                case "scan":
                    required.Add("NetworkControl");
                    break;

                case "config":
                    required.Add("ConfigurationAccess");
                    if (args.Length > 1 && args[1].ToLowerInvariant() == "set")
                    {
                        required.Add("ConfigurationWrite");
                    }
                    break;

                case "security-scan":
                case "diagnostics":
                    required.Add("SystemDiagnostics");
                    break;

                case "profile":
                case "profiles":
                    required.Add("ProfileManagement");
                    break;

                case "backup":
                case "restore":
                    required.Add("BackupRestore");
                    required.Add("FileSystemWrite");
                    break;

                case "log-purge":
                case "audit":
                    required.Add("LogManagement");
                    break;

                case "reset-network":
                case "reset":
                    required.Add("SystemReset");
                    required.Add("NetworkControl");
                    break;

                default:
                    required.Add("BasicExecution");
                    break;
            }

            // 管理者権限が必要なコマンド
            var adminRequiredCommands = new[] { "reset-network", "security-scan", "backup", "restore", "config" };
            if (adminRequiredCommands.Contains(canonicalCommand.ToLowerInvariant()))
            {
                required.Add("Administrator");
            }

            return required;
        }

        private static PrivilegeEscalationInfo? DetectPrivilegeEscalation(List<string> currentPrivileges, List<string> requiredPrivileges)
        {
            // 管理者権限の昇格検知
            if (requiredPrivileges.Contains("Administrator") && !currentPrivileges.Contains("Administrator"))
            {
                return new PrivilegeEscalationInfo
                {
                    Reason = "Administrator privileges required but not available"
                };
            }

            // システム権限の昇格検知
            if (requiredPrivileges.Contains("SystemOperator") && !currentPrivileges.Contains("SystemOperator"))
            {
                return new PrivilegeEscalationInfo
                {
                    Reason = "System operator privileges required but not available"
                };
            }

            // 特権コマンドの実行検知
            var privilegedCommands = new[] { "SystemReset", "BackupRestore", "LogManagement" };
            if (requiredPrivileges.Any(p => privilegedCommands.Contains(p)) &&
                !currentPrivileges.Contains("Administrator"))
            {
                return new PrivilegeEscalationInfo
                {
                    Reason = "Privileged operation requires administrator access"
                };
            }

            return null;
        }

        private static async Task<ProcessDelegationInfo?> DetectUnauthorizedProcessDelegationAsync(string canonicalCommand, string[] args)
        {
            // プロセス委任の検証（例: 外部プロセス呼び出し）
            if (canonicalCommand.ToLowerInvariant() == "run" || args.Any(arg => arg.Contains("cmd") || arg.Contains("powershell")))
            {
                // 危険なプロセス委任パターンの検知
                var dangerousPatterns = new[] { "cmd.exe", "powershell.exe", "bash", "sh", "sudo" };
                if (args.Any(arg => dangerousPatterns.Any(pattern => arg.ToLowerInvariant().Contains(pattern))))
                {
                    return new ProcessDelegationInfo
                    {
                        Reason = "Unauthorized process delegation attempt detected"
                    };
                }
            }

            // ファイル実行の検証
            if (args.Any(arg => arg.EndsWith(".exe") || arg.EndsWith(".bat") || arg.EndsWith(".cmd")))
            {
                // 許可されていない実行ファイルの検知
                var blockedExtensions = new[] { ".exe", ".bat", ".cmd", ".scr", ".pif" };
                if (args.Any(arg => blockedExtensions.Any(ext => arg.ToLowerInvariant().EndsWith(ext))))
                {
                    return new ProcessDelegationInfo
                    {
                        Reason = "Execution of unauthorized file types blocked"
                    };
                }
            }

            return null;
        }

        private static async Task<ExecutionContextValidation> ValidateExecutionContextAsync(string canonicalCommand)
        {
            var result = new ExecutionContextValidation { IsValid = true };

            try
            {
                // 実行コンテキストの一貫性チェック
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();

                // 予期せぬ親プロセスの検知
                var parentProcess = GetParentProcess(currentProcess);
                if (parentProcess != null)
                {
                    var suspiciousParents = new[] { "cmd.exe", "powershell.exe", "explorer.exe", "taskmgr.exe" };
                    if (suspiciousParents.Contains(parentProcess.ProcessName.ToLowerInvariant()))
                    {
                        result.IsValid = false;
                        result.Reason = $"Suspicious parent process detected: {parentProcess.ProcessName}";
                        return result;
                    }
                }

                // 環境変数の改ざん検知
                var criticalEnvVars = new[] { "PATH", "SYSTEMROOT", "PROGRAMFILES" };
                foreach (var envVar in criticalEnvVars)
                {
                    var value = Environment.GetEnvironmentVariable(envVar);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        result.IsValid = false;
                        result.Reason = $"Critical environment variable missing: {envVar}";
                        return result;
                    }
                }

                // 作業ディレクトリの検証
                var currentDir = Environment.CurrentDirectory;
                if (!System.IO.Directory.Exists(currentDir))
                {
                    result.IsValid = false;
                    result.Reason = "Current working directory does not exist";
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Reason = $"Execution context validation failed: {ex.Message}";
            }

            return result;
        }

        private static System.Diagnostics.Process GetParentProcess(System.Diagnostics.Process process)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Windowsでの親プロセス取得
                    var parentId = GetParentProcessIdWindows(process.Id);
                    return parentId.HasValue ? System.Diagnostics.Process.GetProcessById(parentId.Value) : null;
                }
                else
                {
                    // Unix系OSでは親プロセスIDを直接取得
                    return process.Id == 1 ? null : System.Diagnostics.Process.GetProcessById(1);
                }
            }
            catch
            {
                return null;
            }
        }

        private static int? GetParentProcessIdWindows(int processId)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}");

                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    return Convert.ToInt32(obj["ParentProcessId"]);
                }
            }
            catch
            {
                // WMIが利用できない場合
            }

            return null;
        }

        private readonly struct PrivilegeBoundaryCheckResult
        {
            public bool IsAllowed { get; init; }
            public List<string> CurrentPrivileges { get; init; }
            public List<string> RequiredPrivileges { get; init; }
            public string Reason { get; init; }
        }

        private readonly struct PrivilegeEscalationInfo
        {
            public string Reason { get; init; }
        }

        private readonly struct ProcessDelegationInfo
        {
            public string Reason { get; init; }
        }

        private readonly struct ExecutionContextValidation
        {
            public bool IsValid { get; init; }
            public string Reason { get; init; }
        }
}
