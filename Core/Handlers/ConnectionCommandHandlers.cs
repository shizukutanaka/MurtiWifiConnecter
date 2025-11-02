using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core.Handlers
{
    internal static class ConnectionCommandHandlers
    {
        private static readonly TimeSpan DefaultNetworkTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ProfileQueryTimeout = TimeSpan.FromSeconds(30);

        internal static async Task<int> ConnectAsync(string[] args, Func<Task<int>> showStatusAsync, bool emitJson = false)
        {
            if (args == null || args.Length < 2)
            {
                if (emitJson)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        connected = false,
                        error = "Usage: connect <SSID> [password]"
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    Console.WriteLine("Usage: connect <SSID> [password]");
                }
                return 1;
            }

            string ssid;
            string password;

            try
            {
                ssid = InputValidator.EnsureValidSsid(args[1]);
                password = args.Length > 2 ? InputValidator.EnsureValidPassword(args[2]) : null;
            }
            catch (ArgumentException ex)
            {
                if (emitJson)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        connected = false,
                        ssid = args.Length > 1 ? args[1] : null,
                        error = ex.Message
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    UIHelper.PrintError($"Invalid input: {ex.Message}");
                }
                return 1;
            }

            using var networkCts = CreateConsoleCancellation(DefaultNetworkTimeout, out var cancelHandler);
            ConsoleSpinner spinner = null;
            try
            {
                if (!emitJson)
                {
                    Console.Write($"Connecting to {ssid}");
                    spinner = new ConsoleSpinner();
                    _ = spinner.StartAsync();
                }

                var success = await NetworkOperations.ConnectAsync(ssid, password, networkCts.Token);
                spinner?.Stop();

                if (success)
                {
                    if (emitJson)
                    {
                        using var statusCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        var statusSnapshot = await NetworkOperations.GetStatusAsync(statusCts.Token);
                        var payload = new
                        {
                            connected = true,
                            ssid,
                            status = statusSnapshot?.Status,
                            signal = statusSnapshot?.Signal,
                            ipAddress = statusSnapshot?.IpAddress,
                            usingSavedProfile = string.IsNullOrEmpty(password)
                        };

                        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        }));
                    }
                    else
                    {
                        Console.WriteLine($"\r[OK] Connected to {ssid}");

                        if (showStatusAsync != null)
                        {
                            await Task.Delay(1000);
                            await showStatusAsync();
                        }
                    }
                }
                else
                {
                    if (emitJson)
                    {
                        using var statusCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        var statusSnapshot = await NetworkOperations.GetStatusAsync(statusCts.Token);
                        var payload = new
                        {
                            connected = false,
                            ssid,
                            error = "Failed to connect",
                            status = statusSnapshot?.Status,
                            signal = statusSnapshot?.Signal,
                            ipAddress = statusSnapshot?.IpAddress
                        };

                        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        }));
                    }
                    else
                    {
                        Console.WriteLine($"\r[FAIL] Failed to connect to {ssid}");
                    }
                }
                return success ? 0 : 1;
            }
            catch (OperationCanceledException)
            {
                spinner?.Stop();
                if (emitJson)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        connected = false,
                        ssid,
                        canceled = true,
                        error = "Connection attempt canceled"
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    UIHelper.PrintWarning("Connection attempt canceled");
                }
                return 1;
            }
            finally
            {
                spinner?.Stop();
                Console.CancelKeyPress -= cancelHandler;
            }
        }

        internal static async Task<int> DisconnectAsync(bool emitJson = false)
        {
            using var networkCts = CreateConsoleCancellation(DefaultNetworkTimeout, out var cancelHandler);
            try
            {
                using var statusBeforeCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var statusBefore = await NetworkOperations.GetStatusAsync(statusBeforeCts.Token);
                if (!emitJson)
                {
                    Console.Write("Disconnecting");
                }

                var success = await NetworkOperations.DisconnectAsync(networkCts.Token);
                using var statusAfterCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var statusAfter = await NetworkOperations.GetStatusAsync(statusAfterCts.Token);

                if (emitJson)
                {
                    var payload = new
                    {
                        disconnected = success,
                        message = success ? "Disconnected" : "Failed to disconnect",
                        previousStatus = statusBefore?.Status,
                        previousSsid = statusBefore?.Ssid,
                        previousSignal = statusBefore?.Signal,
                        currentStatus = statusAfter?.Status,
                        currentSsid = statusAfter?.Ssid,
                        currentSignal = statusAfter?.Signal
                    };

                    Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
                }
                else
                {
                    Console.WriteLine(success ? "\r[OK] Disconnected" : "\r[FAIL] Failed to disconnect");
                }

                return success ? 0 : 1;
            }
            catch (OperationCanceledException)
            {
                if (emitJson)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        disconnected = false,
                        canceled = true,
                        message = "Disconnect canceled"
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    UIHelper.PrintWarning("Disconnect canceled");
                }

                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }

        internal static async Task<int> QuickConnectAsync(int? requestedIndex, int displayLimit, bool emitJson, Func<Task<int>> showStatusAsync)
        {
            using var profileCts = CreateConsoleCancellation(ProfileQueryTimeout, out var cancelHandler);
            try
            {
                var profiles = await NetworkOperations.GetSavedProfilesAsync(profileCts.Token);
                if (profiles.Count == 0)
                {
                    if (emitJson)
                    {
                        Console.WriteLine(JsonSerializer.Serialize(new
                        {
                            connected = false,
                            reason = "No saved networks",
                            profiles = Array.Empty<string>()
                        }, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    else
                    {
                        UIHelper.PrintWarning("No saved networks found");
                    }
                    return 1;
                }

                if (requestedIndex.HasValue)
                {
                    if (requestedIndex.Value <= profiles.Count)
                    {
                        var selectedArgs = new[] { "connect", profiles[requestedIndex.Value - 1] };
                        return await ConnectAsync(selectedArgs, showStatusAsync, emitJson);
                    }

                    if (emitJson)
                    {
                        Console.WriteLine(JsonSerializer.Serialize(new
                        {
                            connected = false,
                            reason = "Requested index out of range",
                            requestedIndex,
                            availableProfiles = profiles.Count
                        }, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    else
                    {
                        UIHelper.PrintWarning("Requested profile index is out of range");
                    }
                    return 1;
                }

                var limitedProfiles = profiles
                    .Take(Math.Max(1, displayLimit))
                    .ToList();

                if (emitJson)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        connected = false,
                        selectionRequired = true,
                        totalProfiles = profiles.Count,
                        displayed = limitedProfiles.Count,
                        profiles = limitedProfiles
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    return 0;
                }

                Console.WriteLine("Saved networks:");
                for (int i = 0; i < limitedProfiles.Count; i++)
                {
                    Console.WriteLine($"  {i + 1}. {limitedProfiles[i]}");
                }

                if (profiles.Count > limitedProfiles.Count)
                {
                    Console.WriteLine($"  ... ({profiles.Count - limitedProfiles.Count} more not shown)");
                }

                Console.Write($"Select network (1-{limitedProfiles.Count}): ");
                if (int.TryParse(Console.ReadLine(), out var choice) && choice > 0 && choice <= limitedProfiles.Count)
                {
                    var selectedArgs = new[] { "connect", limitedProfiles[choice - 1] };
                    return await ConnectAsync(selectedArgs, showStatusAsync, emitJson);
                }

                UIHelper.PrintWarning("Invalid selection");
                return 1;
            }
            catch (OperationCanceledException)
            {
                if (emitJson)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        connected = false,
                        canceled = true,
                        reason = "Profile query canceled"
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    UIHelper.PrintWarning("Profile query canceled");
                }
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }

        private static CancellationTokenSource CreateConsoleCancellation(TimeSpan timeout, out ConsoleCancelEventHandler handler)
        {
            var cts = new CancellationTokenSource(timeout);
            handler = (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += handler;
            return cts;
        }
    }
}
