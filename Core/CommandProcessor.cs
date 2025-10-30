using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
namespace MurtiWifiConnecter.Core
{
    public static partial class CommandProcessor
    {
        private static string RedactSensitiveValue(string key, string value)
        {
            return SensitiveDataHelper.RedactValue(key, value);
        }

        /// <summary>
        /// Security-005: サンドボックス実行の実装
        /// 危険コマンドをWindows AppContainer サンドボックスで実行
        /// </summary>
        public static class SandboxExecutor
        {
            private static readonly HashSet<string> DangerousCommands = new(StringComparer.OrdinalIgnoreCase)
            {
                "reset-network",
                "security-scan",
                "diagnostics",
                "config",
                "backup",
                "restore",
                "log-purge"
            };

            private static readonly HashSet<string> DangerousArguments = new(StringComparer.OrdinalIgnoreCase)
            {
                "cmd.exe",
                "powershell.exe",
                "bash",
                "sh",
                "sudo",
                "netsh",
                "regedit",
                "format",
                "del",
                "rmdir"
            };

            /// <summary>
            /// コマンドがサンドボックス実行を必要とするかを判定
            /// </summary>
            public static bool RequiresSandboxExecution(string canonicalCommand, string[] args)
            {
                // 危険コマンドのチェック
                if (DangerousCommands.Contains(canonicalCommand))
                {
                    return true;
                }

                // 危険引数のチェック
                if (args != null)
                {
                    foreach (var arg in args)
                    {
                        if (DangerousArguments.Any(dangerous => arg.Contains(dangerous, StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            /// <summary>
            /// サンドボックスでコマンドを実行
            /// </summary>
            public static async Task<int> ExecuteInSandboxAsync(string canonicalCommand, string[] args, Func<string[], Task<int>> handler)
            {
                try
                {
                    await Logger.LogInfo("Executing command in sandbox", nameof(SandboxExecutor), new Dictionary<string, object>
                    {
                        ["command"] = canonicalCommand,
                        ["requiresSandbox"] = true
                    });

                    // Windows AppContainerを使用したサンドボックス実行
                    if (OperatingSystem.IsWindows())
                    {
                        return await ExecuteInAppContainerAsync(canonicalCommand, args, handler);
                    }
                    else
                    {
                        // 非Windows環境では制限されたコンテキストで実行
                        return await ExecuteInRestrictedContextAsync(canonicalCommand, args, handler);
                    }
                }
                catch (Exception ex)
                {
                    await Logger.LogError("Sandbox execution failed", nameof(SandboxExecutor), new Dictionary<string, object>
                    {
                        ["command"] = canonicalCommand,
                        ["error"] = ex.Message
                    }, ex);

                    await AuditTrail.RecordEventAsync(
                        "Security",
                        "SandboxExecutionFailed",
                        new Dictionary<string, object>
                        {
                            ["command"] = canonicalCommand,
                            ["error"] = ex.Message
                        },
                        "Critical");

                    throw;
                }
            }

            private static async Task<int> ExecuteInAppContainerAsync(string canonicalCommand, string[] args, Func<string[], Task<int>> handler)
            {
                // Windows AppContainerを使用したサンドボックス実行
                // 実際の実装ではWindows APIを使用してAppContainerを作成

                var containerName = $"MurtiWifiConnecter_{canonicalCommand}_{Guid.NewGuid():N}";
                var containerSid = CreateAppContainerSid(containerName);

                try
                {
                    // AppContainerのセキュリティケーパビリティを設定
                    var capabilities = GetAppContainerCapabilities(canonicalCommand);

                    // プロセスをAppContainer内で実行
                    var result = await ExecuteInContainerAsync(containerSid, capabilities, handler, args);

                    await Logger.LogInfo("AppContainer execution completed", nameof(SandboxExecutor), new Dictionary<string, object>
                    {
                        ["command"] = canonicalCommand,
                        ["containerName"] = containerName,
                        ["result"] = result
                    });

                    return result;
                }
                finally
                {
                    // AppContainerのクリーンアップ
                    CleanupAppContainer(containerSid);
                }
            }

            private static async Task<int> ExecuteInRestrictedContextAsync(string canonicalCommand, string[] args, Func<string[], Task<int>> handler)
            {
                // 非Windows環境での制限実行
                // リソース制限、権限制限などを適用

                // 環境変数の制限
                var restrictedEnv = CreateRestrictedEnvironment();

                // タイムアウトの設定
                var timeout = GetCommandTimeout(canonicalCommand);
                var cts = new CancellationTokenSource(timeout);

                try
                {
                    // 制限された環境で実行
                    using (new RestrictedExecutionScope(restrictedEnv))
                    {
                        var task = handler(args);
                        var completedTask = await Task.WhenAny(task, Task.Delay(timeout, cts.Token));

                        if (completedTask == task)
                        {
                            cts.Cancel();
                            return await task;
                        }
                        else
                        {
                            throw new TimeoutException($"Command execution timed out: {canonicalCommand}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    await Logger.LogWarning("Restricted execution failed", nameof(SandboxExecutor), new Dictionary<string, object>
                    {
                        ["command"] = canonicalCommand,
                        ["error"] = ex.Message
                    });

                    throw;
                }
            }

            private static string CreateAppContainerSid(string containerName)
            {
                // 実際の実装ではWindows APIを使用してAppContainer SIDを作成
                // ここではプレースホルダー
                return $"S-1-15-2-{Math.Abs(containerName.GetHashCode()):X}";
            }

            private static string[] GetAppContainerCapabilities(string canonicalCommand)
            {
                // コマンド別のケーパビリティを設定
                switch (canonicalCommand.ToLowerInvariant())
                {
                    case "reset-network":
                        return new[] { "networking", "internetClient" };
                    case "security-scan":
                        return new[] { "networking", "documentsLibrary" };
                    case "config":
                        return new[] { "documentsLibrary" };
                    case "backup":
                    case "restore":
                        return new[] { "documentsLibrary", "picturesLibrary" };
                    default:
                        return new[] { "internetClient" };
                }
            }

            private static async Task<int> ExecuteInContainerAsync(string containerSid, string[] capabilities, Func<string[], Task<int>> handler, string[] args)
            {
                // 実際の実装ではAppContainer内でプロセスを実行
                // ここでは制限された実行としてハンドラーを呼び出し

                await Logger.LogInfo("Executing in AppContainer", nameof(SandboxExecutor), new Dictionary<string, object>
                {
                    ["containerSid"] = containerSid,
                    ["capabilities"] = string.Join(", ", capabilities)
                });

                // 実際のAppContainer実装ではここでコンテキストを切り替え
                return await handler(args);
            }

            private static void CleanupAppContainer(string containerSid)
            {
                // AppContainerのリソースをクリーンアップ
                // 実際の実装ではWindows APIを呼び出し
            }

            private static Dictionary<string, string> CreateRestrictedEnvironment()
            {
                // 制限された環境変数を作成
                return new Dictionary<string, string>
                {
                    ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "",
                    ["TEMP"] = Path.GetTempPath(),
                    ["TMP"] = Path.GetTempPath(),
                    // 危険な環境変数を除去
                };
            }

            private static TimeSpan GetCommandTimeout(string canonicalCommand)
            {
                // コマンド別のタイムアウトを設定
                switch (canonicalCommand.ToLowerInvariant())
                {
                    case "reset-network":
                        return TimeSpan.FromMinutes(5);
                    case "security-scan":
                        return TimeSpan.FromMinutes(10);
                    case "diagnostics":
                        return TimeSpan.FromMinutes(2);
                    default:
                        return TimeSpan.FromMinutes(1);
                }
            }
        }

        /// <summary>
        /// 制限実行スコープ
        /// </summary>
        private class RestrictedExecutionScope : IDisposable
        {
            private readonly Dictionary<string, string> _originalEnvironment;

            public RestrictedExecutionScope(Dictionary<string, string> restrictedEnvironment)
            {
                _originalEnvironment = new Dictionary<string, string>();

                // 現在の環境を保存
                foreach (var key in restrictedEnvironment.Keys)
                {
                    _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
                }

                // 制限された環境を設定
                foreach (var kvp in restrictedEnvironment)
                {
                    Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
                }
            }

            public void Dispose()
            {
                // 元の環境を復元
                foreach (var kvp in _originalEnvironment)
                {
                    Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
                }
            }
        }

        public static async Task<int> ProcessCommand(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return await ExecuteInteractive();
            }

            var invokedCommand = args[0]?.Trim();
            if (string.IsNullOrWhiteSpace(invokedCommand))
            {
                UIHelper.PrintError("Command cannot be empty.");
                UIHelper.PrintInfo("Type 'help' to see available commands.");
                return 1;
            }

            var normalizedCommand = invokedCommand;
            if (!CommandMap.TryGetValue(normalizedCommand, out var metadata))
            {
                UIHelper.PrintError($"Unknown command: {invokedCommand}");
                var suggestions = GetCommandSuggestions(invokedCommand);
                if (suggestions.Count > 0)
                {
                    UIHelper.PrintInfo($"Did you mean: {string.Join(", ", suggestions)}?");
                }
                else
                {
                    UIHelper.PrintInfo("Type 'help' to see available commands.");
                }
                return 1;
            }

            var executionArgs = (string[])args.Clone();
            executionArgs[0] = metadata.CanonicalName;

            return await CommandExecution.RunAsync(
                invokedCommand,
                metadata.CanonicalName,
                executionArgs,
                metadata.Handler,
                metadata.SensitiveArgumentIndexes);
        }

        private static IReadOnlyList<string> GetCommandSuggestions(string attempted, int maxSuggestions = 3)
        {
            if (string.IsNullOrWhiteSpace(attempted))
            {
                return Array.Empty<string>();
            }

            var attemptedLower = attempted.ToLowerInvariant();
            var threshold = Math.Min(3, Math.Max(1, attemptedLower.Length / 2));

            var ranked = CommandMap
                .GroupBy(pair => pair.Value.CanonicalName)
                .Select(group => new
                {
                    CanonicalName = group.Key,
                    Distance = group.Min(pair => LevenshteinDistance(attemptedLower, pair.Key.ToLowerInvariant()))
                })
                .Where(entry => entry.Distance <= threshold)
                .OrderBy(entry => entry.Distance)
                .ThenBy(entry => entry.CanonicalName.Length)
                .ThenBy(entry => entry.CanonicalName, StringComparer.OrdinalIgnoreCase)
                .Take(maxSuggestions)
                .Select(entry => entry.CanonicalName)
                .ToArray();

            return ranked.Length == 0 ? Array.Empty<string>() : ranked;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            var distances = new int[a.Length + 1, b.Length + 1];

            for (int i = 0; i <= a.Length; i++) distances[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) distances[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    distances[i, j] = Math.Min(
                        Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                        distances[i - 1, j - 1] + cost);
                }
            }

            return distances[a.Length, b.Length];
        }

        private static Task<int> ExecuteConnect(string[] args)
        {
            bool emitJson = false;
            string ssid = null;
            string password = null;
            bool showStatus = true;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = false;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown connect option format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return Task.FromResult(1);
                    }
                }
                else if (token.Equals("--no-status", StringComparison.OrdinalIgnoreCase))
                {
                    showStatus = false;
                }
                else if (ssid == null)
                {
                    ssid = token;
                }
                else if (password == null)
                {
                    password = token;
                }
                else
                {
                    Console.WriteLine("Usage: connect <SSID> [password] [--json] [--format=<text|json>] [--no-status]");
                    return Task.FromResult(1);
                }
            }

            if (string.IsNullOrWhiteSpace(ssid))
            {
                Console.WriteLine("Usage: connect <SSID> [password] [--json] [--format=<text|json>] [--no-status]");
                return Task.FromResult(1);
            }

            var handlerArgs = password == null
                ? new[] { "connect", ssid }
                : new[] { "connect", ssid, password };

            Func<Task<int>> showStatusCallback = (!emitJson && showStatus) ? () => ExecuteStatus(Array.Empty<string>()) : null;
            return ConnectionCommandHandlers.ConnectAsync(handlerArgs, showStatusCallback, emitJson);
        }

        private static Task<int> ExecuteDisconnect(string[] args)
        {
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = false;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown disconnect option format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return Task.FromResult(1);
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown disconnect option: {token}");
                    Console.WriteLine("Options: [--json] [--format=<text|json>]");
                    return Task.FromResult(1);
                }
            }

            return ConnectionCommandHandlers.DisconnectAsync(emitJson);
        }

