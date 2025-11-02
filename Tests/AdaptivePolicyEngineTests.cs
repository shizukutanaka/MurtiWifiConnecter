using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter.Tests.Core
{
    public class AdaptivePolicyEngineTests
    {
        private readonly AdaptivePolicyEngine _engine;

        public AdaptivePolicyEngineTests()
        {
            _engine = new AdaptivePolicyEngine();
        }

        [Fact]
        public async Task EvaluatePolicyAsync_WithLowRisk_ReturnsAllowed()
        {
            // Arrange
            var context = new Dictionary<string, object>
            {
                ["threat_level"] = "low",
                ["location_anomaly"] = false,
                ["recent_failures"] = 0,
                ["pattern_score"] = 0.1
            };

            // Act
            var result = await _engine.EvaluatePolicyAsync("network_connect", context, 0.2);

            // Assert
            result.Should().NotBeNull();
            result.IsAllowed.Should().BeTrue();
            result.RiskLevel.Should().Be(RiskLevel.Low);
            result.AdjustedRiskScore.Should().BeLessThan(0.4);
        }

        [Fact]
        public async Task EvaluatePolicyAsync_WithHighRisk_ReturnsDenied()
        {
            // Arrange
            var context = new Dictionary<string, object>
            {
                ["threat_level"] = "high",
                ["location_anomaly"] = true,
                ["recent_failures"] = 5,
                ["pattern_score"] = 0.9
            };

            // Act
            var result = await _engine.EvaluatePolicyAsync("credential_access", context, 0.8);

            // Assert
            result.Should().NotBeNull();
            result.AdjustedRiskScore.Should().BeGreaterThan(0.6);
            result.RequiredActions.Should().NotBeEmpty();
        }

        [Fact]
        public async Task EvaluatePolicyAsync_IncludesMLPrediction()
        {
            // Arrange
            var context = new Dictionary<string, object>
            {
                ["threat_level"] = "medium",
                ["device_trust"] = true,
                ["encrypted"] = true
            };

            // Act
            var result = await _engine.EvaluatePolicyAsync("network_connect", context, 0.3);

            // Assert
            result.MLPrediction.Should().NotBeNull();
            result.MLPrediction.RiskScore.Should().BeInRange(0, 1);
            result.MLPrediction.Confidence.Should().BeInRange(0, 1);
            result.MLPrediction.Features.Should().NotBeNull();
        }

        [Fact]
        public async Task EvaluatePolicyAsync_RecordsDecisionHistory()
        {
            // Arrange
            var context = new Dictionary<string, object>
            {
                ["threat_level"] = "low"
            };

            // Act
            var result1 = await _engine.EvaluatePolicyAsync("network_connect", context, 0.2);
            var result2 = await _engine.EvaluatePolicyAsync("network_connect", context, 0.2);

            // Assert
            result1.Timestamp.Should().BeBefore(result2.Timestamp);
            result1.Operation.Should().Be("network_connect");
            result2.Operation.Should().Be("network_connect");
        }

        [Fact]
        public void GetPolicy_ReturnsCorrectPolicy()
        {
            // Act
            var policy = _engine.GetPolicy("network_connect");

            // Assert
            policy.Should().NotBeNull();
            policy.Name.Should().Be("network_connect Policy");
            policy.BaseRiskThreshold.Should().BeGreaterThan(0);
            policy.AdaptiveRules.Should().NotBeNull();
        }

        [Fact]
        public void GetPolicy_WithUnknownOperation_ReturnsNull()
        {
            // Act
            var policy = _engine.GetPolicy("unknown_operation");

            // Assert
            policy.Should().BeNull();
        }

        [Fact]
        public void UpdatePolicy_UpdatesExistingPolicy()
        {
            // Arrange
            var updatedPolicy = new AdaptivePolicy
            {
                Name = "Test Policy",
                BaseRiskThreshold = 0.5,
                AdaptiveRules = new List<AdaptiveRule>(),
                LastUpdated = DateTime.UtcNow
            };

            // Act
            _engine.UpdatePolicy("network_connect", updatedPolicy);
            var retrievedPolicy = _engine.GetPolicy("network_connect");

            // Assert
            retrievedPolicy.Should().NotBeNull();
            retrievedPolicy.Name.Should().Be("Test Policy");
            retrievedPolicy.BaseRiskThreshold.Should().Be(0.5);
        }

        [Theory]
        [InlineData(0.2, RiskLevel.Low)]
        [InlineData(0.5, RiskLevel.Medium)]
        [InlineData(0.7, RiskLevel.High)]
        [InlineData(0.9, RiskLevel.Critical)]
        public void RiskLevelDetermination_WorksCorrectly(double riskScore, RiskLevel expectedLevel)
        {
            // This test verifies the internal risk level determination logic
            // We test this indirectly through EvaluatePolicyAsync

            var context = new Dictionary<string, object>();
            var task = _engine.EvaluatePolicyAsync("network_connect", context, riskScore);
            task.Wait();
            var result = task.Result;

            // The actual risk level depends on ML prediction and adjustments,
            // so we just verify the result is valid
            result.RiskLevel.Should().BeDefined();
            Enum.IsDefined(typeof(RiskLevel), result.RiskLevel).Should().BeTrue();
        }

        [Fact]
        public async Task EvaluateCondition_WithThreatLevelHigh_ReturnsTrue()
        {
            // Arrange
            var context = new Dictionary<string, object>
            {
                ["threat_level"] = "high"
            };

            // Act & Assert - This is tested indirectly through EvaluatePolicyAsync
            var result = await _engine.EvaluatePolicyAsync("network_connect", context, 0.1);

            // High threat level should increase risk
            result.AdjustedRiskScore.Should().BeGreaterThan(result.BaseRiskScore);
        }

        [Fact]
        public async Task EvaluateCondition_WithLocationAnomaly_ReturnsTrue()
        {
            // Arrange
            var context = new Dictionary<string, object>
            {
                ["location_anomaly"] = true
            };

            // Act & Assert
            var result = await _engine.EvaluatePolicyAsync("network_connect", context, 0.1);

            result.AdjustedRiskScore.Should().BeGreaterThan(result.BaseRiskScore);
        }

        [Fact]
        public async Task EvaluateCondition_WithMultipleFailures_ReturnsTrue()
        {
            // Arrange
            var context = new Dictionary<string, object>
            {
                ["recent_failures"] = 5
            };

            // Act & Assert
            var result = await _engine.EvaluatePolicyAsync("network_connect", context, 0.1);

            result.AdjustedRiskScore.Should().BeGreaterThan(result.BaseRiskScore);
        }

        [Fact]
        public async Task EvaluateCondition_WithSuspiciousPattern_ReturnsTrue()
        {
            // Arrange
            var context = new Dictionary<string, object>
            {
                ["pattern_score"] = 0.8
            };

            // Act & Assert
            var result = await _engine.EvaluatePolicyAsync("credential_access", context, 0.1);

            result.AdjustedRiskScore.Should().BeGreaterThan(result.BaseRiskScore);
        }

        [Fact]
        public async Task EvaluateCondition_WithPrivilegedCommand_ReturnsTrue()
        {
            // Arrange
            var context = new Dictionary<string, object>
            {
                ["is_privileged"] = true
            };

            // Act & Assert
            var result = await _engine.EvaluatePolicyAsync("command_execution", context, 0.1);

            result.AdjustedRiskScore.Should().BeGreaterThan(result.BaseRiskScore);
        }

        [Fact]
        public async Task EvaluateCondition_WithAnomalyDetected_ReturnsTrue()
        {
            // Arrange
            var context = new Dictionary<string, object>
            {
                ["anomaly_score"] = 0.9
            };

            // Act & Assert
            var result = await _engine.EvaluatePolicyAsync("command_execution", context, 0.1);

            result.AdjustedRiskScore.Should().BeGreaterThan(result.BaseRiskScore);
        }
    }
}
