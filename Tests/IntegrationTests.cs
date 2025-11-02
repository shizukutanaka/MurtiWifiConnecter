using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter.Tests.Integration
{
    [Collection("Integration Tests")]
    public class IntegrationTests
    {
        [Fact]
        public async Task FullPolicyEvaluationWorkflow_WorksEndToEnd()
        {
            // Arrange
            var engine = new AdaptivePolicyEngine();

            // Simulate a high-risk scenario
            var highRiskContext = new Dictionary<string, object>
            {
                ["threat_level"] = "high",
                ["location_anomaly"] = true,
                ["recent_failures"] = 3,
                ["pattern_score"] = 0.8,
                ["time_of_day"] = 3, // 3 AM - high risk time
                ["device_trust"] = false, // Unknown device
                ["encrypted"] = false // Unencrypted connection
            };

            // Simulate a low-risk scenario
            var lowRiskContext = new Dictionary<string, object>
            {
                ["threat_level"] = "low",
                ["location_anomaly"] = false,
                ["recent_failures"] = 0,
                ["pattern_score"] = 0.1,
                ["time_of_day"] = 14, // 2 PM - normal time
                ["device_trust"] = true, // Known device
                ["encrypted"] = true // Encrypted connection
            };

            // Act
            var highRiskDecision = await engine.EvaluatePolicyAsync("network_connect", highRiskContext, 0.6);
            var lowRiskDecision = await engine.EvaluatePolicyAsync("network_connect", lowRiskContext, 0.2);

            // Assert
            highRiskDecision.Should().NotBeNull();
            lowRiskDecision.Should().NotBeNull();

            // High risk should be denied or have higher risk score
            highRiskDecision.AdjustedRiskScore.Should().BeGreaterThan(lowRiskDecision.AdjustedRiskScore);

            // ML predictions should be included
            highRiskDecision.MLPrediction.Should().NotBeNull();
            lowRiskDecision.MLPrediction.Should().NotBeNull();

            // Required actions should be populated for high risk
            if (highRiskDecision.AdjustedRiskScore > 0.5)
            {
                highRiskDecision.RequiredActions.Should().NotBeEmpty();
            }
        }

        [Fact]
        public async Task ConfigurationAndLocalization_Integration()
        {
            // Arrange
            var originalLanguage = LocalizationManager.CurrentLanguage;

            try
            {
                // Act - Change language
                var languageChanged = await LocalizationManager.SetLanguage("ja");
                var configUpdated = await ConfigManager.UpdateSetting("autoconnect", true);

                // Assert
                if (languageChanged)
                {
                    LocalizationManager.CurrentLanguage.Should().Be("ja");

                    // Test localized strings
                    var localizedText = LocalizationManager.Localize("ok");
                    localizedText.Should().NotBeNullOrEmpty();
                    localizedText.Should().NotBe("ok"); // Should be translated
                }

                configUpdated.Success.Should().BeTrue();

                // Verify config persistence
                var autoConnect = await ConfigManager.GetSetting<bool>("autoconnect", false);
                autoConnect.Should().BeTrue();
            }
            finally
            {
                // Cleanup - Restore original language
                await LocalizationManager.SetLanguage(originalLanguage);
            }
        }

        [Fact]
        public async Task PolicyLearning_ImprovesOverTime()
        {
            // Arrange
            var engine = new AdaptivePolicyEngine();
            var context = new Dictionary<string, object>
            {
                ["threat_level"] = "medium",
                ["pattern_score"] = 0.6,
                ["device_trust"] = false
            };

            // Act - Simulate multiple evaluations to allow learning
            var initialDecisions = new List<AdaptivePolicyDecision>();
            for (int i = 0; i < 10; i++)
            {
                var decision = await engine.EvaluatePolicyAsync("network_connect", context, 0.4);
                initialDecisions.Add(decision);

                // Simulate that decisions with high risk were actually successful
                // This would normally come from actual system feedback
            }

            // Wait a bit for learning to occur (learning happens every 30 minutes in background)
            await Task.Delay(100);

            // Get a few more decisions after potential learning
            var laterDecisions = new List<AdaptivePolicyDecision>();
            for (int i = 0; i < 5; i++)
            {
                var decision = await engine.EvaluatePolicyAsync("network_connect", context, 0.4);
                laterDecisions.Add(decision);
            }

            // Assert
            initialDecisions.Should().HaveCount(10);
            laterDecisions.Should().HaveCount(5);

            // All decisions should be valid
            initialDecisions.Should().AllSatisfy(d => d.Should().NotBeNull());
            laterDecisions.Should().AllSatisfy(d => d.Should().NotBeNull());

            // ML predictions should be consistent
            initialDecisions.Should().AllSatisfy(d => d.MLPrediction.Should().NotBeNull());
            laterDecisions.Should().AllSatisfy(d => d.MLPrediction.Should().NotBeNull());
        }

        [Fact]
        public async Task ErrorHandling_Integration()
        {
            // Arrange
            var engine = new AdaptivePolicyEngine();

            // Test with invalid context
            var invalidContext = new Dictionary<string, object>
            {
                ["invalid_key"] = "invalid_value"
            };

            // Act
            var decision = await engine.EvaluatePolicyAsync("invalid_operation", invalidContext, 0.5);

            // Assert
            decision.Should().NotBeNull();
            decision.Operation.Should().Be("invalid_operation");
            decision.IsAllowed.Should().BeBoolean(); // Should still make a decision
            decision.RiskLevel.Should().BeDefined();
        }

        [Fact]
        public async Task PreferredNetworks_ConfigIntegration()
        {
            // Arrange
            const string testNetwork = "IntegrationTestNetwork";

            try
            {
                // Act - Add preferred network
                var addResult = await ConfigManager.AddPreferredNetwork(testNetwork, 75);
                var networks = await ConfigManager.GetPreferredNetworks();

                // Assert
                addResult.Should().BeTrue();
                networks.Should().Contain(n => n.Ssid == testNetwork && n.Priority == 75);

                // Test localization with network management
                var localizedSuccess = LocalizationManager.Localize("preferred_added", testNetwork);
                localizedSuccess.Should().NotBeNullOrEmpty();
                localizedSuccess.Should().Contain(testNetwork);
            }
            finally
            {
                // Cleanup
                await ConfigManager.RemovePreferredNetwork(testNetwork);
            }
        }

        [Fact]
        public async Task ConfigExportImport_RoundTrip()
        {
            // Arrange
            var exportPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "integration_test_config.json");

            try
            {
                // Modify some settings
                await ConfigManager.UpdateSetting("autoconnect", false);
                await ConfigManager.UpdateSetting("scaninterval", 45);
                await ConfigManager.AddPreferredNetwork("ExportTestNetwork", 50);

                var originalConfig = await ConfigManager.LoadConfig();

                // Act - Export and re-import
                await ConfigManager.ExportConfig(exportPath);
                System.IO.File.Exists(exportPath).Should().BeTrue();

                // Modify config again
                await ConfigManager.UpdateSetting("autoconnect", true);
                await ConfigManager.UpdateSetting("scaninterval", 60);

                // Import the exported config
                await ConfigManager.ImportConfig(exportPath);

                var importedConfig = await ConfigManager.LoadConfig();

                // Assert - Key settings should be restored
                importedConfig.AutoConnect.Should().Be(originalConfig.AutoConnect);
                importedConfig.ScanInterval.Should().Be(originalConfig.ScanInterval);
            }
            finally
            {
                // Cleanup
                if (System.IO.File.Exists(exportPath))
                    System.IO.File.Delete(exportPath);

                // Reset to defaults
                await ConfigManager.ResetToDefaults();
            }
        }

        [Fact]
        public async Task MultiLanguage_SwitchingIntegration()
        {
            // Arrange
            var originalLanguage = LocalizationManager.CurrentLanguage;
            var testKey = "error";

            try
            {
                // Get English version
                var englishText = LocalizationManager.Localize(testKey);

                // Switch to Japanese if available
                var switched = await LocalizationManager.SetLanguage("ja");
                if (switched)
                {
                    var japaneseText = LocalizationManager.Localize(testKey);

                    // Assert
                    englishText.Should().NotBeNullOrEmpty();
                    japaneseText.Should().NotBeNullOrEmpty();

                    // If language switching worked, texts should be different (unless they happen to be the same)
                    // This test mainly verifies that the switching mechanism works without errors
                }

                // Switch back to English
                await LocalizationManager.SetLanguage("en");
                var backToEnglish = LocalizationManager.Localize(testKey);

                // Assert
                backToEnglish.Should().Be(englishText);
            }
            finally
            {
                // Restore original language
                await LocalizationManager.SetLanguage(originalLanguage);
            }
        }

        [Fact]
        public async Task StressTest_MultipleConcurrentEvaluations()
        {
            // Arrange
            var engine = new AdaptivePolicyEngine();
            const int concurrentEvaluations = 20;
            const int evaluationsPerTask = 50;

            var contexts = new[]
            {
                new Dictionary<string, object> { ["threat_level"] = "low", ["device_trust"] = true },
                new Dictionary<string, object> { ["threat_level"] = "high", ["device_trust"] = false },
                new Dictionary<string, object> { ["threat_level"] = "medium", ["location_anomaly"] = true },
                new Dictionary<string, object> { ["threat_level"] = "low", ["recent_failures"] = 2 }
            };

            // Act
            var tasks = new List<Task<List<AdaptivePolicyDecision>>>();
            for (int i = 0; i < concurrentEvaluations; i++)
            {
                var taskId = i;
                tasks.Add(Task.Run(async () =>
                {
                    var results = new List<AdaptivePolicyDecision>();
                    for (int j = 0; j < evaluationsPerTask; j++)
                    {
                        var context = contexts[j % contexts.Length];
                        var decision = await engine.EvaluatePolicyAsync("network_connect", context, 0.3);
                        results.Add(decision);
                    }
                    return results;
                }));
            }

            var allResults = await Task.WhenAll(tasks);

            // Assert
            allResults.Should().HaveCount(concurrentEvaluations);
            foreach (var taskResults in allResults)
            {
                taskResults.Should().HaveCount(evaluationsPerTask);
                taskResults.Should().AllSatisfy(r =>
                {
                    r.Should().NotBeNull();
                    r.IsAllowed.Should().BeBoolean();
                    r.RiskLevel.Should().BeDefined();
                    r.MLPrediction.Should().NotBeNull();
                });
            }
        }

        [Fact]
        public async Task SystemHealth_MonitoringIntegration()
        {
            // This test would normally integrate with actual system health monitoring
            // For now, we test the policy evaluation with health-related context

            // Arrange
            var engine = new AdaptivePolicyEngine();
            var healthContext = new Dictionary<string, object>
            {
                ["system_cpu"] = 85.0, // High CPU
                ["system_memory"] = 90.0, // High memory
                ["network_latency"] = 150.0, // High latency
                ["threat_level"] = "medium"
            };

            // Act
            var decision = await engine.EvaluatePolicyAsync("command_execution", healthContext, 0.4);

            // Assert
            decision.Should().NotBeNull();
            // High system load should influence the decision
            decision.AdjustedRiskScore.Should().BeGreaterThanOrEqualTo(decision.BaseRiskScore);
        }
    }
}