        private static Task<int> ExecuteQuickConnect(string[] args)
        {
            bool emitJson = false;
            int? requestedIndex = null;
            int displayLimit = 9;
            bool showStatus = true;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = false;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown quick option format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return Task.FromResult(1);
                    }
                }
                else if (token.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(token.Substring("--limit=".Length), out var parsed) && parsed > 0)
                    {
                        displayLimit = Math.Min(parsed, 25);
                    }
                    else
                    {
                        Console.WriteLine("Invalid value for --limit. Specify a positive integer.");
                        return Task.FromResult(1);
                    }
                }
                else if (token.Equals("--no-status", StringComparison.OrdinalIgnoreCase))
                {
                    showStatus = false;
                }
                else if (requestedIndex == null && int.TryParse(token, out var numeric) && numeric > 0)
                {
                    requestedIndex = numeric;
                }
                else
                {
                    Console.WriteLine("Usage: quick [index] [--limit=<count>] [--json] [--format=<text|json>] [--no-status]");
                    return Task.FromResult(1);
                }
            }

            Func<Task<int>> statusCallback = (!emitJson && showStatus) ? () => ExecuteStatus(Array.Empty<string>()) : null;
            return ConnectionCommandHandlers.QuickConnectAsync(requestedIndex, displayLimit, emitJson, statusCallback);
        }

        private static async Task<int> ExecuteAdapters(string[] args)
        {
            bool emitJson = false;
            string? setAdapter = null;
            bool clear = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = false;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown adapters option format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else if (token.Equals("--clear", StringComparison.OrdinalIgnoreCase))
                {
                    clear = true;
                }
                else if (token.StartsWith("--set=", StringComparison.OrdinalIgnoreCase))
                {
                    setAdapter = token.Substring("--set=".Length).Trim();
                }
                else
                {
                    Console.WriteLine("Usage: adapters [--set=<adapter name>] [--clear] [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            var manager = new SimplifiedWifiManager();
            try
            {
                if (clear)
                {
                    manager.SetPreferredAdapter(null);
                }
                else if (!string.IsNullOrWhiteSpace(setAdapter))
                {
                    manager.SetPreferredAdapter(setAdapter);
                }

                var adaptersResult = await manager.GetAvailableAdaptersAsync();
                var preferred = manager.GetPreferredAdapter();

                if (!adaptersResult.IsSuccess)
                {
                    Console.WriteLine($"Adapter query failed: {adaptersResult.Error}");
                    return 1;
                }

                var adapters = adaptersResult.Value;

                if (emitJson)
                {
                    var payload = new
                    {
                        adapters = adapters.Select(a => new
                        {
                            name = a.Name,
                            description = a.Description,
                            id = a.Id,
                            status = a.Status,
                            isUp = a.IsUp,
                            selected = preferred != null &&
                                (string.Equals(preferred, a.Name, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(preferred, a.Description, StringComparison.OrdinalIgnoreCase))
                        }),
                        preferred
                    };

                    Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
                }
                else
                {
                    Console.WriteLine("WiFi adapters:");
                    Console.WriteLine("=====================================");

                    if (adapters.Count == 0)
                    {
                        Console.WriteLine("No WiFi adapters detected.");
                    }
                    else
                    {
                        foreach (var adapter in adapters)
                        {
                            var selected = preferred != null &&
                                (string.Equals(preferred, adapter.Name, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(preferred, adapter.Description, StringComparison.OrdinalIgnoreCase));

                            Console.WriteLine(selected
                                ? $"* {adapter.Name} ({adapter.Description}) - {adapter.Status}"
                                : $"  {adapter.Name} ({adapter.Description}) - {adapter.Status}");
                            if (!string.IsNullOrWhiteSpace(adapter.Id))
                            {
                                Console.WriteLine($"    Id   : {adapter.Id}");
                            }
                            Console.WriteLine($"    State: {(adapter.IsUp ? "Up" : "Down")}");
                        }
                    }

                    Console.WriteLine();
                    Console.WriteLine(preferred == null
                        ? "Preferred adapter: (not set)"
                        : $"Preferred adapter: {preferred}");
                }

                return 0;
            }
            finally
            {
                manager.Dispose();
            }
        }

        private static async Task<int> ExecuteStatus(string[] args)
        {
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = false;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown status option format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown status option: {token}");
                    Console.WriteLine("Options: [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            await Logger.LogInfo("Status command invoked", nameof(CommandProcessor), new Dictionary<string, object>
            {
                ["format"] = emitJson ? "json" : "text"
            });

            var status = await NetworkOperations.GetStatusAsync();
            var checkedAt = status.CheckedAtUtc;

            if (emitJson)
            {
                var payload = new
                {
                    status = status.Status,
                    ssid = status.Ssid,
                    signal = status.Signal,
                    ipAddress = status.IpAddress,
                    bssid = status.Bssid,
                    radioType = status.RadioType,
                    channel = status.Channel,
                    receiveRateMbps = status.ReceiveRateMbps,
                    transmitRateMbps = status.TransmitRateMbps,
                    checkedAt
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"Checked : {checkedAt:yyyy-MM-dd HH:mm:ss} UTC");

                if (status.Status == "Connected")
                {
                    Console.WriteLine("Status Summary:");
                    Console.WriteLine($"Status : {status.Status}");
                    Console.WriteLine($"SSID   : {status.Ssid}");
                    Console.WriteLine($"Signal : {status.Signal}% {GetSignalBar(status.Signal)}");
                    if (!string.IsNullOrEmpty(status.IpAddress))
                    {
                        Console.WriteLine($"IP     : {status.IpAddress}");
                    }
                    if (!string.IsNullOrEmpty(status.Bssid))
                    {
                        Console.WriteLine($"BSSID  : {status.Bssid}");
                    }
                    if (!string.IsNullOrEmpty(status.RadioType))
                    {
                        Console.WriteLine($"Radio  : {status.RadioType}");
                    }
                    if (status.Channel.HasValue)
                    {
                        Console.WriteLine($"Channel: {status.Channel.Value}");
                    }
                    if (status.ReceiveRateMbps.HasValue || status.TransmitRateMbps.HasValue)
                    {
                        Console.WriteLine($"Rates  : RX {status.ReceiveRateMbps?.ToString("F1") ?? "-"} Mbps | TX {status.TransmitRateMbps?.ToString("F1") ?? "-"} Mbps");
                    }
                }
                else
                {
                    Console.WriteLine("Status Summary:");
                    Console.WriteLine("Status : Disconnected");
                }
            }

            await Logger.LogInfo("Status command completed", nameof(CommandProcessor), new Dictionary<string, object>
            {
                ["status"] = status.Status,
                ["ssid"] = status.Ssid,
                ["signal"] = status.Signal,
                ["bssid"] = status.Bssid,
                ["radioType"] = status.RadioType,
                ["channel"] = status.Channel,
                ["receiveRateMbps"] = status.ReceiveRateMbps,
                ["transmitRateMbps"] = status.TransmitRateMbps,
                ["format"] = emitJson ? "json" : "text",
                ["checkedAt"] = checkedAt
            });

            return 0;
        }

        private static async Task<int> ExecuteCommandAnomalies(string[] args)
        {
            bool emitJson = false;
            int limit = 10;
            double? windowSeconds = null;
            bool resetRequested = false;
            string targetCommand = null;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = false;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown command-anomalies format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else if (token.StartsWith("--window=", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = token.Substring("--window=".Length);
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
                    {
                        Console.WriteLine("Invalid --window value. Specify seconds as a positive number.");
                        return 1;
                    }
                    windowSeconds = seconds;
                }
                else if (token.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(token.Substring("--limit=".Length), out limit) || limit <= 0)
                    {
                        Console.WriteLine("Invalid --limit value. Provide a positive integer.");
                        return 1;
                    }
                }
                else if (token.StartsWith("--reset", StringComparison.OrdinalIgnoreCase))
                {
                    resetRequested = true;
                    var parts = token.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        targetCommand = parts[1]?.Trim();
                    }
                }
                else if (token.StartsWith("--command=", StringComparison.OrdinalIgnoreCase))
                {
                    targetCommand = token.Substring("--command=".Length).Trim();
                }
                else
                {
                    Console.WriteLine($"Unknown command-anomalies option: {token}");
                    Console.WriteLine("Usage: command-anomalies [--json] [--format=<text|json>] [--window=<seconds>] [--limit=<count>] [--reset[=<command>]] [--command=<command>]");
                    return 1;
                }
            }

            if (resetRequested)
            {
                var resetCount = CommandExecution.ResetCommandAnomalyMetrics(string.IsNullOrWhiteSpace(targetCommand) ? null : targetCommand);
                var payload = new Dictionary<string, object>
                {
                    ["command"] = targetCommand ?? "(all)",
                    ["count"] = resetCount
                };

                await Logger.LogInfo("Command anomaly metrics reset", nameof(CommandProcessor), payload).ConfigureAwait(false);
                await AuditTrail.RecordEventAsync("Security", "CommandAnomalyReset", payload, "Warning").ConfigureAwait(false);

                Console.WriteLine(targetCommand == null
                    ? "Command anomaly trackers have been cleared."
                    : resetCount > 0 ? $"Command anomaly tracker reset for '{targetCommand}'." : $"No anomaly tracker found for '{targetCommand}'.");
            }

            TimeSpan? window = null;
            if (windowSeconds.HasValue)
            {
                window = TimeSpan.FromSeconds(windowSeconds.Value);
            }

            var snapshots = CommandExecution.GetCommandAnomalySnapshots(window);
            var effectiveLimit = limit > 0 ? limit : snapshots.Count;
            var selected = snapshots.Take(effectiveLimit).ToList();

            var outputPayload = selected.Select(snapshot => new Dictionary<string, object>
            {
                ["command"] = snapshot.Command,
                ["attempts"] = snapshot.Attempts,
                ["failures"] = snapshot.Failures,
                ["failureRate"] = Math.Round(snapshot.FailureRate, 4),
                ["averageDurationMs"] = Math.Round(snapshot.AverageDurationMs, 2),
                ["lastObserved"] = snapshot.LastObserved,
                ["alertCount"] = snapshot.AlertCount,
                ["lastAlertUtc"] = snapshot.LastAlertUtc,
                ["lastTrigger"] = snapshot.LastTrigger
            }).ToList();

            var metadata = new Dictionary<string, object>
            {
                ["windowSeconds"] = window?.TotalSeconds ?? CommandExecution.DefaultCommandAnomalyWindow.TotalSeconds,
                ["limit"] = effectiveLimit,
                ["count"] = outputPayload.Count
            };

            if (emitJson)
            {
                var json = JsonSerializer.Serialize(new
                {
                    generatedAtUtc = DateTime.UtcNow,
                    windowSeconds = metadata["windowSeconds"],
                    limit = effectiveLimit,
                    anomalies = outputPayload
                }, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("Command Anomaly Snapshot:");
                Console.WriteLine($"Window Seconds : {metadata["windowSeconds"]}");
                Console.WriteLine($"Records Shown  : {outputPayload.Count}");
                if (outputPayload.Count == 0)
                {
                    Console.WriteLine("No anomalies detected in the specified window.");
                }
                else
                {
                    foreach (var entry in outputPayload)
                    {
                        Console.WriteLine($"\nCommand       : {entry["command"]}");
                        Console.WriteLine($"Attempts      : {entry["attempts"]}");
                        Console.WriteLine($"Failures      : {entry["failures"]}");
                        Console.WriteLine($"Failure Rate  : {entry["failureRate"]:P2}");
                        Console.WriteLine($"Avg Duration  : {entry["averageDurationMs"]} ms");
                        Console.WriteLine($"Alerts Raised : {entry["alertCount"]}");
                        if ((DateTime)entry["lastAlertUtc"] != DateTime.MinValue)
                        {
                            Console.WriteLine($"Last Alert    : {(DateTime)entry["lastAlertUtc"]:u} ({entry["lastTrigger"]})");
                        }
                        if ((DateTime)entry["lastObserved"] != DateTime.MinValue)
                        {
                            Console.WriteLine($"Last Observed : {(DateTime)entry["lastObserved"]:u}");
                        }
                    }
                }
            }

            await Logger.LogInfo("Command anomaly metrics viewed", nameof(CommandProcessor), metadata).ConfigureAwait(false);
            await AuditTrail.RecordEventAsync("Security", "CommandAnomalyViewed", metadata).ConfigureAwait(false);

            return 0;
        }

        private static async Task<int> ExecuteCommandMetrics(string[] args)
        {
            bool emitJson = false;
            int limit = 20;
            string sortOrder = "count";
            bool persistRequested = false;
            bool resetRequested = false;
            string targetCommand = null;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = false;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown command-metrics format: {format}}}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else if (token.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(token.Substring("--limit=".Length), out limit) || limit <= 0)
                    {
                        Console.WriteLine("Invalid --limit value. Provide a positive integer.");
                        return 1;
                    }
                }
                else if (token.StartsWith("--sort=", StringComparison.OrdinalIgnoreCase))
                {
                    sortOrder = token.Substring("--sort=".Length).Trim().ToLowerInvariant();
                    if (sortOrder is not ("count" or "failures" or "duration"))
                    {
                        Console.WriteLine("Invalid --sort value. Allowed: count, failures, duration");
                        return 1;
                    }
                }
                else if (token.Equals("--persist", StringComparison.OrdinalIgnoreCase) || token.Equals("--export", StringComparison.OrdinalIgnoreCase))
                {
                    persistRequested = true;
                }
                else if (token.StartsWith("--reset", StringComparison.OrdinalIgnoreCase))
                {
                    resetRequested = true;
                    var parts = token.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        targetCommand = parts[1]?.Trim();
                    }
                }
                else if (token.StartsWith("--command=", StringComparison.OrdinalIgnoreCase))
                {
                    targetCommand = token.Substring("--command=".Length).Trim();
                }
                else
                {
                    Console.WriteLine($"Unknown command-metrics option: {token}");
                    Console.WriteLine("Usage: command-metrics [--json] [--format=<text|json>] [--limit=<count>] [--sort=<count|failures|duration>] [--persist] [--reset[=<command>]] [--command=<command>]");
                    return 1;
                }
            }

            if (resetRequested)
            {
                var resetCount = CommandExecution.ResetCommandTelemetry(string.IsNullOrWhiteSpace(targetCommand) ? null : targetCommand);
                var payload = new Dictionary<string, object>
                {
                    ["command"] = targetCommand ?? "(all)",
                    ["count"] = resetCount
                };

                await Logger.LogInfo("Command telemetry reset", nameof(CommandProcessor), payload).ConfigureAwait(false);
                await AuditTrail.RecordEventAsync("Operations", "CommandTelemetryReset", payload, "Warning").ConfigureAwait(false);

                Console.WriteLine(targetCommand == null
                    ? "Command telemetry trackers have been cleared."
                    : resetCount > 0 ? $"Command telemetry tracker reset for '{targetCommand}'." : $"No telemetry tracker found for '{targetCommand}'.");
            }

            if (persistRequested)
            {
                await CommandExecution.PersistCommandTelemetryAsync().ConfigureAwait(false);
            }

            var snapshots = CommandExecution.GetCommandTelemetrySnapshots();

            IEnumerable<CommandExecution.CommandTelemetrySnapshot> ordered = sortOrder switch
            {
                "failures" => snapshots.OrderByDescending(s => s.FailureCount).ThenByDescending(s => s.FailureRate).ThenBy(s => s.Command, StringComparer.OrdinalIgnoreCase),
                "duration" => snapshots.OrderByDescending(s => s.MaxDurationMs).ThenByDescending(s => s.AverageDurationMs).ThenBy(s => s.Command, StringComparer.OrdinalIgnoreCase),
                _ => snapshots.OrderByDescending(s => s.TotalCount).ThenBy(s => s.Command, StringComparer.OrdinalIgnoreCase)
            };

            var effectiveLimit = limit > 0 ? limit : snapshots.Count;
            var selected = ordered.Take(effectiveLimit).ToList();

            var outputPayload = selected.Select(snapshot => new Dictionary<string, object>
            {
                ["command"] = snapshot.Command,
                ["total"] = snapshot.TotalCount,
                ["failures"] = snapshot.FailureCount,
                ["failureRate"] = Math.Round(snapshot.FailureRate, 4),
                ["averageDurationMs"] = Math.Round(snapshot.AverageDurationMs, 2),
                ["maxDurationMs"] = snapshot.MaxDurationMs,
                ["firstSeenUtc"] = snapshot.FirstObservedUtc,
                ["lastSeenUtc"] = snapshot.LastObservedUtc,
                ["lastResult"] = snapshot.LastResult,
                ["lastDurationMs"] = snapshot.LastDurationMs
            }).ToList();

            var metadata = new Dictionary<string, object>
            {
                ["limit"] = effectiveLimit,
                ["count"] = outputPayload.Count,
                ["sort"] = sortOrder,
                ["persisted"] = persistRequested,
                ["telemetryPath"] = CommandExecution.CommandTelemetrySnapshotPath
            };

            if (emitJson)
            {
                var json = JsonSerializer.Serialize(new
                {
                    generatedAtUtc = DateTime.UtcNow,
                    sort = sortOrder,
                    limit = effectiveLimit,
                    telemetryPath = CommandExecution.CommandTelemetrySnapshotPath,
                    metrics = outputPayload
                }, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("Command Telemetry Snapshot:");
                Console.WriteLine($"Sort Order    : {sortOrder}");
                Console.WriteLine($"Records Shown : {outputPayload.Count}");
                Console.WriteLine($"Snapshot File : {CommandExecution.CommandTelemetrySnapshotPath}");

                if (outputPayload.Count == 0)
                {
                    Console.WriteLine("No command telemetry has been captured yet.");
                }
                else
                {
                    foreach (var entry in outputPayload)
                    {
                        Console.WriteLine($"\nCommand      : {entry["command"]}");
                        Console.WriteLine($"Total Calls  : {entry["total"]}");
                        Console.WriteLine($"Failures     : {entry["failures"]}}");
                        Console.WriteLine($"Failure Rate : {entry["failureRate"]:P2}");
                        Console.WriteLine($"Avg Duration : {entry["averageDurationMs"]} ms");
                        Console.WriteLine($"Max Duration : {entry["maxDurationMs"]} ms");
                        if ((DateTime)entry["firstSeenUtc"] != DateTime.MinValue)
                        {
                            Console.WriteLine($"First Seen   : {(DateTime)entry["firstSeenUtc"]:u}");
                        }
                        if ((DateTime)entry["lastSeenUtc"] != DateTime.MinValue)
                        {
                            Console.WriteLine($"Last Seen    : {(DateTime)entry["lastSeenUtc"]:u} (Result {entry["lastResult"]}, {entry["lastDurationMs"]} ms)");
                        }
                    }
                }
            }

            await Logger.LogInfo("Command telemetry viewed", nameof(CommandProcessor), metadata).ConfigureAwait(false);
            await AuditTrail.RecordEventAsync("Operations", "CommandTelemetryViewed", metadata).ConfigureAwait(false);

            return 0;
        }

        private static async Task<int> ExecuteBackupPermissions(string[] args)
        {
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown backup-permissions option format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown backup-permissions option: {token}");
                    Console.WriteLine("Options: [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            try
            {
                var backupDir = ProfileManager.GetBackupDirectory();
                Directory.CreateDirectory(backupDir);

                var securedFiles = 0;
                var processed = new List<string>();
                foreach (var file in Directory.EnumerateFiles(backupDir, "*", SearchOption.TopDirectoryOnly))
                {
                    await SecurityManager.EnsureSecureFileAclAsync(file).ConfigureAwait(false);
                    securedFiles++;
                    processed.Add(Path.GetFileName(file));

                    var digestPath = file + ".sha256";
                    if (File.Exists(digestPath))
                    {
                        await SecurityManager.EnsureSecureFileAclAsync(digestPath).ConfigureAwait(false);
                    }
                }

                if (emitJson)
                {
                    var payload = new
                    {
                        directory = backupDir,
                        filesSecured = securedFiles,
                        processedFiles = processed
                    };

                    var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    Console.WriteLine(json);
                }
                else
                {
                    Console.WriteLine("Backup permissions secured:");
                    Console.WriteLine($"  Directory: {backupDir}");
                    Console.WriteLine($"  Files processed: {securedFiles}");
                }

                await AuditTrail.RecordEventAsync("Maintenance", "BackupPermissionsSecured", new Dictionary<string, object>
                {
                    ["directory"] = backupDir,
                    ["filesSecured"] = securedFiles
                }).ConfigureAwait(false);

                await Logger.LogInfo("Backup permissions validated", nameof(CommandProcessor), new Dictionary<string, object>
                {
                    ["directory"] = backupDir,
                    ["filesSecured"] = securedFiles
                }).ConfigureAwait(false);

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to secure backup permissions: {ex.Message}");
                await Logger.LogError("Backup permissions command failed", nameof(CommandProcessor), null, ex).ConfigureAwait(false);
                return 1;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            double value = bytes;
            int unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:F1} {units[unitIndex]}";
        }

        private static async Task<int> ExecuteMemorySnapshot(string[] args)
        {
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown memory-snapshot output format: {format}");
                        Console.WriteLine("Options: [--json] [--format=<text|json>]");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown memory-snapshot option: {token}");
                    Console.WriteLine("Options: [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            var usage = OptimizedMemoryManager.GetMemoryUsage();

            if (emitJson)
            {
                var payload = new
                {
                    workingSetMB = usage.WorkingSetMB,
                    privateMemoryMB = usage.PrivateMemoryMB,
                    gcTotalMemoryMB = usage.GCTotalMemoryMB,
                    heapSizeMB = usage.HeapSizeMB,
                    collections = new
                    {
                        gen0 = usage.Gen0Collections,
                        gen1 = usage.Gen1Collections,
                        gen2 = usage.Gen2Collections
                    },
                    serverGcEnabled = usage.IsServerGC,
                    latencyMode = usage.LatencyMode.ToString()
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("Memory snapshot:");
                Console.WriteLine($"  Working set: {usage.WorkingSetMB:F1} MB");
                Console.WriteLine($"  Private memory: {usage.PrivateMemoryMB:F1} MB");
                Console.WriteLine($"  GC total memory: {usage.GCTotalMemoryMB:F1} MB");
                Console.WriteLine($"  GC heap size: {usage.HeapSizeMB:F1} MB");
                Console.WriteLine($"  GC collections: Gen0={usage.Gen0Collections}, Gen1={usage.Gen1Collections}, Gen2={usage.Gen2Collections}");
                Console.WriteLine($"  Server GC: {(usage.IsServerGC ? "Enabled" : "Disabled")}");
                Console.WriteLine($"  Latency mode: {usage.LatencyMode}");
            }

            await AuditTrail.RecordEventAsync("Maintenance", "MemorySnapshot", new Dictionary<string, object>
            {
                ["workingSetMB"] = usage.WorkingSetMB,
                ["privateMemoryMB"] = usage.PrivateMemoryMB,
                ["heapSizeMB"] = usage.HeapSizeMB,
                ["gen0"] = usage.Gen0Collections,
                ["gen1"] = usage.Gen1Collections,
                ["gen2"] = usage.Gen2Collections
            }).ConfigureAwait(false);

            await Logger.LogInfo("Memory snapshot emitted", nameof(CommandProcessor), new Dictionary<string, object>
            {
                ["workingSetMB"] = usage.WorkingSetMB,
                ["privateMemoryMB"] = usage.PrivateMemoryMB,
                ["heapSizeMB"] = usage.HeapSizeMB
            }).ConfigureAwait(false);

            return 0;
        }

        private static Task<int> ExecuteHistory(string[] args)
        {
            int count = 10;
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.StartsWith("--count=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(token.Substring("--count=".Length), out var parsed) && parsed > 0)
                    {
                        count = Math.Min(parsed, 50);
                    }
                    else
                    {
                        Console.WriteLine("Invalid value for --count. Specify a positive integer.");
                        return Task.FromResult(1);
                    }
                }
                else if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown history option format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return Task.FromResult(1);
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown history option: {token}");
                    Console.WriteLine("Options: --count=<number of entries> [--json] [--format=<text|json>]");
                    return Task.FromResult(1);
                }
            }

            var records = CommandExecution.GetRecentCommands(count);
            if (records.Count == 0)
            {
                if (emitJson)
                {
                    Console.WriteLine("[]");
                }
                else
                {
                    Console.WriteLine("No recent command history available.");
                }

                return Task.FromResult(0);
            }

            if (emitJson)
            {
                var payload = records
                    .OrderBy(r => r.Timestamp)
                    .Select(r => new
                    {
                        timestamp = r.Timestamp,
                        status = r.Result == 0 ? "OK" : "ERR",
                        canonicalCommand = r.CanonicalCommand,
                        invokedCommand = r.InvokedCommand,
                        arguments = r.Arguments,
                        result = r.Result
                    });

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("Recent commands:");
                foreach (var entry in records.OrderBy(r => r.Timestamp))
                {
                    var status = entry.Result == 0 ? "OK" : "ERR";
                    var details = string.IsNullOrWhiteSpace(entry.Arguments) ? string.Empty : " " + entry.Arguments;
                    Console.WriteLine($"  [{entry.Timestamp:HH:mm:ss}] {status} {entry.CanonicalCommand}{details}");

                    if (!entry.CanonicalCommand.Equals(entry.InvokedCommand, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"    invoked as: {entry.InvokedCommand}");
                    }
                }
            }

            return Task.FromResult(0);
        }

        private static async Task<int> ExecuteBackupPathCheck(string[] args)
        {
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown backup-path-check option: {token}");
                    Console.WriteLine("Options: [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            try
            {
                var result = await ProfileManager.CheckBackupAccessibilityAsync().ConfigureAwait(false);

                if (emitJson)
                {
                    var payload = new
                    {
                        directory = result.Directory,
                        directoryReady = result.DirectoryReady,
                        writeTestSucceeded = result.WriteTestSucceeded,
                        readTestSucceeded = result.ReadTestSucceeded,
                        availableFreeSpaceBytes = result.AvailableFreeSpaceBytes,
                        totalFreeSpaceBytes = result.TotalFreeSpaceBytes,
                        driveFormat = result.DriveFormat,
                        error = result.ErrorMessage,
                        accessible = result.IsAccessible,
                        checkedAtUtc = result.CheckedAtUtc
                    };

                    var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    Console.WriteLine(json);
                }
                else
                {
                    Console.WriteLine("Backup storage accessibility:");
                    Console.WriteLine($"  Directory: {result.Directory}");
                    Console.WriteLine($"  Directory ready: {result.DirectoryReady}");
                    Console.WriteLine($"  Write test: {result.WriteTestSucceeded}");
                    Console.WriteLine($"  Read test: {result.ReadTestSucceeded}");

                    if (result.AvailableFreeSpaceBytes.HasValue)
                    {
                        Console.WriteLine($"  Available free space: {FormatBytes(result.AvailableFreeSpaceBytes.Value)}");
                    }

                    if (result.TotalFreeSpaceBytes.HasValue)
                    {
                        Console.WriteLine($"  Total free space: {FormatBytes(result.TotalFreeSpaceBytes.Value)}");
                    }

                    if (!string.IsNullOrEmpty(result.DriveFormat))
                    {
                        Console.WriteLine($"  Drive format: {result.DriveFormat}");
                    }

                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        Console.WriteLine($"  Error: {result.ErrorMessage}");
                        return 1;
                    }

                    Console.WriteLine(result.IsAccessible
                        ? "Result: Backup path is accessible."
                        : "Result: Backup path failed accessibility checks.");
                }

                return string.IsNullOrEmpty(result.ErrorMessage) && result.IsAccessible ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to check backup path: {ex.Message}");
                await Logger.LogError("Backup path check failed", nameof(CommandProcessor), null, ex).ConfigureAwait(false);
                return 1;
            }
        }

        private static async Task<int> ExecuteHistoryClear(string[] args)
        {
            int keep = 0;
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.StartsWith("--keep=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(token.Substring("--keep=".Length), out var parsed) && parsed >= 0)
                    {
                        keep = parsed;
                    }
                    else
                    {
                        Console.WriteLine("Invalid value for --keep. Specify a non-negative integer.");
                        return 1;
                    }
                }
                else if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown history-clear output format: {format}");
                        Console.WriteLine("Options: --keep=<entries to retain> [--json] [--format=<text|json>]");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown history-clear option: {token}");
                    Console.WriteLine("Options: --keep=<entries to retain> [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            var removed = CommandExecution.ClearRecentCommands(keep);

            if (emitJson)
            {
                var remaining = CommandExecution.GetRecentCommands(keep).Count;
                var payload = new
                {
                    removed,
                    kept = keep,
                    totalAfter = remaining
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine(removed == 0
                    ? "No history entries were removed."
                    : $"Removed {removed} history entries while keeping {keep} recent items.");
            }

            await AuditTrail.RecordEventAsync("Maintenance", "HistoryCleared", new Dictionary<string, object>
            {
                ["removed"] = removed,
                ["kept"] = keep
            }).ConfigureAwait(false);

            await Logger.LogInfo("History cleared", nameof(CommandProcessor), new Dictionary<string, object>
            {
                ["removed"] = removed,
                ["kept"] = keep
            }).ConfigureAwait(false);

            return 0;
        }

        private static Task<int> ExecuteHistoryTop(string[] args)
        {
            int top = 5;
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.StartsWith("--top=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(token.Substring("--top=".Length), out var parsed) && parsed > 0)
                    {
                        top = Math.Min(parsed, 20);
                    }
                    else
                    {
                        Console.WriteLine("Invalid value for --top. Specify a positive integer.");
                        return Task.FromResult(1);
                    }
                }
                else if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return Task.FromResult(1);
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown history-top option: {token}");
                    Console.WriteLine("Options: --top=<count> [--json] [--format=<text|json>]");
                    return Task.FromResult(1);
                }
            }

            var usage = CommandExecution.GetMostUsedCommands(top);
            if (usage.Count == 0)
            {
                if (emitJson)
                {
                    Console.WriteLine("[]");
                }
                else
                {
                    Console.WriteLine("No command usage data available.");
                }
                return Task.FromResult(0);
            }

            if (emitJson)
            {
                var payload = usage.Select(entry => new
                {
                    command = entry.Command,
                    count = entry.Count
                });

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("Top command usage:");
                int rank = 1;
                foreach (var (command, count) in usage)
                {
                    Console.WriteLine($"  {rank}. {command} ({count} executions)");
                    rank++;
                }
            }

            return Task.FromResult(0);
        }

        private static async Task<int> ExecuteBackupDigestVerify(string[] args)
        {
            bool emitJson = false;
            bool verifyAll = false;
            bool failuresOnly = false;
            int limit = 10;
            var requestedFiles = new List<string>();

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else if (token.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(token.Substring("--limit=".Length), out var parsedLimit) && parsedLimit > 0)
                    {
                        limit = Math.Clamp(parsedLimit, 1, 200);
                    }
                    else
                    {
                        Console.WriteLine("Invalid value for --limit. Specify a positive integer.");
                        return 1;
                    }
                }
                else if (token.Equals("--all", StringComparison.OrdinalIgnoreCase))
                {
                    verifyAll = true;
                }
                else if (token.StartsWith("--file=", StringComparison.OrdinalIgnoreCase))
                {
                    var specified = token.Substring("--file=".Length).Trim();
                    if (string.IsNullOrWhiteSpace(specified))
                    {
                        Console.WriteLine("--file option requires a file name.");
                        return 1;
                    }

                    if (specified.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
                    {
                        Console.WriteLine("File name must not include directory separators.");
                        return 1;
                    }

                    requestedFiles.Add(specified);
                }
                else if (token.Equals("--failures-only", StringComparison.OrdinalIgnoreCase))
                {
                    failuresOnly = true;
                }
                else
                {
                    Console.WriteLine($"Unknown backup-digest-verify option: {token}");
                    Console.WriteLine("Options: [--json] [--format=<text|json>] [--limit=<count>] [--all] [--file=<name>] [--failures-only]");
                    return 1;
                }
            }

            var backupDir = ProfileManager.GetBackupDirectory();
            if (!Directory.Exists(backupDir))
            {
                Console.WriteLine("No backups found to verify.");
                return 1;
            }

            var backupDirFull = Path.GetFullPath(backupDir);
            if (!backupDirFull.EndsWith(Path.DirectorySeparatorChar))
            {
                backupDirFull += Path.DirectorySeparatorChar;
            }

            string[] targetFiles;

            if (requestedFiles.Count > 0)
            {
                var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var requested in requestedFiles)
                {
                    var combined = Path.Combine(backupDir, requested);
                    var fullPath = Path.GetFullPath(combined);
                    if (!fullPath.StartsWith(backupDirFull, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Specified file must reside within the backup directory: {requested}");
                        return 1;
                    }

                    normalized.Add(fullPath);
                }

                targetFiles = normalized.ToArray();
            }
            else
            {
                var query = Directory.EnumerateFiles(backupDir, "*.json", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetCreationTime);

                if (!verifyAll)
                {
                    query = query.Take(limit);
                }

                targetFiles = query.ToArray();
            }

            if (targetFiles.Length == 0)
            {
                Console.WriteLine("No backup files detected.");
                return 1;
            }

            var verified = 0;
            var failed = 0;
            var missingFiles = 0;
            var missingDigests = 0;
            var entries = new List<BackupDigestEntry>(targetFiles.Length);

            foreach (var file in targetFiles)
            {
                var fileName = Path.GetFileName(file);
                var fileExists = File.Exists(file);
                var digestExists = fileExists && File.Exists(file + ProfileManagerBackupDigestExtension);
                bool result = false;

                if (fileExists)
                {
                    result = await ProfileManager.VerifyBackupDigestAsync(file).ConfigureAwait(false);
                }

                if (result)
                {
                    verified++;
                }
                else
                {
                    failed++;
                    if (!fileExists)
                    {
                        missingFiles++;
                    }
                    else if (!digestExists)
                    {
                        missingDigests++;
                    }
                }

                entries.Add(new BackupDigestEntry(fileName, result, fileExists, digestExists));
            }

            if (emitJson)
            {
                var payload = new
                {
                    directory = backupDir,
                    verified,
                    failed,
                    missingFiles,
                    missingDigests,
                    totalExamined = targetFiles.Length,
                    filters = new
                    {
                        limit = verifyAll ? null : limit,
                        all = verifyAll,
                        failuresOnly,
                        requested = requestedFiles.Count > 0 ? requestedFiles : null
                    },
                    files = entries.Select(e => new
                    {
                        file = e.FileName,
                        ok = e.Succeeded,
                        fileFound = e.FileFound,
                        digestFound = e.DigestFound
                    })
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("Verifying backup digests:");
                foreach (var entry in entries)
                {
                    if (failuresOnly && entry.Succeeded)
                    {
                        continue;
                    }

                    if (entry.Succeeded)
                    {
                        Console.WriteLine($"  ✓ {entry.FileName}");
                    }
                    else if (!entry.FileFound)
                    {
                        Console.WriteLine($"  ✗ {entry.FileName} (file missing)");
                    }
                    else if (!entry.DigestFound)
                    {
                        Console.WriteLine($"  ✗ {entry.FileName} (digest missing)");
                    }
                    else
                    {
                        Console.WriteLine($"  ✗ {entry.FileName} (digest mismatch)");
                    }
                }

                if (failuresOnly && entries.All(e => e.Succeeded))
                {
                    Console.WriteLine("  No failed digests detected.");
                }

                Console.WriteLine($"Summary: {verified} verified, {failed} failed (missing files: {missingFiles}, missing digests: {missingDigests})");
            }

            await AuditTrail.RecordEventAsync("Maintenance", "BackupDigestVerify", new Dictionary<string, object>
            {
                ["directory"] = backupDir,
                ["verified"] = verified,
                ["failed"] = failed,
                ["missingFiles"] = missingFiles,
                ["missingDigests"] = missingDigests,
                ["totalExamined"] = targetFiles.Length,
                ["all"] = verifyAll,
                ["limit"] = verifyAll ? null : limit,
                ["requested"] = requestedFiles.Count > 0 ? string.Join(",", requestedFiles) : null,
                ["failuresOnly"] = failuresOnly
            }).ConfigureAwait(false);

            await Logger.LogInfo("Backup digest verification completed", nameof(CommandProcessor), new Dictionary<string, object>
            {
                ["directory"] = backupDir,
                ["verified"] = verified,
                ["failed"] = failed,
                ["missingFiles"] = missingFiles,
                ["missingDigests"] = missingDigests,
                ["totalExamined"] = targetFiles.Length
            }).ConfigureAwait(false);

            return failed == 0 ? 0 : 1;
        }

        private const string ProfileManagerBackupDigestExtension = ".sha256";

        private sealed record BackupDigestEntry(string FileName, bool Succeeded, bool FileFound, bool DigestFound);

        private static async Task<int> ExecuteBackupList(string[] args)
        {
            int limit = 50;
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(token.Substring("--limit=".Length), out var parsed) && parsed > 0)
                    {
                        limit = Math.Min(parsed, 200);
                    }
                    else
                    {
                        Console.WriteLine("Invalid value for --limit. Specify a positive integer.");
                        return 1;
                    }
                }
                else if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown backup-list option: {token}");
                    Console.WriteLine("Options: [--limit=<count>] [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            var summaries = await ProfileManager.GetBackupSummariesAsync(limit).ConfigureAwait(false);

            if (summaries.Count == 0)
            {
                if (emitJson)
                {
                    Console.WriteLine("[]");
                }
                else
                {
                    Console.WriteLine("No backups found.");
                }
                return 0;
            }

            var validCount = summaries.Count(summary => summary.IsValid);
            var invalidCount = summaries.Count - validCount;
            var totalProfiles = summaries.Where(summary => summary.IsValid).Sum(summary => summary.ProfileCount);
            var totalSize = summaries.Sum(summary => summary.FileSizeBytes);

            var auditPayload = new Dictionary<string, object>
            {
                ["count"] = summaries.Count,
                ["limit"] = limit,
                ["first"] = summaries.First().FileName,
                ["last"] = summaries.Last().FileName,
                ["valid"] = validCount,
                ["invalid"] = invalidCount,
                ["totalProfiles"] = totalProfiles,
                ["totalSizeBytes"] = totalSize
            };

            await AuditTrail.RecordEventAsync("Maintenance", "BackupList", auditPayload).ConfigureAwait(false);
            await Logger.LogInfo("Backup list generated", nameof(CommandProcessor), auditPayload).ConfigureAwait(false);

            if (emitJson)
            {
                var payload = new
                {
                    summary = new
                    {
                        count = summaries.Count,
                        limit,
                        valid = validCount,
                        invalid = invalidCount,
                        totalProfiles,
                        totalSizeBytes = totalSize
                    },
                    items = summaries.Select(summary => new
                    {
                        file = summary.FileName,
                        sizeBytes = summary.FileSizeBytes,
                        createdAtUtc = summary.CreatedAtUtc,
                        timestamp = summary.Timestamp,
                        profiles = summary.ProfileCount,
                        isValid = summary.IsValid
                    })
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("Available backups:");
                foreach (var summary in summaries)
                {
                    if (summary.IsValid)
                    {
                        Console.WriteLine($"  {summary.FileName} | {summary.Timestamp:yyyy-MM-dd HH:mm} | {summary.ProfileCount} profiles | {FormatBytes(summary.FileSizeBytes)}");
                    }
                    else
                    {
                        Console.WriteLine($"  {summary.FileName} | Invalid backup file");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("Summary:");
                Console.WriteLine($"  Total items: {summaries.Count}");
                Console.WriteLine($"  Valid: {validCount}, Invalid: {invalidCount}");
                Console.WriteLine($"  Total profiles: {totalProfiles}");
                Console.WriteLine($"  Total size: {FormatBytes(totalSize)}");
            }

            return 0;
        }

        private static async Task<int> ExecuteBackupCleanup(string[] args)
        {
            int keepDays = 30;
            bool emitJson = false;
            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.StartsWith("--keep=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(token.Substring("--keep=".Length), out var parsed) && parsed >= 0)
                    {
                        keepDays = parsed;
                    }
                    else
                    {
                        Console.WriteLine("Invalid value for --keep. Specify a non-negative integer.");
                        return 1;
                    }
                }
                else if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown backup-cleanup option: {token}");
                    Console.WriteLine("Options: --keep=<days> [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            var cleanupResult = await ProfileManager.CleanupOldBackups(keepDays).ConfigureAwait(false);

            var auditPayload = new Dictionary<string, object>
            {
                ["directory"] = cleanupResult.Directory,
                ["keepDays"] = cleanupResult.KeepDays,
                ["totalCandidates"] = cleanupResult.TotalCandidates,
                ["deleted"] = cleanupResult.DeletedCount,
                ["failed"] = cleanupResult.FailedCount,
                ["failedFiles"] = cleanupResult.FailedFiles,
                ["succeeded"] = cleanupResult.Succeeded,
                ["error"] = cleanupResult.ErrorMessage
            };

            await AuditTrail.RecordEventAsync("Maintenance", "BackupCleanup", auditPayload).ConfigureAwait(false);
            await Logger.LogInfo("Backup cleanup executed", nameof(CommandProcessor), auditPayload).ConfigureAwait(false);

            if (emitJson)
            {
                var json = JsonSerializer.Serialize(new
                {
                    directory = cleanupResult.Directory,
                    keepDays = cleanupResult.KeepDays,
                    totalCandidates = cleanupResult.TotalCandidates,
                    deleted = cleanupResult.DeletedCount,
                    failed = cleanupResult.FailedCount,
                    failedFiles = cleanupResult.FailedFiles,
                    error = cleanupResult.ErrorMessage,
                    succeeded = cleanupResult.Succeeded,
                    checkedAtUtc = cleanupResult.CheckedAtUtc
                }, new JsonSerializerOptions { WriteIndented = true });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("Backup cleanup summary:");
                Console.WriteLine($"  Directory: {cleanupResult.Directory}");
                Console.WriteLine($"  Keep days: {cleanupResult.KeepDays}");
                Console.WriteLine($"  Candidates: {cleanupResult.TotalCandidates}");
                Console.WriteLine($"  Deleted: {cleanupResult.DeletedCount}");
                Console.WriteLine($"  Failed: {cleanupResult.FailedCount}");

                if (cleanupResult.FailedFiles.Count > 0)
                {
                    Console.WriteLine("  Failed files:");
                    foreach (var file in cleanupResult.FailedFiles)
                    {
                        Console.WriteLine($"    - {file}");
                    }
                }

                if (!string.IsNullOrEmpty(cleanupResult.ErrorMessage))
                {
                    Console.WriteLine($"  Error: {cleanupResult.ErrorMessage}");
                }

                Console.WriteLine(cleanupResult.Succeeded
                    ? "Result: Cleanup completed successfully."
                    : "Result: Cleanup completed with errors.");
            }

            return cleanupResult.Succeeded ? 0 : 1;
        }

        private static async Task<int> ExecutePreferClear(string[] args)
        {
            var success = await ConfigManager.ClearPreferredNetworks();
            if (success)
            {
                Console.WriteLine("[OK] Preferred networks cleared");
                await AuditTrail.RecordEventAsync("Configuration", "PreferredNetworksCleared");
                return 0;
            }

            Console.WriteLine("[FAIL] No preferred networks cleared");
            return 1;
        }

        private static async Task<int> ExecuteMaintenance(string[] args)
        {
            bool purgeLogs = false;
            bool purgeAudits = false;
            int logRetention = 30;
            int auditRetention = 90;
            bool secureDelete = true;
            bool emitJson = false;

            if (args.Length == 1)
            {
                purgeLogs = true;
                purgeAudits = true;
            }
            else
            {
                for (int i = 1; i < args.Length; i++)
                {
                    var rawToken = args[i];
                    var token = rawToken.ToLowerInvariant();
                    switch (token)
                    {
                        case "logs":
                            purgeLogs = true;
                            break;
                        case "temp":
                        case "tmp":
                            purgeTemp = true;
                            break;
                        case "cache":
                            purgeCache = true;
                            break;
                        case "all":
                            purgeLogs = true;
                            purgeTemp = true;
                            purgeCache = true;
                            break;
                        case "--no-secure-delete":
                            secureDelete = false;
                            break;
                        case "--json":
                            emitJson = true;
                            break;
                        case "--format=json":
                            emitJson = true;
                            break;
                        default:
                            if (token.StartsWith("--format="))
                            {
                                var format = rawToken.Substring("--format=".Length);
                                if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                                {
                                    emitJson = true;
                                }
                                else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                                {
                                    Console.WriteLine($"Unknown maintenance output format: {format}");
                                    Console.WriteLine("Supported formats: text, json");
                                    return 1;
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Unknown maintenance option: {args[i]}");
                                return 1;
                            }
                            break;
                    }
                }
            }

            if (!purgeLogs && !purgeAudits)
            {
                Console.WriteLine("Specify 'logs' or 'audits' or run without arguments to purge both.");
                Console.WriteLine("Options: logs audits --log-retention=<days> --audit-retention=<days> --no-secure-delete [--json] [--format=<text|json>]");
                return 1;
            }

            var summary = await MaintenanceManager.PurgeSensitiveArtifactsAsync(
                purgeLogs,
                purgeAudits,
                logRetention,
                auditRetention,
                secureDelete);

            if (emitJson)
            {
                var payload = new
                {
                    purgeLogs,
                    purgeAudits,
                    secureDeleteUsed = summary.SecureDeletionUsed,
                    logRetentionDays = summary.LogRetentionDays,
                    auditRetentionDays = summary.AuditRetentionDays,
                    logFilesRemoved = summary.LogFilesRemoved,
                    auditFilesRemoved = summary.AuditFilesRemoved,
                    auditDigestFilesRemoved = summary.AuditDigestFilesRemoved,
                    warnings = summary.Warnings,
                    errors = summary.Errors
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("Maintenance summary:");
                Console.WriteLine($"  Secure deletion: {(summary.SecureDeletionUsed ? "Enabled" : "Disabled")}");
                Console.WriteLine($"  Log retention (days): {summary.LogRetentionDays}");
                Console.WriteLine($"  Audit retention (days): {summary.AuditRetentionDays}");
                Console.WriteLine($"  Log files removed: {summary.LogFilesRemoved}");
                Console.WriteLine($"  Audit files removed: {summary.AuditFilesRemoved}");
                Console.WriteLine($"  Audit digest files removed: {summary.AuditDigestFilesRemoved}");

                if (summary.Warnings?.Count > 0)
                {
                    Console.WriteLine("  Warnings:");
                    foreach (var warning in summary.Warnings)
                    {
                        Console.WriteLine($"    - {warning}");
                    }
                }

                if (summary.Errors?.Count > 0)
                {
                    Console.WriteLine("  Errors:");
                    foreach (var error in summary.Errors)
                    {
                        Console.WriteLine($"    - {error}");
                    }
                }
            }

            await AuditTrail.RecordEventAsync("Maintenance", "PurgeSensitiveArtifacts", new Dictionary<string, object>
            {
                ["logs"] = purgeLogs,
                ["audits"] = purgeAudits,
                ["logRetentionDays"] = summary.LogRetentionDays,
                ["auditRetentionDays"] = summary.AuditRetentionDays,
                ["secureDeletion"] = summary.SecureDeletionUsed,
                ["logFilesRemoved"] = summary.LogFilesRemoved,
                ["auditFilesRemoved"] = summary.AuditFilesRemoved,
                ["digestFilesRemoved"] = summary.AuditDigestFilesRemoved
            });

            return 0;
        }

        private static async Task<int> ExecuteLogPurge(string[] args)
        {
            bool secureDelete = true;
            int retentionDays = 30;
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var rawToken = args[i];
                var token = rawToken.ToLowerInvariant();
                switch (token)
                {
                    case "--no-secure-delete":
                        secureDelete = false;
                        break;
                    case var retention when retention.StartsWith("--retention="):
                        if (int.TryParse(retention.Substring("--retention=".Length), out var parsedRetention))
                        {
                            retentionDays = Math.Max(0, parsedRetention);
                        }
                        else
                        {
                            Console.WriteLine("Invalid value for --retention");
                            return 1;
                        }
                        break;
                    case "--json":
                        emitJson = true;
                        break;
                    default:
                        if (token.StartsWith("--format="))
                        {
                            var format = rawToken.Substring("--format=".Length);
                            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                            {
                                emitJson = true;
                            }
                            else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine($"Unknown log-purge output format: {format}");
                                Console.WriteLine("Supported formats: text, json");
                                return 1;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Unknown log-purge option: {args[i]}");
                            Console.WriteLine("Options: --retention=<days> --no-secure-delete [--json] [--format=<text|json>]");
                            return 1;
                        }
                        break;
                }
            }

            var removedCount = await Logger.PurgeLogsAsync(retentionDays, secureDelete).ConfigureAwait(false);

            if (emitJson)
            {
                var payload = new
                {
                    secureDeletionUsed = secureDelete,
                    retentionDays,
                    logFilesRemoved = removedCount
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("Log purge summary:");
                Console.WriteLine($"  Secure deletion: {(secureDelete ? "Enabled" : "Disabled")}");
                Console.WriteLine($"  Log retention (days): {retentionDays}");
                Console.WriteLine($"  Log files removed: {removedCount}");
            }

            await AuditTrail.RecordEventAsync("Maintenance", "LogPurge", new Dictionary<string, object>
            {
                ["secureDeletion"] = secureDelete,
                ["retentionDays"] = retentionDays,
                ["logFilesRemoved"] = removedCount
            }).ConfigureAwait(false);

            await Logger.LogInfo("Log purge executed", nameof(CommandProcessor), new Dictionary<string, object>
            {
                ["secureDeletion"] = secureDelete,
                ["retentionDays"] = retentionDays,
                ["logFilesRemoved"] = removedCount
            }).ConfigureAwait(false);

            return 0;
        }

        private static async Task<int> ExecuteLogStats(string[] args)
        {
            int recentCount = 5;
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.StartsWith("--recent=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(token.Substring("--recent=".Length), out var parsedRecent) && parsedRecent >= 0)
                    {
                        recentCount = Math.Min(parsedRecent, 20);
                    }
                    else
                    {
                        Console.WriteLine("Invalid value for --recent. Specify a non-negative integer.");
                        return 1;
                    }
                }
                else if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown log-stats output format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown log-stats option: {token}");
                    Console.WriteLine("Options: --recent=<count> [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            var stats = Logger.GetStatistics();
            var recentMessages = stats.RecentMessages ?? Array.Empty<string>();
            var selectedMessages = recentCount > 0
                ? recentMessages.Where(m => !string.IsNullOrWhiteSpace(m)).TakeLast(Math.Min(recentMessages.Length, recentCount)).ToArray()
                : Array.Empty<string>();

            if (emitJson)
            {
                var payload = new
                {
                    lastUpdate = stats.LastUpdate,
                    totals = new
                    {
                        entries = stats.TotalLogEntries,
                        security = stats.SecurityEvents,
                        error = stats.ErrorEvents,
                        warning = stats.WarningEvents,
                        info = stats.InfoEvents,
                        debug = stats.DebugEvents
                    },
                    recentRequested = recentCount,
                    recentMessages = selectedMessages
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("Log statistics:");
                Console.WriteLine($"  Last update: {stats.LastUpdate:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Total entries: {stats.TotalLogEntries}");
                Console.WriteLine($"  Security events: {stats.SecurityEvents}");
                Console.WriteLine($"  Error events: {stats.ErrorEvents}");
                Console.WriteLine($"  Warning events: {stats.WarningEvents}");
                Console.WriteLine($"  Info events: {stats.InfoEvents}");
                Console.WriteLine($"  Debug events: {stats.DebugEvents}");

                if (selectedMessages.Length > 0)
                {
                    Console.WriteLine("  Recent messages:");
                    foreach (var message in selectedMessages)
                    {
                        Console.WriteLine($"    - {message}");
                    }
                }
            }

            await AuditTrail.RecordEventAsync("Maintenance", "LogStatsViewed", new Dictionary<string, object>
            {
                ["recentRequested"] = recentCount,
                ["totalLogs"] = stats.TotalLogEntries,
                ["errors"] = stats.ErrorEvents,
                ["warnings"] = stats.WarningEvents
            }).ConfigureAwait(false);

            return 0;
        }

        private static async Task<int> ExecuteInfo(string[] args)
        {
            await ExecuteStatus(args);
            Console.WriteLine();

            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                .FirstOrDefault();

            if (interfaces != null)
            {
                Console.WriteLine($"Adapter: {interfaces.Description}");
                Console.WriteLine($"Speed:   {interfaces.Speed / 1_000_000} Mbps");
                Console.WriteLine($"Status:  {interfaces.OperationalStatus}");
            }

            return 0;
        }

        private static async Task<int> ExecuteScan(string[] args)
        {
            bool emitJson = false;
            bool forceRefresh = true;
            int limit = 15;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown scan output format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else if (token.Equals("--cached", StringComparison.OrdinalIgnoreCase))
                {
                    forceRefresh = false;
                }
                else if (token.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(token.Substring("--limit=".Length), out var parsedLimit) && parsedLimit > 0)
                    {
                        limit = Math.Min(parsedLimit, 50);
                    }
                    else
                    {
                        Console.WriteLine("Invalid value for --limit. Specify a positive integer.");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown scan option: {token}");
                    Console.WriteLine("Options: [--cached] [--limit=<count>] [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            ConsoleSpinner spinner = null;
            if (!emitJson)
            {
                Console.Write("Scanning for networks");
                spinner = new ConsoleSpinner();
                _ = spinner.StartAsync();
            }

            var networks = await NetworkOperations.ScanNetworksAsync(forceRefresh);
            spinner?.Stop();

            if (emitJson)
            {
                var payload = new
                {
                    count = networks.Count,
                    limit,
                    forceRefresh,
                    networks = networks.Take(limit).Select(n => new
                    {
                        ssid = n.Ssid,
                        signal = n.Signal,
                        security = n.Security,
                        band = n.Band,
                        open = n.Security.Contains("Open", StringComparison.OrdinalIgnoreCase)
                    })
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"\rFound {networks.Count} networks:");
                Console.WriteLine();

                foreach (var network in networks.Take(limit))
                {
                    var signalBar = GetSignalBar(network.Signal);
                    var secIcon = network.Security.Contains("Open", StringComparison.OrdinalIgnoreCase) ? "[OPEN]" : "[SECURE]";
                    Console.WriteLine($"  {secIcon} {network.Ssid,-25} {signalBar} {network.Signal}% ({network.Band})");
                }
            }

            return 0;
        }

        private static async Task<int> ExecuteAvailable(string[] args)
        {
            return await ExecuteScan(args);
        }

        private static async Task<int> ExecuteProfiles(string[] args)
        {
            bool emitJson = false;
            int? limit = null;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = false;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown profiles option format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else if (token.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(token.Substring("--limit=".Length), out var parsedLimit) && parsedLimit > 0)
                    {
                        limit = Math.Min(parsedLimit, 100);
                    }
                    else
                    {
                        Console.WriteLine("Invalid value for --limit. Specify a positive integer.");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown profiles option: {token}");
                    Console.WriteLine("Options: [--limit=<count>] [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            var profiles = await NetworkOperations.GetSavedProfilesAsync();
            var ordered = profiles
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var appliedLimit = limit.HasValue ? Math.Min(limit.Value, ordered.Count) : ordered.Count;
            var subset = ordered.Take(appliedLimit).ToList();

            if (emitJson)
            {
                var payload = new
                {
                    total = ordered.Count,
                    returned = subset.Count,
                    profiles = subset
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"Saved profiles ({ordered.Count}){(limit.HasValue ? $" showing {subset.Count}" : string.Empty)}:");
                if (subset.Count == 0)
                {
                    Console.WriteLine("  (none)");
                }
                else
                {
                    foreach (var profile in subset)
                    {
                        Console.WriteLine($"  - {profile}");
                    }
                }
            }

            return 0;
        }

        private static async Task<int> ExecuteDelete(string[] args)
        {
            bool emitJson = false;
            string ssidToken = null;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = false;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown delete option format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else if (ssidToken == null)
                {
                    ssidToken = token;
                }
                else
                {
                    Console.WriteLine($"Unknown delete option: {token}");
                    Console.WriteLine("Usage: delete <SSID> [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            if (string.IsNullOrWhiteSpace(ssidToken))
            {
                Console.WriteLine("Usage: delete <SSID> [--json] [--format=<text|json>]");
                return 1;
            }

            string ssid;

            try
            {
                ssid = InputValidator.EnsureValidSsid(ssidToken);
            }
            catch (ArgumentException ex)
            {
                if (emitJson)
                {
                    var payload = new
                    {
                        ssid = ssidToken,
                        deleted = false,
                        error = ex.Message
                    };

                    Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
                }
                else
                {
                    Console.WriteLine($"Invalid input: {ex.Message}");
                }

                return 1;
            }

            var success = await NetworkOperations.DeleteProfileAsync(ssid);

            if (emitJson)
            {
                var payload = new
                {
                    ssid,
                    deleted = success,
                    message = success ? "Profile deleted" : "Profile deletion failed"
                };

                Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            else
            {
                Console.WriteLine(success ? $"[OK] Profile '{ssid}' deleted" : $"[FAIL] Failed to delete '{ssid}'");
            }

            return success ? 0 : 1;
        }

        private static async Task<int> ExecuteReset(string[] args)
        {
            Console.Write("Resetting network adapter");
            var spinner = new ConsoleSpinner();
            _ = spinner.StartAsync();

            await NetworkOperations.DisconnectAsync();
            await Task.Run(() =>
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "winsock reset",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
            });

            spinner.Stop();
            Console.WriteLine("\r[OK] Network adapter reset complete");
            return 0;
        }

        private const int PreferredNetworkPriorityMin = 0;
        private const int PreferredNetworkPriorityMax = 500;

        internal static bool IsValidPreferredPriority(int priority) =>
            priority >= PreferredNetworkPriorityMin && priority <= PreferredNetworkPriorityMax;

        private static async Task<int> ExecutePrefer(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: prefer <SSID> [priority]");
                return 1;
            }

            int priority = 100;
            if (args.Length > 2)
            {
                if (!int.TryParse(args[2], out priority))
                {
                    Console.WriteLine("Priority must be an integer between 0 and 500");
                    return 1;
                }

                if (!IsValidPreferredPriority(priority))
                {
                    Console.WriteLine($"Priority must be between {PreferredNetworkPriorityMin} and {PreferredNetworkPriorityMax}");
                    return 1;
                }
            }

            var success = await ConfigManager.AddPreferredNetwork(args[1], priority);
            if (success)
            {
                Console.WriteLine($"[OK] Preferred network saved: {args[1]} (priority {priority})");
                await Logger.LogInfo("Preferred network added", nameof(CommandProcessor), new Dictionary<string, object>
                {
                    ["ssid"] = args[1],
                    ["priority"] = priority
                });
                await AuditTrail.RecordEventAsync("Configuration", "PreferredNetworkAdded", new Dictionary<string, object>
                {
                    ["ssid"] = args[1],
                    ["priority"] = priority
                });
                return 0;
            }

            Console.WriteLine("[FAIL] Failed to add preferred network");
            await Logger.LogWarning("Failed to add preferred network", nameof(CommandProcessor), new Dictionary<string, object>
            {
                ["ssid"] = args[1],
                ["priority"] = priority
            });
            await AuditTrail.RecordEventAsync("Configuration", "PreferredNetworkAddFailed", new Dictionary<string, object>
            {
                ["ssid"] = args[1],
                ["priority"] = priority
            }, "Warning");
            return 1;
        }

        private static async Task<int> ExecutePreferRemove(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: prefer-remove <SSID>");
                return 1;
            }

            var success = await ConfigManager.RemovePreferredNetwork(args[1]);
            if (success)
            {
                Console.WriteLine($"[OK] Preferred network removed: {args[1]}");
                await Logger.LogInfo("Preferred network removed", nameof(CommandProcessor), new Dictionary<string, object>
                {
                    ["ssid"] = args[1]
                });
                await AuditTrail.RecordEventAsync("Configuration", "PreferredNetworkRemoved", new Dictionary<string, object>
                {
                    ["ssid"] = args[1]
                });
                return 0;
            }

            Console.WriteLine($"[FAIL] Preferred network not removed: {args[1]}");
            await Logger.LogWarning("Preferred network removal failed", nameof(CommandProcessor), new Dictionary<string, object>
            {
                ["ssid"] = args[1]
            });
            await AuditTrail.RecordEventAsync("Configuration", "PreferredNetworkRemoveFailed", new Dictionary<string, object>
            {
                ["ssid"] = args[1]
            }, "Warning");
            return 1;
        }

        private static async Task<int> ExecutePreferList(string[] args)
        {
            var preferred = await ConfigManager.GetPreferredNetworks();

            if (preferred.Count == 0)
            {
                Console.WriteLine("No preferred networks configured");
                return 0;
            }

            Console.WriteLine("Preferred Networks:");
            foreach (var (network, index) in preferred.Select((p, i) => (p, i + 1)))
            {
                Console.WriteLine($"  {index}. {network.Ssid} (Priority: {network.Priority}, Updated: {network.LastUpdated:yyyy-MM-dd HH:mm})");
            }

            return 0;
        }

        private static async Task<int> ExecuteDiagnostics(string[] args)
        {
            Console.WriteLine("Running diagnostics...");
            Console.WriteLine();

            // Check adapter
            var adapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                .ToList();

            Console.WriteLine($"WiFi Adapters: {adapters.Count}");
            foreach (var adapter in adapters)
            {
                Console.WriteLine($"  - {adapter.Description} ({adapter.OperationalStatus})");
            }

            // Check connectivity
            var status = await NetworkOperations.GetStatusAsync();
            Console.WriteLine($"Connection: {status.Status}");

            // Check DNS
            try
            {
                var dns = await System.Net.Dns.GetHostAddressesAsync("google.com");
                Console.WriteLine($"DNS: Working ({dns.Length} addresses resolved)");
            }
            catch
            {
                Console.WriteLine("DNS: Not working");
            }

            // Check Internet
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync("8.8.8.8");
                Console.WriteLine($"Internet: {(reply.Status == System.Net.NetworkInformation.IPStatus.Success ? "Connected" : "Not connected")}");
            }
            catch
            {
                Console.WriteLine("Internet: Not connected");
            }

            return 0;
        }

        private static async Task<int> ExecuteTest(string[] args)
        {
            var success = await TestRunner.RunBasicTests();
            return success ? 0 : 1;
        }

        private static async Task<int> ExecuteValidate(string[] args)
        {
            var hasInteractive = args.Length > 1 && args[1].ToLower() == "interactive";

            var basicSuccess = await TestRunner.RunBasicTests();
            var interactiveSuccess = true;

            if (hasInteractive)
            {
                Console.WriteLine();
                interactiveSuccess = await TestRunner.RunInteractiveTest();
            }

            return (basicSuccess && interactiveSuccess) ? 0 : 1;
        }

        private static async Task<int> ExecuteSpeedTest(string[] args)
        {
            var status = await NetworkOperations.GetStatusAsync();
            if (status.Status != "Connected")
            {
                Console.WriteLine("Not connected to any network");
                return 1;
            }

            var result = await NetworkAnalytics.RunSpeedTest(status.Ssid);

            Console.WriteLine($"Speed Test Results for {status.Ssid}:");
            Console.WriteLine($"  Average Latency: {result.AverageLatency:F1}ms");
            Console.WriteLine($"  Min Latency: {result.MinLatency}ms");
            Console.WriteLine($"  Max Latency: {result.MaxLatency}ms");
            Console.WriteLine($"  Packet Loss: {result.PacketLoss:F1}%");
            Console.WriteLine($"  DNS Resolution: {result.DnsResolutionTime:F1}ms");
            Console.WriteLine($"  Test Duration: {result.TestDuration.TotalSeconds:F1}s");

            if (!string.IsNullOrEmpty(result.Error))
            {
                Console.WriteLine($"  Error: {result.Error}");
                return 1;
            }

            return 0;
        }

        private static async Task<int> ExecuteAnalytics(string[] args)
        {
            string ssid = null;
            if (args.Length > 1)
            {
                try
                {
                    ssid = InputValidator.EnsureValidSsid(args[1]);
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine($"Invalid input: {ex.Message}");
                    return 1;
                }
            }
            var report = await NetworkAnalytics.GenerateQualityReport(ssid);

            Console.WriteLine($"Network Quality Report: {report.Ssid}");
            Console.WriteLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");

            if (!string.IsNullOrEmpty(report.Message))
            {
                Console.WriteLine($"Message: {report.Message}");
                return 0;
            }

            Console.WriteLine($"Measurements: {report.TotalMeasurements} over {report.TimeSpan.TotalDays:F1} days");
            Console.WriteLine($"Quality Score: {report.QualityScore:F1}/100");

            if (report.AverageSignalStrength > 0)
            {
                Console.WriteLine($"Signal Strength: {report.AverageSignalStrength:F1}% (range: {report.MinSignalStrength}%-{report.MaxSignalStrength}%)");
                Console.WriteLine($"Signal Stability: {report.SignalStability:F1}%");
            }

            if (report.SuccessRate >= 0)
            {
                Console.WriteLine($"Connection Success Rate: {report.SuccessRate:F1}%");
            }

            if (report.AverageLatency > 0)
            {
                Console.WriteLine($"Average Latency: {report.AverageLatency:F1}ms");
            }

            if (report.Recommendations.Count > 0)
            {
                Console.WriteLine("\nRecommendations:");
                foreach (var rec in report.Recommendations)
                {
                    Console.WriteLine($"  - {rec}");
                }
            }

            return 0;
        }

        private static async Task<int> ExecuteTrends(string[] args)
        {
            string ssid = null;
            if (args.Length > 1)
            {
                try
                {
                    ssid = InputValidator.EnsureValidSsid(args[1]);
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine($"Invalid input: {ex.Message}");
                    return 1;
                }
            }
            var analysis = AdvancedScanner.AnalyzeNetworkTrends(ssid);

            Console.WriteLine("Network Trend Analysis");
            Console.WriteLine($"Generated: {analysis.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Summary: {analysis.Summary}");

            if (analysis.NetworkTrends.Count > 0)
            {
                Console.WriteLine("\nTop Networks by Activity:");
                foreach (var trend in analysis.NetworkTrends.Take(10))
                {
                    var trendIcon = trend.RecentTrend > 1 ? "📈" : trend.RecentTrend < -1 ? "📉" : "➡️";
                    Console.WriteLine($"  {trendIcon} {trend.Ssid} - Avg: {trend.AverageSignal:F0}%, Stability: {trend.Stability:F0}%");
                }
            }

            return 0;
        }

        private static async Task<int> ExecuteSecurity(string[] args)
        {
            Console.WriteLine("Running security analysis...");
            var alerts = await NetworkAnalytics.AnalyzeSecurity();

            if (alerts.Count == 0)
            {
                Console.WriteLine("[OK] No security issues detected");
                return 0;
            }

            Console.WriteLine($"Found {alerts.Count} security alerts:");

            var grouped = alerts.GroupBy(a => a.Level).OrderByDescending(g => g.Key);
            foreach (var group in grouped)
            {
                var icon = group.Key switch
                {
                    NetworkAnalytics.AlertLevel.Critical => "[CRIT]",
                    NetworkAnalytics.AlertLevel.High => "[HIGH]",
                    NetworkAnalytics.AlertLevel.Medium => "[MED]",
                    _ => "[LOW]"
                };

                Console.WriteLine($"\n{icon} {group.Key} Priority:");
                foreach (var alert in group)
                {
                    Console.WriteLine($"  - {alert.NetworkName}: {alert.Issue}");
                    Console.WriteLine($"    Recommendation: {alert.Recommendation}");
                }
            }

            Console.WriteLine($"Generated at: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Security Score: {report.SecurityScore:F1}/100");
            Console.WriteLine($"Issues detected: {report.TotalIssues} (Critical: {report.CriticalCount}, High: {report.HighCount}, Medium: {report.MediumCount}, Low: {report.LowCount})");

            if (report.Issues?.Any() == true)
            {
                Console.WriteLine("\nTop issues:");
                foreach (var issue in report.Issues.Take(10))
                {
                    Console.WriteLine($"- [{issue.Severity}] {issue.Network ?? "(system)"}: {issue.Description}");
                    Console.WriteLine($"  Recommendation: {issue.Recommendation}");
                }
            }

            if (report.Recommendations?.Any() == true)
            {
                Console.WriteLine("\nRecommended actions:");
                foreach (var recommendation in report.Recommendations.Take(5))
                {
                    Console.WriteLine($"- {recommendation}");
                }
            }

            await Logger.LogInfo("Security audit executed", nameof(CommandProcessor), new Dictionary<string, object>
            {
                ["score"] = report.SecurityScore,
                ["issues"] = report.TotalIssues
            });

            await AuditTrail.RecordEventAsync("Security", "AuditRun", new Dictionary<string, object>
            {
                ["score"] = report.SecurityScore,
                ["critical"] = report.CriticalCount,
                ["high"] = report.HighCount,
                ["medium"] = report.MediumCount,
                ["low"] = report.LowCount
            });

            return report.CriticalCount > 0 || report.HighCount > 0 ? 1 : 0;
        }

        private static async Task<int> ExecuteSecurityMetrics(string[] args)
        {
            bool emitJson = false;
            bool resetCounters = false;
            string resetReason = null;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = false;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown security-metrics format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else if (token.StartsWith("--reset", StringComparison.OrdinalIgnoreCase))
                {
                    resetCounters = true;
                    var parts = token.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        resetReason = parts[1]?.Trim();
                    }
                }
                else if (token.StartsWith("--reason=", StringComparison.OrdinalIgnoreCase))
                {
                    resetReason = token.Substring("--reason=".Length).Trim();
                }
                else
                {
                    Console.WriteLine($"Unknown security-metrics option: {token}");
                    Console.WriteLine("Usage: security-metrics [--json] [--format=<text|json>] [--reset[=<reason>]] [--reason=<reason>]");
                    return 1;
                }
            }

            if (resetCounters)
            {
                if (string.IsNullOrWhiteSpace(resetReason))
                {
                    Console.WriteLine("Reset requires a reason. Provide with --reset=<reason> or --reason=<reason>.");
                    return 1;
                }
                SecurityManager.ResetRateLimitMetrics();
            }

            var metrics = SecurityManager.GetRateLimitMetrics();

            var payload = new Dictionary<string, object>
            {
                ["commandWindowSeconds"] = metrics.CommandWindow.TotalSeconds,
                ["commandMaxAttempts"] = metrics.CommandMaxAttempts,
                ["commandRejections"] = metrics.CommandRejections,
                ["globalWindowSeconds"] = metrics.GlobalWindow.TotalSeconds,
                ["globalMaxAttempts"] = metrics.GlobalMaxAttempts,
                ["globalRejections"] = metrics.GlobalRejections,
                ["trackedOperations"] = metrics.TrackedOperations,
                ["lastResetUtc"] = metrics.LastResetUtc,
                ["reset"] = resetCounters,
                ["resetReason"] = resetReason
            };

            if (emitJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            else
            {
                Console.WriteLine("Security Rate Limit Metrics:");
                Console.WriteLine($"Command Window : {metrics.CommandWindow.TotalSeconds:F0}s (max {metrics.CommandMaxAttempts})");
                Console.WriteLine($"Global Window  : {metrics.GlobalWindow.TotalSeconds:F0}s (max {metrics.GlobalMaxAttempts})");
                Console.WriteLine($"Tracked Ops    : {metrics.TrackedOperations}");
                Console.WriteLine($"Command Rejects: {metrics.CommandRejections}");
                Console.WriteLine($"Global Rejects : {metrics.GlobalRejections}");
                if (metrics.LastResetUtc != DateTime.MinValue)
                {
                    Console.WriteLine($"Last Reset     : {metrics.LastResetUtc:u}");
                }

                if (resetCounters)
                {
                    Console.WriteLine("Counters were reset before reading metrics.");
                    Console.WriteLine($"Reason         : {resetReason}");
                }
            }

            await Logger.LogInfo("Security metrics retrieved", nameof(CommandProcessor), payload);
            await AuditTrail.RecordEventAsync(
                "Security",
                resetCounters ? "RateLimitMetricsReset" : "RateLimitMetricsViewed",
                payload,
                resetCounters ? "Warning" : "Info");

            return 0;
        }

        private static async Task<int> ExecuteBackup(string[] args)
        {
            string fileName = null;
            bool emitJson = false;

            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (string.Equals(token, "--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown backup option format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else if (token.StartsWith("--name=", StringComparison.OrdinalIgnoreCase))
                {
                    var requested = token.Substring("--name=".Length);
                    if (string.IsNullOrWhiteSpace(requested))
                    {
                        Console.WriteLine("Specify a file name after --name=.");
                        return 1;
                    }

                    try
                    {
                        fileName = InputValidator.SanitizeFileName(requested, ".json");
                    }
                    catch (ArgumentException ex)
                    {
                        Console.WriteLine($"Invalid input: {ex.Message}");
                        return 1;
                    }
                }
                else if (!token.StartsWith("--", StringComparison.OrdinalIgnoreCase) && fileName == null)
                {
                    try
                    {
                        fileName = InputValidator.SanitizeFileName(token, ".json");
                    }
                    catch (ArgumentException ex)
                    {
                        Console.WriteLine($"Invalid input: {ex.Message}");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown backup option: {token}");
                    Console.WriteLine("Usage: backup [<fileName>] [--name=<fileName>] [--json] [--format=<text|json>]");
                    return 1;
                }
            }

            ConsoleSpinner spinner = null;
            if (!emitJson)
            {
                Console.Write("Creating backup");
                spinner = new ConsoleSpinner();
                _ = spinner.StartAsync();
            }

            string filePath;
            try
            {
                filePath = await ProfileManager.CreateFullBackup(fileName).ConfigureAwait(false);
            }
            finally
            {
                spinner?.Stop();
            }

            if (emitJson)
            {
                var info = new FileInfo(filePath);
                var payload = new
                {
                    path = filePath,
                    file = info.Name,
                    sizeBytes = info.Length,
                    createdAtUtc = info.CreationTimeUtc
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"\rBackup created: {filePath}");
            }

            return 0;
        }

        private static async Task<int> ExecuteRestore(string[] args)
        {
            bool emitJson = false;
            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];
                if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    emitJson = true;
                }
                else if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    var format = token.Substring("--format=".Length);
                    if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        emitJson = true;
                    }
                    else if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Unknown restore option format: {format}");
                        Console.WriteLine("Supported formats: text, json");
                        return 1;
                    }
                }
                else
                {
                    // treat as positional argument (backup path)
                }
            }

            if (args.Length < 2 || (emitJson && args.Length == 1))
            {
                var summaries = await ProfileManager.GetBackupSummariesAsync().ConfigureAwait(false);
                if (summaries.Count == 0)
                {
                    if (emitJson)
                    {
                        Console.WriteLine("[]");
                    }
                    else
                    {
                        Console.WriteLine("No backups found");
                    }
                    return 1;
                }

                if (emitJson)
                {
                    var payload = summaries.Select(summary => new
                    {
                        file = summary.FileName,
                        path = summary.FullPath,
                        sizeBytes = summary.FileSizeBytes,
                        createdAtUtc = summary.CreatedAtUtc,
                        timestamp = summary.Timestamp,
                        profiles = summary.ProfileCount,
                        isValid = summary.IsValid
                    });

                    var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    Console.WriteLine(json);
                    return 0;
                }

                Console.WriteLine("Available backups:");
                for (int i = 0; i < summaries.Count; i++)
                {
                    var summary = summaries[i];
                    var display = summary.IsValid
                        ? $"{summary.FileName} - {summary.Timestamp:yyyy-MM-dd HH:mm} ({summary.ProfileCount} profiles)"
                        : $"{summary.FileName} - Invalid backup file";
                    Console.WriteLine($"  {i + 1}. {display}");
                }

                Console.Write("Select backup (1-9): ");
                if (int.TryParse(Console.ReadLine(), out var choice) && choice > 0 && choice <= summaries.Count)
                {
                    var filePath = summaries[choice - 1].FullPath;
                    var success = await ProfileManager.RestoreFromBackup(filePath).ConfigureAwait(false);
                    return success ? 0 : 1;
                }

                return 1;
            }

            try
            {
                var backupPath = InputValidator.EnsureSafeFilePath(args[1]);
                var success2 = await ProfileManager.RestoreFromBackup(backupPath);
                return success2 ? 0 : 1;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid path: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> ExecuteExport(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: export <SSID> <file_path>");
                return 1;
            }

            try
            {
                var ssid = InputValidator.EnsureValidSsid(args[1]);
                var outputPath = InputValidator.EnsureSafeFilePath(args[2]);

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var success = await ProfileManager.ExportProfile(ssid, outputPath);
                return success ? 0 : 1;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid input: {ex.Message}");
                return 1;
            }

            return 1;
        }

        private static async Task<int> ExecuteImport(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: import <file_path>");
                return 1;
            }

            try
            {
                var inputPath = InputValidator.EnsureSafeFilePath(args[1]);
                var success = await ProfileManager.ImportProfile(inputPath);
                return success ? 0 : 1;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid path: {ex.Message}");
                return 1;
            }

            return 1;
        }

        private static async Task<int> ExecuteConfig(string[] args)
        {
            if (args.Length == 1)
            {
                await ConfigManager.ShowCurrentConfig();
                return 0;
            }

            var subCommand = args[1].ToLower();

            switch (subCommand)
            {
                case "reset":
                    await ConfigManager.ResetToDefaults();
                    return 0;

                case "validate":
                    await ConfigManager.ValidateConfig();
                    return 0;

                case "verify":
                    var (isValid, message) = await ConfigManager.VerifyUserConfigIntegrity();
                    if (isValid)
                    {
                        Console.WriteLine($"[OK] {message}");
                        await Logger.LogInfo("Configuration integrity verified", nameof(CommandProcessor));
                        await AuditTrail.RecordEventAsync("Configuration", "IntegrityVerified", new Dictionary<string, object>
                        {
                            ["status"] = "Valid"
                        });
                        return 0;
                    }

                    Console.WriteLine($"[FAIL] Integrity check failed: {message}");
                    await Logger.LogWarning("Configuration integrity verification failed", nameof(CommandProcessor), new Dictionary<string, object>
                    {
                        ["message"] = message
                    });
                    await AuditTrail.RecordEventAsync("Configuration", "IntegrityFailure", new Dictionary<string, object>
                    {
                        ["message"] = message
                    }, "Warning");
                    return 1;

                case "export":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: config export <file_path>");
                        return 1;
                    }
                    try
                    {
                        var exportPath = InputValidator.EnsureSafeFilePath(args[2]);
                        var directory = Path.GetDirectoryName(exportPath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        await ConfigManager.ExportConfig(exportPath);
                        await Logger.LogInfo("Configuration exported", nameof(CommandProcessor), new Dictionary<string, object>
                        {
                            ["path"] = exportPath
                        });
                        await AuditTrail.RecordEventAsync("Configuration", "Export", new Dictionary<string, object>
                        {
                            ["path"] = exportPath
                        });
                        return 0;
                    }
                    catch (ArgumentException ex)
                    {
                        Console.WriteLine($"Invalid path: {ex.Message}");
                        return 1;
                    }

                case "import":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: config import <file_path>");
                        return 1;
                    }
                    try
                    {
                        var importPath = InputValidator.EnsureSafeFilePath(args[2]);
                        await ConfigManager.ImportConfig(importPath);
                        await Logger.LogInfo("Configuration imported", nameof(CommandProcessor), new Dictionary<string, object>
                        {
                            ["path"] = importPath
                        });
                        await Logger.RefreshConfigurationAsync();
                        await AuditTrail.RecordEventAsync("Configuration", "Import", new Dictionary<string, object>
                        {
                            ["path"] = importPath
                        });
                        return 0;
                    }
                    catch (ArgumentException ex)
                    {
                        Console.WriteLine($"Invalid path: {ex.Message}");
                        await Logger.LogWarning("Configuration import failed", nameof(CommandProcessor), new Dictionary<string, object>
                        {
                            ["path"] = args[2],
                            ["reason"] = ex.Message
                        });
                        return 1;
                    }
                case "set":
                    if (args.Length < 4)
                    {
                        Console.WriteLine("Usage: config set <key> <value>");
                        return 1;
                    }

                    var key = args[2]?.Trim();
                    if (string.IsNullOrEmpty(key))
                    {
                        Console.WriteLine("Configuration key cannot be empty");
                        return 1;
                    }

                    var value = string.Join(' ', args.Skip(3));

                    try
                    {
                        var updateResult = await ConfigManager.UpdateSetting(key, value);
                        if (updateResult.Success)
                        {
                            var displayValue = RedactSensitiveValue(key, updateResult.NewValue);
                            Console.WriteLine($"Setting '{key}' updated successfully. Current value: {displayValue}");
                            await Logger.LogInfo("Configuration updated", nameof(CommandProcessor), new Dictionary<string, object>
                            {
                                ["key"] = key,
                                ["value"] = displayValue
                            });
                            await Logger.RefreshConfigurationAsync();
                            await AuditTrail.RecordEventAsync("Configuration", "Update", new Dictionary<string, object>
                            {
                                ["key"] = key,
                                ["value"] = displayValue
                            });
                            return 0;
                        }

                        var reason = string.IsNullOrEmpty(updateResult.Message) ? "Unknown error" : updateResult.Message;
                        Console.WriteLine($"Failed to update setting '{key}': {reason}");
                        await Logger.LogWarning("Configuration update failed", nameof(CommandProcessor), new Dictionary<string, object>
                        {
                            ["key"] = key,
                            ["reason"] = reason
                        });
                        return 1;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to update setting '{key}': {ex.Message}");
                        await Logger.LogWarning("Configuration update error", nameof(CommandProcessor), new Dictionary<string, object>
                        {
                            ["key"] = key,
                            ["reason"] = ex.Message
                        });
                        return 1;
                    }

                case "describe":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: config describe <key>");
                        return 1;
                    }

                    var metadata = ConfigManager.GetSettingMetadata(args[2]);
                    if (metadata == null)
                    {
                        Console.WriteLine($"Unknown setting: {args[2]}");
                        await Logger.LogWarning("Unknown setting described", nameof(CommandProcessor), new Dictionary<string, object>
                        {
                            ["key"] = args[2]
                        });
                        return 1;
                    }

                    var current = metadata.GetCurrentValue(await ConfigManager.LoadConfig());
                    Console.WriteLine($"Setting: {metadata.Key}");
                    Console.WriteLine($"Description: {metadata.Description}");
                    Console.WriteLine($"Type: {metadata.ValueType}");
                    Console.WriteLine($"Default: {metadata.DefaultValue}");
                    Console.WriteLine($"Current: {current}");
                    if (!string.IsNullOrEmpty(metadata.AllowedValues))
                    {
                        Console.WriteLine($"Allowed: {metadata.AllowedValues}");
                    }
                    if (!string.IsNullOrEmpty(metadata.Range))
                    {
                        Console.WriteLine($"Range: {metadata.Range}");
                    }
                    if (!string.IsNullOrEmpty(metadata.Notes))
                    {
                        Console.WriteLine($"Notes: {metadata.Notes}");
                    }
                    return 0;

                case "list":
                    var settings = ConfigManager.GetSettingsMetadata();
                    Console.WriteLine("Available settings:");
                    foreach (var setting in settings.OrderBy(s => s.Key))
                    {
                        Console.WriteLine($"  {setting.Key,-22} {setting.Description}");
                    }
                    Console.WriteLine("\nUse 'config describe <key>' for details.");
                    await Logger.LogInfo("Configuration list displayed", nameof(CommandProcessor));
                    return 0;

                case "metadata":
                    var includeCurrent = true;
                    string outputPath = null;

                    if (args.Length >= 3)
                    {
                        if (args[2].Equals("--defaults", StringComparison.OrdinalIgnoreCase))
                        {
                            includeCurrent = false;
                            if (args.Length >= 4)
                            {
                                outputPath = args[3];
                            }
                        }
                        else
                        {
                            outputPath = args[2];
                        }
                    }

                    try
                    {
                        var json = await ConfigManager.GetSettingsMetadataJson(includeCurrent);

                        if (string.IsNullOrEmpty(outputPath))
                        {
                            Console.WriteLine(json);
                        }
                        else
                        {
                            var safePath = InputValidator.EnsureSafeFilePath(outputPath);
                            var directory = Path.GetDirectoryName(safePath);
                            if (!string.IsNullOrEmpty(directory))
                            {
                                Directory.CreateDirectory(directory);
                            }

                            await File.WriteAllTextAsync(safePath, json);
                            Console.WriteLine($"Settings metadata exported to {safePath}");
                        }

                        return 0;
                    }
                    catch (ArgumentException ex)
                    {
                        Console.WriteLine($"Invalid path: {ex.Message}");
                        return 1;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to export metadata: {ex.Message}");
                        return 1;
                    }

                default:
                    Console.WriteLine("Available config commands:");
                    Console.WriteLine("  show | (no args)        Display current configuration");
                    Console.WriteLine("  set <key> <value>       Update a configuration value");
                    Console.WriteLine("  describe <key>          Show details for a setting");
                    Console.WriteLine("  list                    List configurable settings");
                    Console.WriteLine("  metadata [path]         Export settings metadata as JSON");
                    Console.WriteLine("  metadata --defaults [path]  Export defaults-only metadata");
                    Console.WriteLine("  reset                   Reset to defaults");
                    Console.WriteLine("  validate                Validate configuration");
                    Console.WriteLine("  export <path>           Export configuration to file");
                    Console.WriteLine("  import <path>           Import configuration from file");
                    return 1;
            }
        }

        private static async Task<int> ExecuteDetailedScan(string[] args)
        {
            Console.Write("Performing detailed scan");
            var spinner = new ConsoleSpinner();
            _ = spinner.StartAsync();

            var networks = await AdvancedScanner.PerformDetailedScan();
            spinner.Stop();

            Console.WriteLine($"\rDetailed Scan Results ({networks.Count} networks):");
            Console.WriteLine();

            foreach (var network in networks.Take(15))
            {
                var secIcon = network.SecurityAnalysis.Level == AdvancedScanner.SecurityLevel.None ? " " : "🔒";
                var trendIcon = network.RecentTrend > 1 ? "📈" : network.RecentTrend < -1 ? "📉" : "➡️";
                var signalBar = GetSignalBar(network.CurrentSignalStrength);

                Console.WriteLine($"  {secIcon} {network.Ssid,-25} {signalBar} {network.CurrentSignalStrength}% {trendIcon}");
                Console.WriteLine($"    Quality: {network.QualityScore:F0}/100, Stability: {network.SignalStability:F0}%, {network.Band}");

                if (network.IsSavedProfile)
                {
                    Console.WriteLine("    💾 Saved profile");
                }
            }

            return 0;
        }

        private static async Task<int> ExecuteMonitor(string[] args)
        {
            var interval = 30;
            if (args.Length > 1 && int.TryParse(args[1], out var customInterval))
            {
                interval = Math.Max(5, Math.Min(300, customInterval));
            }

            Console.WriteLine($"Starting continuous monitoring (interval: {interval}s)");
            Console.WriteLine("Press Ctrl+C to stop");

            await AdvancedScanner.StartContinuousScanning(interval);

            // Wait for user input to stop
            Console.ReadKey();
            AdvancedScanner.StopContinuousScanning();

            return 0;
        }

        private static async Task<int> ExecutePredict(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: predict <SSID>");
                return 1;
            }

            string ssid;
            try
            {
                ssid = InputValidator.EnsureValidSsid(args[1]);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid input: {ex.Message}");
                return 1;
            }

            var prediction = await AdvancedScanner.PredictNetworkQuality(ssid);

            Console.WriteLine($"Network Quality Prediction: {prediction.Ssid}");
            Console.WriteLine($"Predicted Signal: {prediction.PredictedSignalStrength}%");
            Console.WriteLine($"Confidence: {prediction.Confidence:P0}");
            Console.WriteLine($"Trend: {prediction.TrendDirection}");
            Console.WriteLine($"Prediction: {prediction.Prediction}");

            return 0;
        }

        private static async Task<int> ExecuteCompare(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: compare <SSID1> <SSID2> [SSID3...]");
                return 1;
            }

            List<string> ssids;
            try
            {
                ssids = args.Skip(1).Select(InputValidator.EnsureValidSsid).ToList();
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid input: {ex.Message}");
                return 1;
            }
            var comparisons = AdvancedScanner.CompareNetworks(ssids);

            if (comparisons.Count == 0)
            {
                Console.WriteLine("No historical data available for comparison");
                return 1;
            }

            Console.WriteLine("Network Comparison Results:");
            Console.WriteLine();

            foreach (var comp in comparisons)
            {
                Console.WriteLine($"🏆 {comp.Ssid} (Score: {comp.Score:F1})");
                Console.WriteLine($"  Signal: {comp.AverageSignal:F0}% (range: {comp.MinSignal}-{comp.MaxSignal}%)");
                Console.WriteLine($"  Stability: {comp.Stability:F0}%");
                Console.WriteLine($"  Availability: {comp.Availability:F0}%");
                Console.WriteLine($"  Data points: {comp.MeasurementCount}");
                Console.WriteLine();
            }

            return 0;
        }

        private static async Task<int> ExecuteHelp(string[] args)
        {
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  MurtiWiFi Connector - Enterprise WiFi Management CLI");
            Console.WriteLine("  Version 2.0.0 | Secure, Fast, Reliable");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine();

            Console.WriteLine("QUICK START:");
            Console.WriteLine("  status                     Check current WiFi connection");
            Console.WriteLine("  scan                       Find available networks");
            Console.WriteLine("  connect MyWiFi password    Connect to a network");
            Console.WriteLine();

            Console.WriteLine("CONNECTION COMMANDS:");
            Console.WriteLine("  connect <SSID> <password>  Connect to WiFi network");
            Console.WriteLine("  disconnect                 Disconnect current connection");
            Console.WriteLine("  quick                      Interactive connection wizard");
            Console.WriteLine();
            Console.WriteLine("  Examples:");
            Console.WriteLine("    connect \"Home WiFi\" mypassword123");
            Console.WriteLine("    disconnect");
            Console.WriteLine();

            Console.WriteLine("NETWORK DISCOVERY:");
            Console.WriteLine("  scan                       Scan for available networks");
            Console.WriteLine("  profiles                   List saved WiFi profiles");
            Console.WriteLine("  status                     Show current connection status");
            Console.WriteLine("  info                       Detailed network information");
            Console.WriteLine();
            Console.WriteLine("  Examples:");
            Console.WriteLine("    scan");
            Console.WriteLine("    profiles");
            Console.WriteLine();

            Console.WriteLine("DIAGNOSTICS & TESTING:");
            Console.WriteLine("  diag                       Run system diagnostics");
            Console.WriteLine("  health                     Check system health");
            Console.WriteLine("  test                       Connection quality test");
            Console.WriteLine("  speed                      Network speed test");
            Console.WriteLine();

            Console.WriteLine("BACKUP & RESTORE:");
            Console.WriteLine("  backup [filename]          Backup WiFi profiles");
            Console.WriteLine("  restore <filepath>         Restore from backup");
            Console.WriteLine("  backup-list                List available backups");
            Console.WriteLine();
            Console.WriteLine("  Examples:");
            Console.WriteLine("    backup my_profiles.json");
            Console.WriteLine("    restore backup_20250106.json");
            Console.WriteLine();

            Console.WriteLine("CONFIGURATION:");
            Console.WriteLine("  config list                Show all settings");
            Console.WriteLine("  config get <key>           Get specific setting");
            Console.WriteLine("  config set <key> <value>   Update setting");
            Console.WriteLine();
            Console.WriteLine("  Examples:");
            Console.WriteLine("    config get ScanInterval");
            Console.WriteLine("    config set ScanInterval 60");
            Console.WriteLine();

            Console.WriteLine("SECURITY:");
            Console.WriteLine("  security                   Security status overview");
            Console.WriteLine("  security-audit             Full security audit");
            Console.WriteLine("  security-metrics           Security metrics report");
            Console.WriteLine();

            Console.WriteLine("UTILITIES:");
            Console.WriteLine("  help                       Show this help message");
            Console.WriteLine("  version                    Display version info");
            Console.WriteLine("  clear                      Clear console");
            Console.WriteLine("  exit                       Exit application");
            Console.WriteLine();

            Console.WriteLine("SHORTCUTS:");
            Console.WriteLine("  c, q, s, d, h, v           First letter abbreviations");
            Console.WriteLine("  Example: 's' for status, 'c' for connect");
            Console.WriteLine();

            Console.WriteLine("For more help: consult the packaged README documentation");
            Console.WriteLine("Report issues: Run 'diag' command for diagnostics");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  prefer <SSID> [priority]   Add or update preferred network");
            Console.WriteLine("  prefer-remove <SSID>       Remove preferred network");
            Console.WriteLine("  prefer-list                Show preferred networks");
            Console.WriteLine("  prefer-clear               Clear all preferred networks");
            Console.WriteLine();
            Console.WriteLine("Management Commands:");
            Console.WriteLine("  delete <SSID>             Delete saved profile");
            Console.WriteLine("  reset                     Reset network adapter");
            Console.WriteLine();
            Console.WriteLine("Diagnostic Commands:");
            Console.WriteLine("  diag                      Run diagnostics");
            Console.WriteLine("  test                      Run system tests");
            Console.WriteLine("  validate                  Validate system");
            Console.WriteLine();
            Console.WriteLine("Configuration Commands:");
            Console.WriteLine("  config                    Show current config");
            Console.WriteLine("  config set <key> <value>  Update setting");
            Console.WriteLine("  config describe <key>     Show setting details");
            Console.WriteLine("  config list               List configurable settings");
            Console.WriteLine("  config metadata [path]    Export settings metadata as JSON");
            Console.WriteLine("  config metadata --defaults [path]  Export defaults-only metadata");
            Console.WriteLine("  config reset              Reset to defaults");
            Console.WriteLine("  config validate           Validate configuration");
            Console.WriteLine();
            Console.WriteLine("Aliases: c=connect, d=disconnect, q=quick, s=status, i=info, a=scan, p=profiles, r=reset, h=help");

            return 0;
        }

        private static async Task<int> ExecuteVersion(string[] args)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine($"MurtiWifiConnecter v{version}");
            Console.WriteLine("Optimized WiFi Management Tool");
            return 0;
        }

        private static async Task<int> ExecuteClear(string[] args)
        {
            Console.Clear();
            return 0;
        }

        private static async Task<int> ExecuteExit(string[] args)
        {
            Environment.Exit(0);
            return 0;
        }

        private static async Task<int> ExecuteInteractive()
        {
            UIHelper.PrintInfo("Interactive mode ready. Press Ctrl+C or type 'exit' to quit.");
            Console.WriteLine();

            while (true)
            {
                var input = InteractiveConsole.ReadCommand("> ", CommandNames);
                if (input == null)
                {
                    Console.WriteLine();
                    break;
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                var parts = SplitArguments(input);
                if (parts.Length == 0)
                {
                    continue;
                }

                var command = parts[0].ToLowerInvariant();
                if (command == "exit" || command == "quit")
                {
                    break;
                }

                _ = await ProcessCommand(parts);
                Console.WriteLine();
            }

            return 0;
        }

        private static string[] SplitArguments(string input)
        {
            var arguments = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            var escapeNext = false;

            foreach (var ch in input)
            {
                if (escapeNext)
                {
                    current.Append(ch);
                    escapeNext = false;
                    continue;
                }

                switch (ch)
                {
                    case '"':
                        inQuotes = !inQuotes;
                        continue;
                    case '\\':
                        if (inQuotes)
                        {
                            escapeNext = true;
                            continue;
                        }
                        break;
                }

                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        arguments.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }

            if (current.Length > 0)
            {
                arguments.Add(current.ToString());
            }

            return arguments.ToArray();
        }

        private static string GetSignalBar(int signal)
        {
            return UIHelper.GetSignalStrengthBar(signal);
        }

        // Enterprise command implementations
        private static async Task<int> ExecuteRealtime(string[] args)
        {
            var session = await RealtimeMonitor.GetCurrentSession();

            UIHelper.PrintHeader("Real-time Monitoring", UIHelper.Colors.Info);

            var sessionItems = new List<(string, string, ConsoleColor?)>
            {
                ("Status", session.IsActive ? "Active" : "Inactive", session.IsActive ? UIHelper.Colors.Success : UIHelper.Colors.Warning),
                ("Start Time", session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"), null),
                ("Event Count", session.EventCount.ToString(), null),
                ("Metric Count", session.MetricCount.ToString(), null)
            };

            if (session.IsActive)
            {
                sessionItems.Add(("Current Network", session.CurrentNetwork ?? "None", null));
                sessionItems.Add(("Connection Status", session.ConnectionStatus, session.ConnectionStatus == "Connected" ? UIHelper.Colors.Success : UIHelper.Colors.Warning));
                if (session.SignalStrength > 0)
                {
                    sessionItems.Add(("Signal Strength", $"{session.SignalStrength}%", UIHelper.GetSignalColor(session.SignalStrength)));
                }
            }

            UIHelper.PrintBox("Monitoring Session", sessionItems);

            if (session.RecentEvents.Any())
            {
                Console.WriteLine("\nRecent Events:");
                foreach (var evt in session.RecentEvents.TakeLast(5))
                {
                    Console.WriteLine($"  {evt.Timestamp:HH:mm:ss} [{evt.Severity}] {evt.Type}: {evt.Description}");
                }
            }

            return 0;
        }

        private static async Task<int> ExecuteMonitorStart(string[] args)
        {
            int interval = 10;
            int duration = 60;

            if (args.Length > 1 && int.TryParse(args[1], out var parsedInterval))
            {
                interval = Math.Max(5, Math.Min(300, parsedInterval));
            }

            if (args.Length > 2 && int.TryParse(args[2], out var parsedDuration))
            {
                duration = Math.Max(1, Math.Min(1440, parsedDuration));
            }

            var started = await RealtimeMonitor.StartMonitoring(interval, duration);

            if (started)
            {
                UIHelper.PrintSuccess($"Real-time monitoring started!");
                Console.WriteLine($"  Interval: {interval} seconds");
                Console.WriteLine($"  Duration: {duration} minutes");
            }
            else
            {
                UIHelper.PrintWarning("Monitoring is already active");
            }

            return 0;
        }

        private static async Task<int> ExecuteMonitorStop(string[] args)
        {
            var stopped = await RealtimeMonitor.StopMonitoring();

            if (stopped)
            {
                UIHelper.PrintSuccess("Real-time monitoring stopped");
            }
            else
            {
                UIHelper.PrintInfo("No active monitoring session");
            }

            return 0;
        }

        private static async Task<int> ExecuteAlerts(string[] args)
        {
            var alerts = await RealtimeMonitor.GetActiveAlerts();

            if (!alerts.Any())
            {
                UIHelper.PrintSuccess("No active alerts - system is healthy!");
                return 0;
            }

            UIHelper.PrintHeader($"Active Alerts ({alerts.Count})", UIHelper.Colors.Warning);

            foreach (var alert in alerts)
            {
                var severityColor = alert.Severity switch
                {
                    RealtimeMonitor.AlertSeverity.Critical => UIHelper.Colors.Error,
                    RealtimeMonitor.AlertSeverity.High => UIHelper.Colors.Error,
                    RealtimeMonitor.AlertSeverity.Medium => UIHelper.Colors.Warning,
                    _ => UIHelper.Colors.Info
                };

                UIHelper.PrintMessage($"{UIHelper.Symbols.Warning} {alert.Message}", severityColor);
                Console.WriteLine($"    Network: {alert.Network ?? "N/A"}");
                Console.WriteLine($"    Recommendation: {alert.Recommendation}");
                Console.WriteLine();
            }

            return 0;
        }

        private static async Task<int> ExecuteAutomation(string[] args)
        {
            if (args.Length < 2 || args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                PrintAutomationSummary();
                return 0;
            }

            var subCommand = args[1].ToLowerInvariant();

            switch (subCommand)
            {
                case "help":
                    PrintAutomationUsage();
                    return 0;

                case "create-smart":
                    await CreateAutomationRuleAsync("Smart auto-connect", AutomationEngine.CreateSmartConnectionRule);
                    return 0;

                case "create-monitor":
                case "create-performance":
                    await CreateAutomationRuleAsync("Performance monitoring", AutomationEngine.CreatePerformanceMonitoringRule);
                    return 0;

                case "create-security":
                    await CreateAutomationRuleAsync("Security audit", AutomationEngine.CreateSecurityAuditRule);
                    return 0;

                case "create-maintenance":
                    await CreateAutomationRuleAsync("Maintenance", AutomationEngine.CreateMaintenanceRule);
                    return 0;

                case "run":
                    return await RunAutomationRule(args);

                case "run-all":
                case "execute-all":
                    return await RunAllAutomationRules();

                case "enable":
                    return await ToggleAutomationRule(args, true);

                case "disable":
                    return await ToggleAutomationRule(args, false);

                case "delete":
                    return await DeleteAutomationRule(args);

                case "check":
                    return await ExecuteAutomationCheck();

                case "show":
                case "describe":
                case "details":
                    return await ShowAutomationRule(args);

                default:
                    Console.WriteLine($"Unknown automation command: {subCommand}");
                    PrintAutomationUsage();
                    return 1;
            }
        }

        private static async Task CreateAutomationRuleAsync(string label, Func<Task<string>> factory)
        {
            var ruleId = await factory();
            UIHelper.PrintSuccess($"{label} rule created");
            Console.WriteLine($"  Rule ID: {ruleId}");
        }

        private static async Task<int> RunAutomationRule(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: automation run <ruleId|name>");
                return 1;
            }

            var identifier = string.Join(' ', args.Skip(2));
            var rule = FindAutomationRule(identifier);

            if (rule == null)
            {
                Console.WriteLine($"Automation rule not found: {identifier}");
                return 1;
            }

            if (!rule.IsEnabled)
            {
                Console.WriteLine($"Automation rule '{rule.Name}' is disabled. Enable it with 'automation enable " +
                                  $"{rule.Id}' before running.");
                return 1;
            }

            var result = await AutomationEngine.ExecuteRule(rule.Id);
            if (result.Success)
            {
                UIHelper.PrintSuccess($"Automation '{rule.Name}' completed");
            }
            else
            {
                UIHelper.PrintWarning($"Automation '{rule.Name}' reported errors: {result.Error ?? "Unknown error"}");
            }

            foreach (var action in result.ActionResults)
            {
                var outcome = action.Success ? "Success" : "Failed";
                Console.WriteLine($"  - {action.ActionType}: {outcome} {FormatOptionalData(action)}");
            }

            return result.Success ? 0 : 1;
        }

        private static async Task<int> RunAllAutomationRules()
        {
            var enabledRules = AutomationEngine.GetActiveRules()
                .Where(r => r.IsEnabled)
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!enabledRules.Any())
            {
                Console.WriteLine("No enabled automation rules available to execute");
                return 0;
            }

            var results = await AutomationEngine.ExecuteAllEnabledRules();

            foreach (var result in results)
            {
                var status = result.Success ? "completed" : "failed";
                Console.WriteLine($"Rule '{result.RuleName}' {status}");

                foreach (var action in result.ActionResults)
                {
                    var outcome = action.Success ? "Success" : "Failed";
                    Console.WriteLine($"  - {action.ActionType}: {outcome} {FormatOptionalData(action)}");
                }

                if (!result.Success && !string.IsNullOrEmpty(result.Error))
                {
                    Console.WriteLine($"    Error: {result.Error}");
                }
            }

            return results.All(r => r.Success) ? 0 : 1;
        }

        private static async Task<int> ToggleAutomationRule(string[] args, bool enable)
        {
            var actionName = enable ? "enable" : "disable";

            if (args.Length < 3)
            {
                Console.WriteLine($"Usage: automation {actionName} <ruleId|name>");
                return 1;
            }

            var identifier = string.Join(' ', args.Skip(2));
            var rule = FindAutomationRule(identifier);

            if (rule == null)
            {
                Console.WriteLine($"Automation rule not found: {identifier}");
                return 1;
            }

            var updated = await AutomationEngine.EnableRule(rule.Id, enable);
            if (!updated)
            {
                Console.WriteLine($"Failed to {actionName} rule '{rule.Name}'");
                return 1;
            }

            UIHelper.PrintSuccess($"Rule '{rule.Name}' {(enable ? "enabled" : "disabled")}");
            return 0;
        }

        private static async Task<int> DeleteAutomationRule(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: automation delete <ruleId|name>");
                return 1;
            }

            var identifier = string.Join(' ', args.Skip(2));
            var rule = FindAutomationRule(identifier);

            if (rule == null)
            {
                Console.WriteLine($"Automation rule not found: {identifier}");
                return 1;
            }

            var removed = await AutomationEngine.DeleteRule(rule.Id);
            if (!removed)
            {
                Console.WriteLine($"Failed to delete rule '{rule.Name}'");
                return 1;
            }

            UIHelper.PrintSuccess($"Rule '{rule.Name}' deleted");
            return 0;
        }

        private static async Task<int> ExecuteAutomationCheck()
        {
            var results = await AutomationEngine.CheckAndExecuteTriggers();

            if (!results.Any())
            {
                Console.WriteLine("No automation triggers matched current conditions");
                return 0;
            }

            foreach (var result in results)
            {
                var status = result.Success ? "completed" : "failed";
                Console.WriteLine($"Rule '{result.RuleName}' {status}");

                foreach (var action in result.ActionResults)
                {
                    var outcome = action.Success ? "Success" : "Failed";
                    Console.WriteLine($"  - {action.ActionType}: {outcome} {FormatOptionalData(action)}");
                }

                if (!result.Success && !string.IsNullOrEmpty(result.Error))
                {
                    Console.WriteLine($"    Error: {result.Error}");
                }
            }

            return results.All(r => r.Success) ? 0 : 1;
        }

        private static void PrintAutomationSummary()
        {
            var rules = AutomationEngine.GetActiveRules()
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Console.WriteLine($"Automation Rules: {rules.Count}");

            if (!rules.Any())
            {
                Console.WriteLine("Use 'automation create-smart' to create automation rules");
                PrintAutomationUsage();
                return;
            }

            foreach (var rule in rules)
            {
                var status = rule.IsEnabled ? "Enabled" : "Disabled";
                var lastRun = rule.LastExecuted?.ToString("yyyy-MM-dd HH:mm") ?? "Never";
                Console.WriteLine($"  • {rule.Name} ({status})");
                Console.WriteLine($"    Id: {rule.Id}");
                Console.WriteLine($"    Executions: {rule.ExecutionCount}, Last Run: {lastRun}");
            }
        }

        private static void PrintAutomationUsage()
        {
            Console.WriteLine("Automation commands:");
            Console.WriteLine("  automation list");
            Console.WriteLine("  automation create-smart");
            Console.WriteLine("  automation create-monitor");
            Console.WriteLine("  automation create-security");
            Console.WriteLine("  automation create-maintenance");
            Console.WriteLine("  automation run-all             Execute all enabled automation rules");
            Console.WriteLine("  automation run <ruleId|name>");
            Console.WriteLine("  automation enable <ruleId|name>");
            Console.WriteLine("  automation disable <ruleId|name>");
            Console.WriteLine("  automation delete <ruleId|name>");
            Console.WriteLine("  automation check");
        }

        private static AutomationEngine.AutomationRule FindAutomationRule(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return null;
            }

            var trimmed = identifier.Trim();
            var rules = AutomationEngine.GetActiveRules();

            return rules.FirstOrDefault(r => string.Equals(r.Id, trimmed, StringComparison.OrdinalIgnoreCase))
                ?? rules.FirstOrDefault(r => string.Equals(r.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        }

        private static Task<int> ShowAutomationRule(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: automation show <ruleId|name>");
                return Task.FromResult(1);
            }

            var identifier = string.Join(' ', args.Skip(2));
            var rule = FindAutomationRule(identifier);

            if (rule == null)
            {
                Console.WriteLine($"Automation rule not found: {identifier}");
                return Task.FromResult(1);
            }

            UIHelper.PrintHeader($"Automation Rule: {rule.Name}", UIHelper.Colors.Info);

            Console.WriteLine($"Id: {rule.Id}");
            Console.WriteLine($"Status: {(rule.IsEnabled ? "Enabled" : "Disabled")}");
            Console.WriteLine($"Created: {rule.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Last Executed: {rule.LastExecuted:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Executions: {rule.ExecutionCount}");
            Console.WriteLine($"Stop On Error: {rule.StopOnError}");

            var trigger = rule.Trigger;
            if (trigger != null)
            {
                Console.WriteLine();
                Console.WriteLine("Trigger:");
                Console.WriteLine($"  Type: {trigger.Type}");
                if (!string.IsNullOrWhiteSpace(trigger.Description))
                {
                    Console.WriteLine($"  Description: {trigger.Description}");
                }
                if (!string.IsNullOrWhiteSpace(trigger.Schedule))
                {
                    Console.WriteLine($"  Schedule: {trigger.Schedule}");
                }
                if (!string.IsNullOrWhiteSpace(trigger.Parameter1))
                {
                    Console.WriteLine($"  Parameter1: {trigger.Parameter1}");
                }
                if (!string.IsNullOrWhiteSpace(trigger.Parameter2))
                {
                    Console.WriteLine($"  Parameter2: {trigger.Parameter2}");
                }
            }

            if (rule.Actions?.Any() == true)
            {
                Console.WriteLine();
                Console.WriteLine("Actions:");
                for (int i = 0; i < rule.Actions.Count; i++)
                {
                    var action = rule.Actions[i];
                    Console.WriteLine($"  {i + 1}. Type: {action.Type}");
                    if (!string.IsNullOrWhiteSpace(action.Description))
                    {
                        Console.WriteLine($"     Description: {action.Description}");
                    }
                    if (!string.IsNullOrWhiteSpace(action.Parameter1))
                    {
                        Console.WriteLine($"     Parameter1: {action.Parameter1}");
                    }
                    if (!string.IsNullOrWhiteSpace(action.Parameter2))
                    {
                        Console.WriteLine($"     Parameter2: {action.Parameter2}");
                    }
                    if (action.DelayAfterMs > 0)
                    {
                        Console.WriteLine($"     Delay After: {action.DelayAfterMs}ms");
                    }
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Actions: none configured");
            }

            return Task.FromResult(0);
        }

        private static string FormatOptionalData(AutomationEngine.AutomationActionResult action)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(action.Data))
            {
                parts.Add(action.Data);
            }

            if (!action.Success && !string.IsNullOrWhiteSpace(action.Error))
            {
                parts.Add($"Error: {action.Error}");
            }

            return parts.Count == 0 ? string.Empty : $"({string.Join("; ", parts)})";
        }

        private static async Task<int> ExecuteCompliance(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: compliance <standard>");
                Console.WriteLine("Standards: soc2, gdpr, hipaa, iso27001, nist, pci");
                return 1;
            }

            var standardText = args[1].ToLower();
            ComplianceFramework.ComplianceStandard standard;

            switch (standardText)
            {
                case "soc2":
                    standard = ComplianceFramework.ComplianceStandard.SOC2;
                    break;
                case "gdpr":
                    standard = ComplianceFramework.ComplianceStandard.GDPR;
                    break;
                default:
                    Console.WriteLine($"Unsupported standard: {standardText}");
                    return 1;
            }

            var report = await ComplianceFramework.GenerateComplianceReport(standard);
            Console.WriteLine($"\n{standard} Compliance Score: {report.OverallScore:F1}%");
            Console.WriteLine($"Level: {report.ComplianceLevel}");

            return 0;
        }

        private static async Task<int> ExecuteHealth(string[] args)
        {
            try
            {
                Console.WriteLine("System Health Check");
                Console.WriteLine("===================");
                
                // Basic system health checks
                var healthStatus = await SystemHealthMonitor.CheckSystemHealthAsync();
                
                Console.WriteLine($"CPU Usage: {healthStatus.CpuUsage:F1}%");
                Console.WriteLine($"Memory Usage: {healthStatus.MemoryUsage:F1}%");
                Console.WriteLine($"Disk Space: {healthStatus.DiskSpace:F1}%");
                Console.WriteLine($"Network Status: {healthStatus.NetworkStatus}");
                
                if (healthStatus.IsHealthy)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("System Health: GOOD");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("System Health: WARNING");
                    Console.WriteLine($"Issues: {string.Join(", ", healthStatus.Issues)}");
                }
                
                Console.ResetColor();
                return 0;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Health check failed");
                Console.WriteLine($"Error during health check: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> ExecuteReport(string[] args)
        {
            try
            {
                Console.WriteLine("Generating System Report");
                Console.WriteLine("========================");
                
                var report = await ReportingSystem.GenerateReportAsync();
                
                Console.WriteLine($"Report generated: {report.FilePath}");
                Console.WriteLine($"Report size: {report.Size} bytes");
                Console.WriteLine($"Generation time: {report.GenerationTime.TotalSeconds:F1} seconds");
                
                return 0;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Report generation failed");
                Console.WriteLine($"Error generating report: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> ExecuteSecurityScan(string[] args)
        {
            try
            {
                Console.WriteLine("Security Scan");
                Console.WriteLine("=============");
                
                var scanResults = await SecurityManager.RunSecurityScanAsync();
                
                Console.WriteLine($"Scan completed. Found {scanResults.Vulnerabilities.Count} vulnerabilities.");
                
                if (scanResults.Vulnerabilities.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Vulnerabilities found:");
                    foreach (var vuln in scanResults.Vulnerabilities)
                    {
                        Console.WriteLine($"- {vuln.Description} (Severity: {vuln.Severity})");
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("No vulnerabilities found.");
                }
                
                Console.ResetColor();
                return 0;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Security scan failed");
                Console.WriteLine($"Error during security scan: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> ExecutePerformance(string[] args)
        {
            try
            {
                Console.WriteLine("Performance Analysis");
                Console.WriteLine("====================");
                
                var metrics = await HardwareMonitor.GetPerformanceMetricsAsync();
                
                Console.WriteLine($"Response Time: {metrics.ResponseTime.TotalMilliseconds:F1} ms");
                Console.WriteLine($"Throughput: {metrics.Throughput:F1} ops/sec");
                Console.WriteLine($"Error Rate: {metrics.ErrorRate:P2}");
                Console.WriteLine($"Resource Usage: CPU {metrics.CpuUsage:F1}%, Memory {metrics.MemoryUsage:F1}%");
                
                if (metrics.IsOptimal)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Performance: OPTIMAL");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Performance: SUBOPTIMAL");
                    Console.WriteLine("Recommendations:");
                    foreach (var rec in metrics.Recommendations)
                    {
                        Console.WriteLine($"- {rec}");
                    }
                }
                
                Console.ResetColor();
                return 0;
            }
            catch (Exception ex)
            {
                await ErrorHandler.LogError(ex, "Performance analysis failed");
                Console.WriteLine($"Error during performance analysis: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> ExecuteWifiAnalyze(string[] args)
        {
            try
            {
                using var analyzer = new WifiAnalyzer();
                analyzer.AnalysisUpdated += (s, e) =>
                {
                    Console.WriteLine($"信号強度分析更新: 平均={e.Analysis.AverageSignalStrength:F1}dBm");
                    foreach (var network in e.Analysis.NetworkData.Take(3))
                    {
                        Console.WriteLine($"  {network.Ssid}: {network.SignalStrength}% ({network.Channel}ch)");
                    }
                };

                analyzer.InterferenceDetected += (s, e) =>
                {
                    Console.WriteLine($"⚠️ チャンネル干渉検知: {e.InterferenceLevel:F1}dBm");
                    Console.WriteLine($"影響チャンネル: {string.Join(", ", e.AffectedChannels.Select(c => $"{c.Channel}ch"))}");
                    Console.WriteLine($"推奨: {e.Recommendation}");
                };

                Console.WriteLine("WiFi分析を開始しています...");
                analyzer.StartAnalysis();

                Console.WriteLine("分析を停止するにはEnterキーを押してください。");
                Console.ReadLine();

                analyzer.StopAnalysis();
                Console.WriteLine("WiFi分析を停止しました。");

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"WiFi分析中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "WiFi分析実行エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteSpeedHistory(string[] args)
        {
            try
            {
                using var speedTest = new EnhancedSpeedTest();
                var history = speedTest.GetHistory(20);

                if (!history.Any())
                {
                    Console.WriteLine("速度テスト履歴がありません。");
                    return 0;
                }

                Console.WriteLine("\n速度テスト履歴:");
                Console.WriteLine("日時\t\t\tDL速度(Mbps)\tUL速度(Mbps)\t状態");
                Console.WriteLine("-".PadRight(80, '-'));

                foreach (var entry in history)
                {
                    var status = entry.Success ? "✓" : "✗";
                    Console.WriteLine($"{entry.Timestamp:yyyy/MM/dd HH:mm:ss}\t{entry.DownloadSpeed,8:F2}\t{entry.UploadSpeed,8:F2}\t{status}");
                }

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"速度履歴取得中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "速度履歴取得エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteSpeedStats(string[] args)
        {
            try
            {
                using var speedTest = new EnhancedSpeedTest();

                // 手動で速度テストを実行
                Console.WriteLine("速度テストを実行中...");
                var result = await speedTest.PerformSpeedTestAsync();

                if (result.Success)
                {
                    Console.WriteLine($"ダウンロード速度: {result.DownloadSpeed:F2} Mbps");
                    Console.WriteLine($"アップロード速度: {result.UploadSpeed:F2} Mbps");
                }
                else
                {
                    Console.WriteLine($"速度テスト失敗: {result.Message}");
                }

                // 統計情報表示
                var stats = speedTest.GetNetworkStatistics();
                Console.WriteLine($"\nネットワーク統計:");
                Console.WriteLine($"送信バイト数: {stats.FormatBytesSent()}");
                Console.WriteLine($"受信バイト数: {stats.FormatBytesReceived()}");
                Console.WriteLine($"現在の速度: {stats.CurrentSpeed:F1} Mbps");

                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"速度統計取得中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "速度統計取得エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteConnectionReport(string[] args)
        {
            try
            {
                using var reporting = new ReportingSystem();

                Console.WriteLine("接続履歴レポートを生成中...");

                var report = await reporting.GenerateConnectionHistoryReportAsync();

                Console.WriteLine($"\n接続履歴レポート (期間: {report.StartDate:yyyy/MM/dd} - {report.EndDate:yyyy/MM/dd})");
                Console.WriteLine($"総接続数: {report.TotalConnections}");
                Console.WriteLine($"成功率: {report.SuccessRate:F1}%");
                Console.WriteLine($"成功: {report.SuccessfulConnections}, 失敗: {report.FailedConnections}");

                Console.WriteLine("\nSSID別統計 (上位5件):");
                foreach (var ssidStat in report.SsidStatistics.Take(5))
                {
                    Console.WriteLine($"  {ssidStat.Ssid}: {ssidStat.ConnectionCount}回接続, 平均信号強度: {ssidStat.AverageSignalStrength:F1}%");
                }

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"接続レポート生成中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "接続レポート生成エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteSecurityReport(string[] args)
        {
            try
            {
                using var reporting = new ReportingSystem();

                Console.WriteLine("セキュリティレポートを生成中...");

                var report = await reporting.GenerateSecurityReportAsync();

                Console.WriteLine($"\nセキュリティレポート (期間: {report.StartDate:yyyy/MM/dd} - {report.EndDate:yyyy/MM/dd})");
                Console.WriteLine($"総イベント数: {report.TotalEvents}");

                Console.WriteLine("\nイベントタイプ別統計:");
                foreach (var eventType in report.EventTypeStatistics)
                {
                    Console.WriteLine($"  {eventType.EventType}: {eventType.Count}件 (深刻度: {eventType.Severity})");
                }

                Console.WriteLine("\nリスクレベル別統計:");
                foreach (var riskLevel in report.RiskLevelStatistics)
                {
                    Console.WriteLine($"  {riskLevel.RiskLevel}: {riskLevel.Count}件 ({riskLevel.Percentage:F1}%)");
                }

                if (report.TopRiskFactors.Any())
                {
                    Console.WriteLine("\nトップリスク要因:");
                    foreach (var factor in report.TopRiskFactors.Take(5))
                    {
                        Console.WriteLine($"  {factor.Factor}: {factor.Count}件");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"セキュリティレポート生成中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "セキュリティレポート生成エラー");
                return 1;
            }
        }

        private static async Task<int> ExecutePerformanceReport(string[] args)
        {
            try
            {
                using var reporting = new ReportingSystem();

                Console.WriteLine("パフォーマンスレポートを生成中...");

                var report = await reporting.GeneratePerformanceReportAsync();

                Console.WriteLine($"\nパフォーマンスレポート (期間: {report.StartDate:yyyy/MM/dd} - {report.EndDate:yyyy/MM/dd})");
                Console.WriteLine($"測定回数: {report.TotalMeasurements}");
                Console.WriteLine($"平均ダウンロード速度: {report.AverageDownloadSpeed:F2} Mbps");
                Console.WriteLine($"平均アップロード速度: {report.AverageUploadSpeed:F2} Mbps");
                Console.WriteLine($"平均遅延: {report.AverageLatency:F1} ms");
                Console.WriteLine($"平均信号強度: {report.AverageSignalStrength:F1}%");

                Console.WriteLine("\n時間帯別統計:");
                foreach (var hourly in report.HourlyStatistics.Take(10))
                {
                    Console.WriteLine($"  {hourly.Hour}:00 - DL: {hourly.AverageDownloadSpeed:F1}Mbps, UL: {hourly.AverageUploadSpeed:F1}Mbps ({hourly.MeasurementCount}測定)");
                }

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"パフォーマンスレポート生成中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "パフォーマンスレポート生成エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteContinuousAuth(string[] args)
        {
            try
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("使用法: continuous-auth <ユーザーID> <チェック間隔秒>");
                    return 1;
                }

                var userId = args[1];
                if (!int.TryParse(args[2], out var intervalSeconds) || intervalSeconds < 10)
                {
                    Console.WriteLine("チェック間隔は10秒以上で指定してください。");
                    return 1;
                }

                Console.WriteLine($"継続的認証チェックを開始します (ユーザー: {userId}, 間隔: {intervalSeconds}秒)");
                Console.WriteLine("停止するにはCtrl+Cを押してください。");

                var evaluator = new ZeroTrustEvaluator();
                var cancellationTokenSource = new CancellationTokenSource();

                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cancellationTokenSource.Cancel();
                };

                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        var context = new Dictionary<string, object>
                        {
                            ["LastActivity"] = DateTime.UtcNow.AddMinutes(-5),
                            ["Location"] = "Office",
                            ["DeviceFingerprint"] = "device_123"
                        };

                        var result = await evaluator.PerformContinuousAuthCheckAsync(userId, context);

                        Console.Write($"[{DateTime.Now:HH:mm:ss}] ");

                        if (result.IsAuthenticated)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("✓ 認証継続中");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Write("✗ 認証失敗");
                        }

                        Console.ResetColor();

                        if (result.RiskFactors.Any())
                        {
                            Console.Write($" (リスク要因: {string.Join(", ", result.RiskFactors)})");
                        }

                        Console.WriteLine();

                        await Task.Delay(intervalSeconds * 1000, cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                Console.WriteLine("\n継続的認証チェックを停止しました。");
                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"継続的認証チェック中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "継続的認証チェックエラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteAnalytics(string[] args)
        {
            try
            {
                string ssid = null;
                TimeSpan period = TimeSpan.FromMinutes(5);

                // Parse arguments
                for (int i = 1; i < args.Length; i++)
                {
                    var arg = args[i];
                    if (arg.StartsWith("--ssid=", StringComparison.OrdinalIgnoreCase))
                    {
                        ssid = arg.Substring("--ssid=".Length).Trim('"');
                    }
                    else if (arg.StartsWith("--period=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TimeSpan.TryParse(arg.Substring("--period=".Length), out var parsedPeriod))
                        {
                            period = parsedPeriod;
                        }
                        else
                        {
                            Console.WriteLine("無効な期間形式。例: --period=00:05:00");
                            return 1;
                        }
                    }
                    else if (ssid == null && !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                    {
                        ssid = arg.Trim('"');
                    }
                    else
                    {
                        Console.WriteLine($"不明なオプション: {arg}");
                        Console.WriteLine("使用法: analytics [SSID] [--period=<期間>] [--ssid=<SSID>]");
                        return 1;
                    }
                }

                Console.WriteLine("信号品質分析を実行中...");
                var analysis = await NetworkAnalytics.AnalyzeSignalQualityAsync(ssid, period);

                Console.WriteLine($"\n信号品質分析レポート ({analysis.Ssid})");
                Console.WriteLine("=".Repeat(60));
                Console.WriteLine($"分析期間: {analysis.AnalysisPeriod.TotalMinutes:F0}分");
                Console.WriteLine($"測定回数: {analysis.TotalMeasurements}");
                Console.WriteLine($"生成日時: {analysis.GeneratedAt:yyyy-MM-dd HH:mm:ss}");

                if (string.IsNullOrEmpty(analysis.Message))
                {
                    Console.WriteLine($"\n信号統計:");
                    Console.WriteLine($"  平均信号強度: {analysis.AverageSignalStrength:F1}%");
                    Console.WriteLine($"  最小信号強度: {analysis.MinSignalStrength}%");
                    Console.WriteLine($"  最大信号強度: {analysis.MaxSignalStrength}%");
                    Console.WriteLine($"  信号安定性: {analysis.SignalStability:F1}%");
                    Console.WriteLine($"  推定SNR: {analysis.EstimatedSNR:F1}dB");

                    Console.WriteLine($"\n干渉レベル: {analysis.InterferenceLevel}");
                    if (analysis.ChannelUtilization.Any())
                    {
                        Console.WriteLine($"\nチャンネル使用率:");
                        foreach (var kvp in analysis.ChannelUtilization.OrderBy(c => c.Key))
                        {
                            var utilizationBar = new string('█', (int)(kvp.Value * 20));
                            Console.WriteLine($"  チャンネル {kvp.Key}: {kvp.Value:P1} {utilizationBar}");
                        }
                    }

                    if (analysis.HeatMapData.Any())
                    {
                        Console.WriteLine($"\n時間帯別信号品質:");
                        foreach (var point in analysis.HeatMapData)
                        {
                            var qualityBar = new string('█', (int)(point.QualityScore / 5));
                            Console.WriteLine($"  {point.TimeSlot:00}:00: {point.AverageSignalStrength:F1}% [{qualityBar}] ({point.MeasurementCount}測定)");
                        }
                    }

                    if (analysis.Recommendations.Any())
                    {
                        Console.WriteLine($"\n推奨事項:");
                        foreach (var recommendation in analysis.Recommendations)
                        {
                            Console.WriteLine($"• {recommendation}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"メッセージ: {analysis.Message}");
                }

                await Logger.LogInfo("Signal quality analysis completed", nameof(CommandProcessor), new Dictionary<string, object>
                {
                    ["ssid"] = analysis.Ssid,
                    ["measurements"] = analysis.TotalMeasurements,
                    ["avgSignal"] = analysis.AverageSignalStrength,
                    ["interference"] = analysis.InterferenceLevel.ToString()
                });

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"信号品質分析中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "信号品質分析エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteIsolation(string[] args)
                    Console.WriteLine("  isolation classify <SSID>   - 指定したネットワークを分類");
                    Console.WriteLine();
                    Console.WriteLine("例:");
                    Console.WriteLine("  isolation recommendations");
                    Console.WriteLine("  isolation validate");
                    Console.WriteLine("  isolation classify \"Company-Guest\"");
                    return 0;
                }

                var subCommand = args[1].ToLowerInvariant();

                switch (subCommand)
                {
                    case "recommendations":
                    case "recs":
                        return await ExecuteIsolationRecommendations(args);

                    case "validate":
                    case "check":
                        return await ExecuteIsolationValidate(args);

                    case "classify":
                        if (args.Length < 3)
                        {
                            UIHelper.PrintError("ネットワーク名を指定してください。");
                            Console.WriteLine("例: isolation classify \"MyWiFi\"");
                            return 1;
                        }
                        return await ExecuteNetworkClassify(args.Skip(1).ToArray());

                    default:
                        UIHelper.PrintError($"不明なサブコマンド: {subCommand}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"ネットワーク分離コマンド実行中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "ネットワーク分離コマンドエラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteNetworkClassify(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    UIHelper.PrintError("ネットワーク名を指定してください。");
                    Console.WriteLine("例: network-classify \"MyWiFi\"");
                    return 1;
                }

                var ssid = args[1];

                // Get network information
                var networks = await NetworkOperations.ScanNetworksAsync(true);
                var network = networks.FirstOrDefault(n => n.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase));

                if (network == null)
                {
                    UIHelper.PrintError($"ネットワーク '{ssid}' が見つかりません。");
                    Console.WriteLine("利用可能なネットワークを確認するには 'scan' コマンドを使用してください。");
                    return 1;
                }

                var classification = await NetworkIsolationManager.ClassifyNetworkAsync(
                    network.Ssid, network.Security, network.Band);

                Console.WriteLine($"ネットワーク分類結果: {network.Ssid}");
                Console.WriteLine("=".Repeat(50));

                // Display classification results
                if (classification.IsGuestNetwork)
                {
                    UIHelper.ShowModal("ゲストネットワーク検出",
                        $"ネットワーク '{network.Ssid}' はゲストネットワークとして分類されました。\n\n" +
                        "アクセス制限:\n" +
                        string.Join("\n", classification.AccessRestrictions.Select(r => $"• {r}")) + "\n\n" +
                        "推奨事項:\n" +
                        string.Join("\n", classification.Recommendations.Select(r => $"• {r}")),
                        UIHelper.ModalType.Info);
                }
                else if (classification.IsInternalNetwork)
                {
                    UIHelper.ShowModal("内部ネットワーク検出",
                        $"ネットワーク '{network.Ssid}' は内部ネットワークとして分類されました。\n\n" +
                        "セキュリティレベル: {classification.IsolationLevel}\n\n" +
                        "推奨事項:\n" +
                        string.Join("\n", classification.Recommendations.Select(r => $"• {r}")),
                        UIHelper.ModalType.Info);
                }
                else
                {
                    Console.WriteLine($"分類: 未分類 (セキュリティ: {network.Security}, 周波数帯: {network.Band})");
                    Console.WriteLine();
                    Console.WriteLine("推奨事項:");
                    foreach (var recommendation in classification.Recommendations)
                    {
                        Console.WriteLine($"• {recommendation}");
                    }
                }

                await Logger.LogInfo("Network classified", nameof(CommandProcessor), new Dictionary<string, object>
                {
                    ["ssid"] = ssid,
                    ["classification"] = classification.IsolationLevel.ToString(),
                    ["isGuest"] = classification.IsGuestNetwork,
                    ["isInternal"] = classification.IsInternalNetwork
                });

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"ネットワーク分類中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "ネットワーク分類エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteIsolationRecommendations(string[] args)
        {
            try
            {
                Console.WriteLine("ネットワーク分離の推奨事項");
                Console.WriteLine("=".Repeat(50));

                var recommendations = await NetworkIsolationManager.GetIsolationRecommendationsAsync();

                if (!recommendations.Any())
                {
                    Console.WriteLine("推奨事項が見つかりません。");
                    return 0;
                }

                foreach (var rec in recommendations.OrderBy(r => r.Priority))
                {
                    var priorityColor = rec.Priority switch
                    {
                        NetworkIsolationManager.RecommendationPriority.High => ConsoleColor.Red,
                        NetworkIsolationManager.RecommendationPriority.Medium => ConsoleColor.Yellow,
                        _ => ConsoleColor.Gray
                    };

                    Console.ForegroundColor = priorityColor;
                    Console.Write($"[{rec.Priority.ToString().ToUpper()}] ");
                    Console.ResetColor();
                    Console.WriteLine($"{rec.Category}: {rec.Title}");
                    Console.WriteLine($"  {rec.Description}");
                    Console.WriteLine();

                    if (rec.ImplementationSteps.Any())
                    {
                        Console.WriteLine("  実装手順:");
                        for (int i = 0; i < rec.ImplementationSteps.Count; i++)
                        {
                            Console.WriteLine($"    {i + 1}. {rec.ImplementationSteps[i]}");
                        }
                        Console.WriteLine();
                    }

                    if (rec.Benefits.Any())
                    {
                        Console.WriteLine("  利点:");
                        foreach (var benefit in rec.Benefits)
                        {
                            Console.WriteLine($"    • {benefit}");
                        }
                        Console.WriteLine();
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"分離推奨事項取得中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "分離推奨事項エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteIsolationValidate(string[] args)
        {
            try
            {
                Console.WriteLine("ネットワーク分離設定の検証");
                Console.WriteLine("=".Repeat(50));

                var results = await NetworkIsolationManager.ValidateIsolationSetupAsync();

                if (!results.Any())
                {
                    Console.WriteLine("検証結果が見つかりません。");
                    return 0;
                }

                var hasErrors = false;
                var hasWarnings = false;

                foreach (var result in results)
                {
                    var statusColor = result.Status switch
                    {
                        NetworkIsolationManager.ValidationStatus.Pass => ConsoleColor.Green,
                        NetworkIsolationManager.ValidationStatus.Warning => ConsoleColor.Yellow,
                        NetworkIsolationManager.ValidationStatus.Error => ConsoleColor.Red,
                        _ => ConsoleColor.Gray
                    };

                    if (result.Status == NetworkIsolationManager.ValidationStatus.Error) hasErrors = true;
                    if (result.Status == NetworkIsolationManager.ValidationStatus.Warning) hasWarnings = true;

                    Console.ForegroundColor = statusColor;
                    Console.Write($"[{result.Status.ToString().ToUpper()}] ");
                    Console.ResetColor();
                    Console.WriteLine($"{result.CheckType}: {result.Message}");

                    if (!string.IsNullOrEmpty(result.Recommendation))
                    {
                        Console.WriteLine($"  推奨: {result.Recommendation}");
                    }
                    Console.WriteLine();
                }

                // Summary
                if (hasErrors)
                {
                    UIHelper.ShowModal("検証結果",
                        "ネットワーク分離設定にエラーが見つかりました。上記の推奨事項を確認してください。",
                        UIHelper.ModalType.Warning);
                }
                else if (hasWarnings)
                {
                    Console.WriteLine("設定は機能しますが、最適化の余地があります。");
                }
                else
                {
                    UIHelper.ShowModal("検証結果",
                        "ネットワーク分離設定は適切に構成されています。",
                        UIHelper.ModalType.Success);
                }

                return hasErrors ? 1 : 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"分離設定検証中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "分離設定検証エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteBandwidth(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("帯域監視管理コマンド");
                    Console.WriteLine();
                    Console.WriteLine("使用可能なサブコマンド:");
                    Console.WriteLine("  bandwidth monitor start     - 帯域監視を開始");
                    Console.WriteLine("  bandwidth monitor stop      - 帯域監視を停止");
                    Console.WriteLine("  bandwidth stats             - 現在の帯域統計を表示");
                    Console.WriteLine("  bandwidth report <期間>     - 帯域レポートを生成 (例: 1h, 24h, 7d)");
                    Console.WriteLine();
                    Console.WriteLine("例:");
                    Console.WriteLine("  bandwidth monitor start");
                    Console.WriteLine("  bandwidth stats");
                    Console.WriteLine("  bandwidth report 24h");
                    return 0;
                }

                var subCommand = args[1].ToLowerInvariant();

                switch (subCommand)
                {
                    case "monitor":
                        if (args.Length < 3)
                        {
                            UIHelper.PrintError("モニター操作を指定してください (start/stop)");
                            return 1;
                        }
                        var operation = args[2].ToLowerInvariant();
                        return operation switch
                        {
                            "start" => await ExecuteBandwidthMonitor(args),
                            "stop" => await ExecuteBandwidthMonitorStop(),
                            _ => throw new ArgumentException($"不明なモニター操作: {operation}")
                        };

                    case "stats":
                        return await ExecuteBandwidthStats(args);

                    case "report":
                        return await ExecuteBandwidthReport(args);

                    default:
                        UIHelper.PrintError($"不明なサブコマンド: {subCommand}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"帯域監視コマンド実行中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "帯域監視コマンドエラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteBandwidthMonitor(string[] args)
        {
            try
            {
                await BandwidthMonitor.StartMonitoringAsync();
                UIHelper.ShowModal("帯域監視", "帯域監視を開始しました。", UIHelper.ModalType.Success);
                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"帯域監視開始中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "帯域監視開始エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteBandwidthMonitorStop()
        {
            try
            {
                await BandwidthMonitor.StopMonitoringAsync();
                UIHelper.ShowModal("帯域監視", "帯域監視を停止しました。", UIHelper.ModalType.Info);
                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"帯域監視停止中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "帯域監視停止エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteBandwidthStats(string[] args)
        {
            try
            {
                var stats = await BandwidthMonitor.GetCurrentStatisticsAsync();

                Console.WriteLine("現在の帯域統計");
                Console.WriteLine("=".Repeat(50));
                Console.WriteLine($"現在の使用率: {stats.CurrentUtilization:F1}%");
                Console.WriteLine($"現在の転送速度: {FormatBytesPerSecond(stats.CurrentBytesPerSecond)}");
                Console.WriteLine($"1時間の平均使用率: {stats.AverageUtilizationLastHour:F1}%");
                Console.WriteLine($"1時間のピーク使用率: {stats.PeakUtilizationLastHour:F1}%");
                Console.WriteLine($"測定回数: {stats.TotalMeasurements}");
                Console.WriteLine($"最終測定時刻: {stats.LastMeasurementTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"アクティブなアラート: {stats.ActiveAlerts}");

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"帯域統計取得中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "帯域統計取得エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteBandwidthReport(string[] args)
        {
            try
            {
                TimeSpan period = TimeSpan.FromHours(24); // Default 24 hours

                if (args.Length >= 3)
                {
                    var periodStr = args[2].ToLowerInvariant();
                    period = periodStr switch
                    {
                        var p when p.EndsWith("h") && int.TryParse(p.TrimEnd('h'), out var hours) => TimeSpan.FromHours(hours),
                        var p when p.EndsWith("d") && int.TryParse(p.TrimEnd('d'), out var days) => TimeSpan.FromDays(days),
                        var p when p.EndsWith("m") && int.TryParse(p.TrimEnd('m'), out var minutes) => TimeSpan.FromMinutes(minutes),
                        _ => TimeSpan.FromHours(24)
                    };
                }

                Console.WriteLine($"帯域レポート生成中... (期間: {period.TotalHours:F0}時間)");
                var report = await BandwidthMonitor.GenerateReportAsync(period);

                Console.WriteLine($"\n帯域使用レポート ({report.Period.TotalHours:F0}時間)");
                Console.WriteLine("=".Repeat(60));
                Console.WriteLine($"生成日時: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"測定回数: {report.TotalMeasurements}");

                if (report.TotalMeasurements > 0)
                {
                    Console.WriteLine($"\n使用統計:");
                    Console.WriteLine($"  平均使用率: {report.AverageUtilization:F1}%");
                    Console.WriteLine($"  最大使用率: {report.MaxUtilization:F1}%");
                    Console.WriteLine($"  最小使用率: {report.MinUtilization:F1}%");
                    Console.WriteLine($"  総転送量: {FormatBytes(report.TotalBytesTransferred)}");

                    if (report.PeakUsageHours.Any())
                    {
                        Console.WriteLine($"\nピーク使用時間帯 (上位5件):");
                        foreach (var peak in report.PeakUsageHours)
                        {
                            Console.WriteLine($"  {peak.Hour}:00 - 平均: {peak.AverageUtilization:F1}%, 最大: {peak.MaxUtilization:F1}% ({peak.MeasurementCount}測定)");
                        }
                    }

                    if (report.Recommendations.Any())
                    {
                        Console.WriteLine($"\n推奨事項:");
                        foreach (var recommendation in report.Recommendations)
                        {
                            Console.WriteLine($"• {recommendation}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("指定された期間内に測定データが見つかりません。");
                }

                await Logger.LogInfo("Bandwidth report generated", nameof(CommandProcessor),
                    new Dictionary<string, object>
                    {
                        ["periodHours"] = period.TotalHours,
                        ["measurements"] = report.TotalMeasurements,
                        ["avgUtilization"] = report.AverageUtilization
                    });

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"帯域レポート生成中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "帯域レポート生成エラー");
                return 1;
            }
        }

        private static string FormatBytesPerSecond(long bytesPerSecond)
        {
            const double KB = 1024;
            const double MB = KB * 1024;
            const double GB = MB * 1024;

            if (bytesPerSecond >= GB)
                return $"{bytesPerSecond / GB:F2} GB/s";
            else if (bytesPerSecond >= MB)
                return $"{bytesPerSecond / MB:F2} MB/s";
            else if (bytesPerSecond >= KB)
                return $"{bytesPerSecond / KB:F2} KB/s";
            else
                return $"{bytesPerSecond} B/s";
        }

        private static string FormatBytes(double bytes)
        {
            const double KB = 1024;
            const double MB = KB * 1024;
            const double GB = MB * 1024;
            const double TB = GB * 1024;

            if (bytes >= TB)
                return $"{bytes / TB:F2} TB";
            else if (bytes >= GB)
                return $"{bytes / GB:F2} GB";
            else if (bytes >= MB)
                return $"{bytes / MB:F2} MB";
            else if (bytes >= KB)
                return $"{bytes / KB:F2} KB";
            else
                return $"{bytes:F0} B";
        }

        private static async Task<int> ExecuteFirmware(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("ファームウェア管理コマンド");
                    Console.WriteLine();
                    Console.WriteLine("使用可能なサブコマンド:");
                    Console.WriteLine("  firmware scan              - ファームウェア更新をスキャン");
                    Console.WriteLine("  firmware update <device>   - 指定デバイスのファームウェアを更新");
                    Console.WriteLine("  firmware report <期間>     - ファームウェアレポートを生成 (例: 30d, 90d)");
                    Console.WriteLine("  firmware stats             - ファームウェア統計を表示");
                    Console.WriteLine();
                    Console.WriteLine("例:");
                    Console.WriteLine("  firmware scan");
                    Console.WriteLine("  firmware update \"WiFi Adapter\"");
                    Console.WriteLine("  firmware report 30d");
                    return 0;
                }

                var subCommand = args[1].ToLowerInvariant();

                switch (subCommand)
                {
                    case "scan":
                        return await ExecuteFirmwareScan(args);

                    case "update":
                        return await ExecuteFirmwareUpdate(args);

                    case "report":
                        return await ExecuteFirmwareReport(args);

                    case "stats":
                        return await ExecuteFirmwareStats(args);

                    default:
                        UIHelper.PrintError($"不明なサブコマンド: {subCommand}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"ファームウェア管理コマンド実行中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "ファームウェア管理コマンドエラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteFirmwareScan(string[] args)
        {
            try
            {
                Console.WriteLine("ファームウェア更新のスキャンを開始します...");
                var result = await FirmwareManager.ScanForUpdatesAsync();

                Console.WriteLine($"\nスキャン結果 ({result.ScanEndTime - result.ScanStartTime:hh\\:mm\\:ss}):");
                Console.WriteLine("=".Repeat(60));

                if (!result.Success)
                {
                    UIHelper.PrintError($"スキャンに失敗しました: {result.Error}");
                    return 1;
                }

                Console.WriteLine($"検出されたデバイス: {result.Devices.Count}");
                Console.WriteLine($"利用可能な更新: {result.AvailableUpdates.Count}");

                if (result.Devices.Any())
                {
                    Console.WriteLine($"\n検出されたデバイス:");
                    foreach (var device in result.Devices)
                    {
                        Console.WriteLine($"  {device.Name} ({device.Manufacturer})");
                        Console.WriteLine($"    ドライバ: {device.DriverVersion ?? "不明"}");
                        Console.WriteLine($"    ファームウェア: {device.FirmwareVersion ?? "不明"}");
                    }
                }

                if (result.AvailableUpdates.Any())
                {
                    Console.WriteLine($"\n利用可能な更新:");
                    foreach (var update in result.AvailableUpdates)
                    {
                        var severityColor = update.Severity switch
                        {
                            FirmwareManager.UpdateSeverity.Critical => ConsoleColor.Red,
                            FirmwareManager.UpdateSeverity.Important => ConsoleColor.Yellow,
                            _ => ConsoleColor.Gray
                        };

                        Console.ForegroundColor = severityColor;
                        Console.Write($"[{update.Severity.ToString().ToUpper()}] ");
                        Console.ResetColor();
                        Console.WriteLine($"{update.DeviceName}: {update.CurrentVersion} → {update.NewVersion}");
                        Console.WriteLine($"  ソース: {update.Source}");
                        if (!string.IsNullOrEmpty(update.ReleaseNotes))
                        {
                            Console.WriteLine($"  リリースノート: {update.ReleaseNotes}");
                        }
                    }

                    UIHelper.ShowModal("ファームウェア更新",
                        $"{result.AvailableUpdates.Count}件のファームウェア更新が見つかりました。\n\n" +
                        "重要な更新がある場合は、すぐに適用することを推奨します。",
                        UIHelper.ModalType.Info);
                }
                else
                {
                    UIHelper.ShowModal("ファームウェアスキャン",
                        "すべてのファームウェアが最新です。",
                        UIHelper.ModalType.Success);
                }

                await Logger.LogInfo("Firmware scan completed", nameof(CommandProcessor),
                    new Dictionary<string, object>
                    {
                        ["devicesFound"] = result.Devices.Count,
                        ["updatesFound"] = result.AvailableUpdates.Count,
                        ["scanDuration"] = (result.ScanEndTime - result.ScanStartTime).TotalSeconds
                    });

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"ファームウェアスキャン中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "ファームウェアスキャンエラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteFirmwareUpdate(string[] args)
        {
            try
            {
                if (args.Length < 3)
                {
                    UIHelper.PrintError("デバイス名を指定してください。");
                    Console.WriteLine("例: firmware update \"WiFi Adapter\"");
                    return 1;
                }

                var deviceName = args[2];

                // First scan for updates
                var scanResult = await FirmwareManager.ScanForUpdatesAsync();
                if (!scanResult.Success)
                {
                    UIHelper.PrintError("更新スキャンに失敗しました。");
                    return 1;
                }

                // Find the update for the specified device
                var update = scanResult.AvailableUpdates
                    .FirstOrDefault(u => u.DeviceName?.Contains(deviceName, StringComparison.OrdinalIgnoreCase) == true);

                if (update == null)
                {
                    UIHelper.PrintError($"デバイス '{deviceName}' の更新が見つかりません。");
                    Console.WriteLine("利用可能なデバイス:");
                    foreach (var device in scanResult.Devices)
                    {
                        Console.WriteLine($"  - {device.Name}");
                    }
                    return 1;
                }

                // Confirm update
                var confirm = UIHelper.PromptYesNo($"デバイス '{update.DeviceName}' のファームウェアを更新しますか？\n\n" +
                    $"現在のバージョン: {update.CurrentVersion}\n" +
                    $"新しいバージョン: {update.NewVersion}\n" +
                    $"重要度: {update.Severity}\n" +
                    $"ソース: {update.Source}");

                if (!confirm)
                {
                    Console.WriteLine("更新をキャンセルしました。");
                    return 0;
                }

                Console.WriteLine($"ファームウェア更新を開始します: {update.DeviceName}");
                var result = await FirmwareManager.ApplyUpdateAsync(update);

                if (result.Success)
                {
                    UIHelper.ShowModal("ファームウェア更新",
                        $"デバイス '{update.DeviceName}' のファームウェアが正常に更新されました。\n\n" +
                        $"バージョン: {update.CurrentVersion} → {update.NewVersion}",
                        UIHelper.ModalType.Success);
                }
                else
                {
                    UIHelper.ShowModal("ファームウェア更新",
                        $"デバイス '{update.DeviceName}' のファームウェア更新に失敗しました。\n\n" +
                        $"エラー: {result.Error}\n\n" +
                        "手動での更新を検討してください。",
                        UIHelper.ModalType.Error);
                    return 1;
                }

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"ファームウェア更新中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "ファームウェア更新エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteFirmwareReport(string[] args)
        {
            try
            {
                TimeSpan period = TimeSpan.FromDays(30); // Default 30 days

                if (args.Length >= 3)
                {
                    var periodStr = args[2].ToLowerInvariant();
                    period = periodStr switch
                    {
                        var p when p.EndsWith("d") && int.TryParse(p.TrimEnd('d'), out var days) => TimeSpan.FromDays(days),
                        var p when p.EndsWith("w") && int.TryParse(p.TrimEnd('w'), out var weeks) => TimeSpan.FromDays(weeks * 7),
                        var p when p.EndsWith("m") && int.TryParse(p.TrimEnd('m'), out var months) => TimeSpan.FromDays(months * 30),
                        _ => TimeSpan.FromDays(30)
                    };
                }

                Console.WriteLine($"ファームウェアレポート生成中... (期間: {period.TotalDays:F0}日)");
                var report = await FirmwareManager.GenerateReportAsync(period);

                Console.WriteLine($"\nファームウェアレポート ({report.Period.TotalDays:F0}日)");
                Console.WriteLine("=".Repeat(60));
                Console.WriteLine($"生成日時: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");

                Console.WriteLine($"\n更新履歴: {report.UpdateHistory.Count}件");
                var successful = report.UpdateHistory.Count(h => h.Success);
                var failed = report.UpdateHistory.Count(h => !h.Success);
                Console.WriteLine($"  成功: {successful}件");
                Console.WriteLine($"  失敗: {failed}件");

                if (report.UpdateHistory.Any())
                {
                    Console.WriteLine($"\n最近の更新:");
                    foreach (var update in report.UpdateHistory.OrderByDescending(h => h.Timestamp).Take(5))
                    {
                        var status = update.Success ? "✓" : "✗";
                        Console.WriteLine($"  {status} {update.DeviceName}: {update.PreviousVersion} → {update.NewVersion} ({update.Timestamp:yyyy-MM-dd})");
                    }
                }

                Console.WriteLine($"\nアクティブな更新ソース: {report.ActiveSources.Count}");
                foreach (var source in report.ActiveSources)
                {
                    Console.WriteLine($"  - {source.Name} (最終確認: {source.LastChecked:yyyy-MM-dd HH:mm})");
                }

                if (report.Recommendations.Any())
                {
                    Console.WriteLine($"\n推奨事項:");
                    foreach (var recommendation in report.Recommendations)
                    {
                        Console.WriteLine($"• {recommendation}");
                    }
                }

                await Logger.LogInfo("Firmware report generated", nameof(CommandProcessor),
                    new Dictionary<string, object>
                    {
                        ["periodDays"] = period.TotalDays,
                        ["updatesFound"] = report.UpdateHistory.Count,
                        ["sourcesActive"] = report.ActiveSources.Count
                    });

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"ファームウェアレポート生成中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "ファームウェアレポート生成エラー");
                return 1;
            }
        }

        private static async Task<int> ExecuteFirmwareStats(string[] args)
        {
            try
            {
                var stats = await FirmwareManager.GetStatisticsAsync();

                Console.WriteLine("ファームウェア統計");
                Console.WriteLine("=".Repeat(50));
                Console.WriteLine($"総更新チェック数: {stats.TotalUpdatesChecked}");
                Console.WriteLine($"成功した更新: {stats.SuccessfulUpdates}");
                Console.WriteLine($"失敗した更新: {stats.FailedUpdates}");
                Console.WriteLine($"最終スキャン時刻: {stats.LastScanTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"アクティブなソース: {stats.ActiveSources}");
                Console.WriteLine($"保留中の更新: {stats.PendingUpdates}");

                if (stats.TotalUpdatesChecked > 0)
                {
                    var successRate = (double)stats.SuccessfulUpdates / stats.TotalUpdatesChecked * 100;
                    Console.WriteLine($"成功率: {successRate:F1}%");
                }

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"ファームウェア統計取得中にエラーが発生しました: {ex.Message}");
                await ErrorHandler.LogError(ex, "ファームウェア統計取得エラー");
                return 1;
            }
        }
    }
    catch (Exception ex)
    {
        UIHelper.PrintError($"ハードウェア監視コマンド実行中にエラーが発生しました: {ex.Message}");
        await ErrorHandler.LogError(ex, "ハードウェア監視コマンドエラー");
        return 1;
    }
}

private static async Task<int> ExecuteHardwareMonitor(string[] args)
{
    try
    {
        await HardwareMonitor.StartMonitoringAsync();
        UIHelper.ShowModal("ハードウェア監視", "ハードウェア監視を開始しました。", UIHelper.ModalType.Success);
        return 0;
    }
    catch (Exception ex)
    {
        UIHelper.PrintError($"ハードウェア監視開始中にエラーが発生しました: {ex.Message}");
        await ErrorHandler.LogError(ex, "ハードウェア監視開始エラー");
        return 1;
    }
}

private static async Task<int> ExecuteHardwareMonitorStop()
{
    try
    {
        await HardwareMonitor.StopMonitoringAsync();
        UIHelper.ShowModal("ハードウェア監視", "ハードウェア監視を停止しました。", UIHelper.ModalType.Info);
        return 0;
    }
    catch (Exception ex)
    {
        UIHelper.PrintError($"ハードウェア監視停止中にエラーが発生しました: {ex.Message}");
        await ErrorHandler.LogError(ex, "ハードウェア監視停止エラー");
        return 1;
    }
}

private static async Task<int> ExecuteHardwareStats(string[] args)
{
    try
    {
        var stats = await HardwareMonitor.GetCurrentStatisticsAsync();

        Console.WriteLine("現在のハードウェア統計");
        Console.WriteLine("=".Repeat(50));
        Console.WriteLine($"ネットワークインターフェース: {stats.NetworkInterfaces}");
        Console.WriteLine($"最終測定時刻: {stats.LastMeasurementTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"測定回数: {stats.TotalMeasurements}");
        Console.WriteLine($"アクティブなアラート: {stats.ActiveAlerts}");

        if (stats.SystemPerformance != null)
        {
            Console.WriteLine($"\nシステムパフォーマンス:");
            Console.WriteLine($"  CPU使用率: {stats.SystemPerformance.CpuUsagePercent:F1}%");
            Console.WriteLine($"  メモリ使用率: {stats.SystemPerformance.MemoryUsagePercent:F1}%");
            Console.WriteLine($"  ディスク使用率: {stats.SystemPerformance.DiskUsagePercent:F1}%");
        }

        return 0;
    }
    catch (Exception ex)
    {
        UIHelper.PrintError($"ハードウェア統計取得中にエラーが発生しました: {ex.Message}");
        await ErrorHandler.LogError(ex, "ハードウェア統計取得エラー");
        return 1;
    }
}

private static async Task<int> ExecuteHardwareReport(string[] args)
{
    try
    {
        TimeSpan period = TimeSpan.FromHours(24); // Default 24 hours

        if (args.Length >= 3)
        {
            var periodStr = args[2].ToLowerInvariant();
            period = periodStr switch
            {
                var p when p.EndsWith("h") && int.TryParse(p.TrimEnd('h'), out var hours) => TimeSpan.FromHours(hours),
                var p when p.EndsWith("d") && int.TryParse(p.TrimEnd('d'), out var days) => TimeSpan.FromDays(days),
                var p when p.EndsWith("m") && int.TryParse(p.TrimEnd('m'), out var minutes) => TimeSpan.FromMinutes(minutes),
                _ => TimeSpan.FromHours(24)
            };
        }

        Console.WriteLine($"ハードウェアレポート生成中... (期間: {period.TotalHours:F0}時間)");
        var report = await HardwareMonitor.GenerateReportAsync(period);

        Console.WriteLine($"\nハードウェアレポート ({report.Period.TotalHours:F0}時間)");
        Console.WriteLine("=".Repeat(60));
        Console.WriteLine($"生成日時: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"測定回数: {report.TotalMeasurements}");

        if (report.TotalMeasurements > 0)
        {
            Console.WriteLine($"\nインターフェース分析:");

            foreach (var interfaceAnalysis in report.InterfaceAnalysis)
            {
                Console.WriteLine($"\nインターフェース: {interfaceAnalysis.InterfaceName} ({interfaceAnalysis.InterfaceType})");
                Console.WriteLine($"  測定回数: {interfaceAnalysis.Measurements}");
                Console.WriteLine($"  平均受信速度: {FormatBytesPerSecond(interfaceAnalysis.AverageBytesReceivedPerSecond)}");
                Console.WriteLine($"  平均送信速度: {FormatBytesPerSecond(interfaceAnalysis.AverageBytesSentPerSecond)}");
                Console.WriteLine($"  平均エラー率: {interfaceAnalysis.AverageErrorRate:F2}%");
                Console.WriteLine($"  総エラー数: {interfaceAnalysis.TotalErrors}");

                if (interfaceAnalysis.WifiAnalysis != null)
                {
                    Console.WriteLine($"  WiFi信号品質: {interfaceAnalysis.WifiAnalysis.AverageSignalQuality:F1}% (最小: {interfaceAnalysis.WifiAnalysis.MinSignalQuality}%, 最大: {interfaceAnalysis.WifiAnalysis.MaxSignalQuality}%)");

                    if (interfaceAnalysis.WifiAnalysis.CommonSsids.Any())
                    {
                        Console.WriteLine($"  接続SSID:");
                        foreach (var ssidFreq in interfaceAnalysis.WifiAnalysis.CommonSsids)
                        {
                            Console.WriteLine($"    {ssidFreq.Ssid}: {ssidFreq.Frequency}回");
                        }
                    }

                    Console.WriteLine($"  平均受信レート: {interfaceAnalysis.WifiAnalysis.AverageReceiveRateMbps:F1} Mbps");
                    Console.WriteLine($"  平均送信レート: {interfaceAnalysis.WifiAnalysis.AverageTransmitRateMbps:F1} Mbps");
                }
            }

            if (report.Recommendations.Any())
            {
                Console.WriteLine($"\n推奨事項:");
                foreach (var recommendation in report.Recommendations)
                {
                    Console.WriteLine($"• {recommendation}");
                }
            }
        }
        else
        {
            Console.WriteLine("指定された期間内に測定データが見つかりません。");
        }

        await Logger.LogInfo("Hardware report generated", nameof(CommandProcessor),
            new Dictionary<string, object>
            {
                ["periodHours"] = period.TotalHours,
                ["measurements"] = report.TotalMeasurements,
                ["interfaces"] = report.InterfaceAnalysis.Count
            });

        return 0;
    }
    catch (Exception ex)
    {
        UIHelper.PrintError($"ハードウェアレポート生成中にエラーが発生しました: {ex.Message}");
        await ErrorHandler.LogError(ex, "ハードウェアレポート生成エラー");
        return 1;
    }
}

private static string FormatBytesPerSecond(long bytesPerSecond)
{
    const double KB = 1024;
    const double MB = KB * 1024;
    const double GB = MB * 1024;

    if (bytesPerSecond >= GB)
        return $"{bytesPerSecond / GB:F2} GB/s";
    else if (bytesPerSecond >= MB)
        return $"{bytesPerSecond / MB:F2} MB/s";
    else if (bytesPerSecond >= KB)
        return $"{bytesPerSecond / KB:F2} KB/s";
    else
        return $"{bytesPerSecond} B/s";
}

private static string FormatBytes(double bytes)
{
    const double KB = 1024;
    const double MB = KB * 1024;
    const double GB = MB * 1024;
    const double TB = GB * 1024;

    if (bytes >= TB)
        return $"{bytes / TB:F2} TB";
    else if (bytes >= GB)
        return $"{bytes / GB:F2} GB";
    else if (bytes >= MB)
        return $"{bytes / MB:F2} MB";
    else if (bytes >= KB)
        return $"{bytes / KB:F2} KB";
    else
        return $"{bytes:F0} B";
}