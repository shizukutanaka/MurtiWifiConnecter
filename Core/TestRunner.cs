using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    public static class TestRunner
    {
        public static async Task<bool> RunBasicTests()
        {
            Console.WriteLine("Running basic system tests...");
            Console.WriteLine();

            var tests = new List<(string Name, Func<Task<bool>> Test)>
            {
                ("System Requirements", TestSystemRequirements),
                ("Network Adapter", TestNetworkAdapter),
                ("Command Execution", TestCommandExecution),
                ("Argument Guard", TestArgumentGuard),
                ("Preferred Priority", TestPreferredPriority),
                ("Profile Management", TestProfileManagement),
                ("Error Handling", TestErrorHandling),
                ("Performance", TestPerformance)
            };

            var results = new List<TestResult>();

            foreach (var (name, test) in tests)
            {
                Console.Write($"  {name,-20} ");
                var startTime = DateTime.Now;

                try
                {
                    var success = await test();
                    var duration = DateTime.Now - startTime;

                    results.Add(new TestResult
                    {
                        Name = name,
                        Success = success,
                        Duration = duration,
                        Message = success ? "PASS" : "FAIL"
                    });

                    Console.WriteLine(success ? "[OK] PASS" : "[FAIL] FAIL");
                }
                catch (Exception ex)
                {
                    var duration = DateTime.Now - startTime;
                    results.Add(new TestResult
                    {
                        Name = name,
                        Success = false,
                        Duration = duration,
                        Message = $"ERROR: {ex.Message}"
                    });
                    Console.WriteLine("[ERROR] EXCEPTION");
                }
            }

            Console.WriteLine();
            var passed = results.Count(r => r.Success);
            var total = results.Count;
            Console.WriteLine($"Tests completed: {passed}/{total} passed");

            return passed == total;
        }

        private static async Task<bool> TestArgumentGuard()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Valid input should pass and be trimmed
                    var args = new[] { "  valid  " };
                    CommandArgumentGuard.EnsureSafeArguments(args);
                    if (args[0] != "valid")
                    {
                        return false;
                    }

                    // Zero-width space must be rejected
                    try
                    {
                        CommandArgumentGuard.EnsureSafeArguments(new[] { "bad\u200Binput" });
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        // Expected
                    }

                    // Newline characters must be rejected before normalization
                    try
                    {
                        CommandArgumentGuard.EnsureSafeArguments(new[] { "line\nbreak" });
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        // Expected
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        private static async Task<bool> TestPreferredPriority()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var validSamples = new[] { 0, 100, 250, 500 };
                    var invalidSamples = new[] { -1, -50, 501, 600 };

                    foreach (var sample in validSamples)
                    {
                        if (!CommandProcessor.IsValidPreferredPriority(sample))
                        {
                            return false;
                        }
                    }

                    foreach (var sample in invalidSamples)
                    {
                        if (CommandProcessor.IsValidPreferredPriority(sample))
                        {
                            return false;
                        }
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        private static async Task<bool> TestSystemRequirements()
        {
            return await ErrorHandler.ValidateSystemRequirements();
        }

        private static async Task<bool> TestNetworkAdapter()
        {
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                    .ToList();

                if (interfaces.Count == 0)
                {
                    Console.WriteLine("\n    Warning: No WiFi adapters found");
                    return false;
                }

                // Test basic netsh connectivity
                var output = await ExecuteCommand("netsh", "wlan show interfaces");
                return !string.IsNullOrEmpty(output);
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> TestCommandExecution()
        {
            try
            {
                // Test basic command execution
                var result1 = await ExecuteCommand("netsh", "wlan show profiles");
                if (string.IsNullOrEmpty(result1)) return false;

                // Test network scan (should not fail even if no networks found)
                var networks = await NetworkOperations.ScanNetworksAsync();
                return networks != null; // Should return empty list, not null
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> TestProfileManagement()
        {
            try
            {
                // Test profile listing
                var profiles = await NetworkOperations.GetSavedProfilesAsync();
                if (profiles == null) return false;

                // Test status retrieval
                var status = await NetworkOperations.GetStatusAsync();
                return status != null && !string.IsNullOrEmpty(status.Status);
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> TestErrorHandling()
        {
            try
            {
                // Test error logging
                await ErrorHandler.LogError(new Exception("Test error"), "Unit test");

                // Test invalid operation handling
                var result = await ErrorHandler.HandleNetworkOperation(async () =>
                {
                    throw new Exception("Test exception");
                    return true;
                }, false);

                // Should return fallback value (false) instead of throwing
                return result == false;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> TestPerformance()
        {
            try
            {
                var startTime = DateTime.Now;

                // Test cached operations
                var status1 = await NetworkOperations.GetStatusAsync();
                var status2 = await NetworkOperations.GetStatusAsync(); // Should be faster (cached)

                var duration = DateTime.Now - startTime;

                // Should complete within reasonable time
                return duration.TotalSeconds < 5.0;
            }
            catch
            {
                return false;
            }
        }

        private static void ShowTestSummary(List<TestResult> results)
        {
            var passed = results.Count(r => r.Success);
            var total = results.Count;

            Console.WriteLine("Test Summary:");
            Console.WriteLine($"  Passed: {passed}/{total}");
            Console.WriteLine($"  Total time: {results.Sum(r => r.Duration.TotalMilliseconds):F0}ms");

            if (passed < total)
            {
                Console.WriteLine("\nFailed tests:");
                foreach (var result in results.Where(r => !r.Success))
                {
                    Console.WriteLine($"  - {result.Name}: {result.Message}");
                }
            }

            Console.WriteLine($"\nSystem status: {(passed == total ? "Ready" : "Issues detected")}");
        }

        public static async Task<bool> RunInteractiveTest()
        {
            Console.WriteLine("Interactive Test Mode");
            Console.WriteLine("This will test actual WiFi operations with user confirmation");
            Console.WriteLine();

            Console.Write("Run connection test? (y/N): ");
            var response = Console.ReadLine()?.ToLower();

            if (response == "y" || response == "yes")
            {
                return await RunConnectionTest();
            }

            Console.WriteLine("Skipping interactive tests");
            return true;
        }

        private static async Task<bool> RunConnectionTest()
        {
            Console.WriteLine("\nConnection Test:");

            // Show current status
            var status = await NetworkOperations.GetStatusAsync();
            Console.WriteLine($"Current status: {status.Status}");

            if (!string.IsNullOrEmpty(status.Ssid))
            {
                Console.WriteLine($"Connected to: {status.Ssid}");
            }

            // Show available networks
            Console.WriteLine("\nScanning for networks...");
            var networks = await NetworkOperations.ScanNetworksAsync(true);

            if (networks.Count > 0)
            {
                Console.WriteLine("Found networks:");
                foreach (var network in networks.Take(5))
                {
                    Console.WriteLine($"  - {network.Ssid} ({network.Signal}%)");
                }
            }

            // Show saved profiles
            var profiles = await NetworkOperations.GetSavedProfilesAsync();
            Console.WriteLine($"\nSaved profiles: {profiles.Count}");

            Console.WriteLine("\nConnection test completed successfully");
            return true;
        }

        private static async Task<string> ExecuteCommand(string command, string arguments)
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return output;
        }

        private class TestResult
        {
            public string Name { get; set; }
            public bool Success { get; set; }
            public TimeSpan Duration { get; set; }
            public string Message { get; set; }
        }
    }
}