using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter.Tests.Performance
{
    [Collection("Performance Tests")]
    public class PerformanceTests
    {
        private const int PerformanceThresholdMs = 100; // 100ms threshold for most operations
        private const int HighLoadIterations = 1000;

        [Fact]
        public async Task AdaptivePolicyEngine_EvaluatePolicy_Performance()
        {
            // Arrange
            var engine = new AdaptivePolicyEngine();
            var context = new Dictionary<string, object>
            {
                ["threat_level"] = "low",
                ["location_anomaly"] = false,
                ["recent_failures"] = 1,
                ["pattern_score"] = 0.2
            };

            // Warm up
            await engine.EvaluatePolicyAsync("network_connect", context, 0.2);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await engine.EvaluatePolicyAsync("network_connect", context, 0.2);
            stopwatch.Stop();

            // Assert
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(PerformanceThresholdMs);
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task AdaptivePolicyEngine_HighLoad_Performance()
        {
            // Arrange
            var engine = new AdaptivePolicyEngine();
            var context = new Dictionary<string, object>
            {
                ["threat_level"] = "medium",
                ["device_trust"] = true,
                ["encrypted"] = true
            };

            // Act
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < HighLoadIterations; i++)
            {
                var result = await engine.EvaluatePolicyAsync("network_connect", context, 0.3);
                result.Should().NotBeNull();
            }
            stopwatch.Stop();

            // Assert - Should complete within reasonable time for high load
            var averageTimePerOperation = stopwatch.ElapsedMilliseconds / (double)HighLoadIterations;
            averageTimePerOperation.Should().BeLessThan(10); // Average < 10ms per operation
        }

        [Fact]
        public async Task ConfigManager_LoadConfig_Performance()
        {
            // Arrange - Warm up
            await ConfigManager.LoadConfig();

            // Act
            var stopwatch = Stopwatch.StartNew();
            var config = await ConfigManager.LoadConfig();
            stopwatch.Stop();

            // Assert
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(PerformanceThresholdMs);
            config.Should().NotBeNull();
        }

        [Fact]
        public async Task ConfigManager_UpdateSetting_Performance()
        {
            // Arrange
            var originalValue = await ConfigManager.GetSetting<bool>("autoconnect", true);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await ConfigManager.UpdateSetting("autoconnect", !originalValue);
            stopwatch.Stop();

            // Assert
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(PerformanceThresholdMs);
            result.Success.Should().BeTrue();

            // Restore original value
            await ConfigManager.UpdateSetting("autoconnect", originalValue);
        }

        [Fact]
        public async Task LocalizationManager_Localize_Performance()
        {
            // Arrange
            const string testKey = "ok";
            const int iterations = 10000;

            // Warm up
            LocalizationManager.Localize(testKey);

            // Act
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var result = LocalizationManager.Localize(testKey);
                result.Should().NotBeNullOrEmpty();
            }
            stopwatch.Stop();

            // Assert
            var averageTimePerCall = stopwatch.ElapsedMilliseconds / (double)iterations;
            averageTimePerCall.Should().BeLessThan(0.1); // Average < 0.1ms per call
        }

        [Fact]
        public async Task LocalizationManager_Localize_WithParameters_Performance()
        {
            // Arrange
            const string testKey = "warning";
            const int iterations = 10000;

            // Warm up
            LocalizationManager.Localize(testKey, "test");

            // Act
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var result = LocalizationManager.Localize(testKey, $"param{i}");
                result.Should().NotBeNullOrEmpty();
            }
            stopwatch.Stop();

            // Assert
            var averageTimePerCall = stopwatch.ElapsedMilliseconds / (double)iterations;
            averageTimePerCall.Should().BeLessThan(0.2); // Average < 0.2ms per call with formatting
        }

        [Fact]
        public async Task MemoryUsage_UnderLoad_StaysReasonable()
        {
            // Arrange
            var engine = new AdaptivePolicyEngine();
            var initialMemory = GC.GetTotalMemory(true);

            // Act - Perform many operations
            for (int i = 0; i < 1000; i++)
            {
                var context = new Dictionary<string, object>
                {
                    ["threat_level"] = i % 2 == 0 ? "low" : "high",
                    ["location_anomaly"] = i % 3 == 0,
                    ["recent_failures"] = i % 10,
                    ["pattern_score"] = (double)i / 1000.0
                };

                await engine.EvaluatePolicyAsync("network_connect", context, 0.2);
            }

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(true);
            var memoryIncrease = finalMemory - initialMemory;

            // Assert - Memory increase should be reasonable (less than 10MB)
            memoryIncrease.Should().BeLessThan(10 * 1024 * 1024);
        }

        [Fact]
        public async Task ConcurrentOperations_Performance()
        {
            // Arrange
            var engine = new AdaptivePolicyEngine();
            const int concurrentTasks = 10;
            const int operationsPerTask = 100;

            // Act
            var tasks = new List<Task>();
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < concurrentTasks; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < operationsPerTask; j++)
                    {
                        var context = new Dictionary<string, object>
                        {
                            ["threat_level"] = "low",
                            ["device_trust"] = true
                        };

                        var result = await engine.EvaluatePolicyAsync("network_connect", context, 0.2);
                        result.Should().NotBeNull();
                    }
                }));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var totalOperations = concurrentTasks * operationsPerTask;
            var averageTimePerOperation = stopwatch.ElapsedMilliseconds / (double)totalOperations;
            averageTimePerOperation.Should().BeLessThan(20); // Average < 20ms per operation under concurrency
        }

        [Fact]
        public async Task ConfigOperations_BatchPerformance()
        {
            // Arrange
            var settings = new[]
            {
                ("autoconnect", (object)true),
                ("scaninterval", (object)30),
                ("enablenotifications", (object)false),
                ("loglevel", (object)"Info")
            };

            // Act
            var stopwatch = Stopwatch.StartNew();
            foreach (var (key, value) in settings)
            {
                var result = await ConfigManager.UpdateSetting(key, value);
                result.Success.Should().BeTrue();
            }
            stopwatch.Stop();

            // Assert
            var averageTimePerSetting = stopwatch.ElapsedMilliseconds / (double)settings.Length;
            averageTimePerSetting.Should().BeLessThan(PerformanceThresholdMs);
        }

        [Fact]
        public async Task LocalizationOperations_BulkPerformance()
        {
            // Arrange
            var keys = new[] { "ok", "error", "warning", "info", "success" };
            const int iterations = 1000;

            // Warm up
            foreach (var key in keys)
            {
                LocalizationManager.Localize(key);
            }

            // Act
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                foreach (var key in keys)
                {
                    var result = LocalizationManager.Localize(key);
                    result.Should().NotBeNullOrEmpty();
                }
            }
            stopwatch.Stop();

            // Assert
            var totalOperations = keys.Length * iterations;
            var averageTimePerOperation = stopwatch.ElapsedMilliseconds / (double)totalOperations;
            averageTimePerOperation.Should().BeLessThan(0.05); // Very fast localization lookups
        }

        [Fact]
        public async Task LargeContext_EvaluationPerformance()
        {
            // Arrange
            var engine = new AdaptivePolicyEngine();
            var largeContext = new Dictionary<string, object>();

            // Create a large context with many features
            for (int i = 0; i < 50; i++)
            {
                largeContext[$"feature_{i}"] = i % 2 == 0 ? true : false;
                largeContext[$"metric_{i}"] = (double)i / 100.0;
                largeContext[$"category_{i}"] = $"category_{i % 5}";
            }

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await engine.EvaluatePolicyAsync("network_connect", largeContext, 0.3);
            stopwatch.Stop();

            // Assert
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(PerformanceThresholdMs * 2); // Allow some extra time for large context
            result.Should().NotBeNull();
            result.MLPrediction.Should().NotBeNull();
        }

        [Fact]
        public async Task MemoryCache_Efficiency()
        {
            // Arrange
            var engine = new AdaptivePolicyEngine();
            var context = new Dictionary<string, object>
            {
                ["threat_level"] = "low",
                ["cached_operation"] = true
            };

            // First call to populate cache
            await engine.EvaluatePolicyAsync("network_connect", context, 0.2);

            // Act - Measure cached performance
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                var result = await engine.EvaluatePolicyAsync("network_connect", context, 0.2);
                result.Should().NotBeNull();
            }
            stopwatch.Stop();

            // Assert - Cached operations should be very fast
            var averageTimePerCall = stopwatch.ElapsedMilliseconds / 100.0;
            averageTimePerCall.Should().BeLessThan(5); // Average < 5ms for cached operations
        }
    }
}
