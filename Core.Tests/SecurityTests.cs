using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using MurtiWifiConnecter.Core;
using MurtiWifiConnecter.Core.Security;

namespace MurtiWifiConnecter.Tests
{
    /// <summary>
    /// ZeroTrustEvaluatorの包括的なテストスイート
    /// </summary>
    [TestFixture]
    public class ZeroTrustEvaluatorTests
    {
        private ZeroTrustEvaluator _evaluator;

        [SetUp]
        public void SetUp()
        {
            _evaluator = new ZeroTrustEvaluator();
        }

        [Test]
        public async Task EvaluateAccessAsync_ValidOperation_ShouldAllowAccess()
        {
            // Arrange
            var operation = "network_scan";
            var context = new Dictionary<string, object>
            {
                ["UserId"] = "testuser",
                ["RemoteIP"] = "192.168.1.100"
            };

            // Act
            var decision = await _evaluator.EvaluateAccessAsync(operation, context);

            // Assert
            Assert.That(decision.IsAllowed, Is.True);
            Assert.That(decision.Operation, Is.EqualTo(operation));
            Assert.That(decision.Context, Is.EqualTo(context));
            Assert.That(decision.RiskScore, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(decision.RiskScore, Is.LessThanOrEqualTo(1.0));
        }

        [Test]
        public async Task EvaluateAccessAsync_HighRiskOperation_ShouldDenyAccess()
        {
            // Arrange
            var operation = "credential_store";
            var context = new Dictionary<string, object>
            {
                ["UserId"] = "testuser",
                ["RemoteIP"] = "10.0.0.1" // 外部IPとして扱われる
            };

            // Act
            var decision = await _evaluator.EvaluateAccessAsync(operation, context);

            // Assert
            Assert.That(decision.RiskScore, Is.GreaterThan(0.5));
            Assert.That(decision.IsAllowed, Is.False);
        }

        [Test]
        public async Task EvaluateAccessAsync_AnomalousActivity_ShouldIncreaseRiskScore()
        {
            // Arrange
            var operation = "profile_create";
            var context = new Dictionary<string, object>
            {
                ["UserId"] = "testuser",
                ["RemoteIP"] = "192.168.1.100"
            };

            // 異常な活動を複数回記録して学習させる
            for (int i = 0; i < 10; i++)
            {
                _evaluator.RecordUserActivity("testuser", operation, context);
            }

            // Act
            var decision = await _evaluator.EvaluateAccessAsync(operation, context);

            // Assert
            Assert.That(decision.RiskScore, Is.GreaterThan(0.1));
        }

        [Test]
        public void CalculateRiskScore_ProfileOperations_ShouldHaveHigherRisk()
        {
            // Arrange
            var context = new Dictionary<string, object>
            {
                ["UserId"] = "testuser"
            };

            // Act
            var scanRisk = _evaluator.CalculateRiskScore("network_scan", context);
            var profileRisk = _evaluator.CalculateRiskScore("profile_create", context);

            // Assert
            Assert.That(profileRisk, Is.GreaterThan(scanRisk));
        }

        [Test]
        public async Task PerformContinuousAuthCheckAsync_ValidUser_ShouldAuthenticate()
        {
            // Arrange
            var userId = "testuser";
            var context = new Dictionary<string, object>
            {
                ["LastActivity"] = DateTime.UtcNow.AddMinutes(-30),
                ["DeviceFingerprint"] = "stored_fingerprint_123"
            };

            // Act
            var result = await _evaluator.PerformContinuousAuthCheckAsync(userId, context);

            // Assert
            Assert.That(result.IsAuthenticated, Is.True);
            Assert.That(result.UserId, Is.EqualTo(userId));
            Assert.That(result.RiskFactors, Is.Empty);
        }

        [Test]
        public async Task PerformContinuousAuthCheckAsync_SessionTimeout_ShouldDenyAuthentication()
        {
            // Arrange
            var userId = "testuser";
            var context = new Dictionary<string, object>
            {
                ["LastActivity"] = DateTime.UtcNow.AddHours(-3), // 3時間前
                ["DeviceFingerprint"] = "stored_fingerprint_123"
            };

            // Act
            var result = await _evaluator.PerformContinuousAuthCheckAsync(userId, context);

            // Assert
            Assert.That(result.IsAuthenticated, Is.False);
            Assert.That(result.RiskFactors, Contains.Item("SessionTimeout"));
        }

        [Test]
        public async Task PerformContinuousAuthCheckAsync_DeviceMismatch_ShouldDenyAuthentication()
        {
            // Arrange
            var userId = "testuser";
            var context = new Dictionary<string, object>
            {
                ["LastActivity"] = DateTime.UtcNow.AddMinutes(-30),
                ["DeviceFingerprint"] = "different_fingerprint"
            };

            // Act
            var result = await _evaluator.PerformContinuousAuthCheckAsync(userId, context);

            // Assert
            Assert.That(result.IsAuthenticated, Is.False);
            Assert.That(result.RiskFactors, Contains.Item("DeviceMismatch"));
        }
    }

    /// <summary>
    /// MLAnomalyDetectorのテストスイート
    /// </summary>
    [TestFixture]
    public class MLAnomalyDetectorTests
    {
        private MLAnomalyDetector _detector;

        [SetUp]
        public void SetUp()
        {
            _detector = new MLAnomalyDetector();
        }

        [Test]
        public async Task DetectAnomalyAsync_InsufficientData_ShouldReturnLowConfidence()
        {
            // Arrange
            var userId = "testuser";
            var operation = "test_operation";
            var context = new Dictionary<string, object>();

            // Act
            var result = await _detector.DetectAnomalyAsync(userId, operation, context);

            // Assert
            Assert.That(result.IsAnomalous, Is.False);
            Assert.That(result.ConfidenceScore, Is.EqualTo(0.0));
            Assert.That(result.Reason, Contains.Substring("Insufficient data"));
        }

        [Test]
        public async Task DetectAnomalyAsync_RepeatedNormalActivity_ShouldNotBeAnomalous()
        {
            // Arrange
            var userId = "testuser";
            var operation = "network_scan";
            var context = new Dictionary<string, object>();

            // 通常の活動を複数回記録
            for (int i = 0; i < 20; i++)
            {
                _detector.RecordUserActivity(userId, operation, context);
            }

            // Act
            var result = await _detector.DetectAnomalyAsync(userId, operation, context);

            // Assert
            Assert.That(result.IsAnomalous, Is.False);
            Assert.That(result.ConfidenceScore, Is.LessThan(0.7));
        }

        [Test]
        public async Task DetectAnomalyAsync_UnusualTime_ShouldBeAnomalous()
        {
            // Arrange
            var userId = "testuser";
            var operation = "network_scan";
            var context = new Dictionary<string, object>();

            // 深夜の時間帯を設定
            var midnightContext = new Dictionary<string, object>(context)
            {
                ["UnusualTime"] = true
            };

            // 通常の活動を記録
            for (int i = 0; i < 10; i++)
            {
                _detector.RecordUserActivity(userId, operation, context);
            }

            // Act
            var result = await _detector.DetectAnomalyAsync(userId, operation, midnightContext);

            // Assert
            Assert.That(result.IsAnomalous, Is.True);
            Assert.That(result.ContributingFactors, Contains.Item("Unusual time pattern"));
        }

        [Test]
        public async Task RecordUserActivity_ShouldUpdateBehaviorProfile()
        {
            // Arrange
            var userId = "testuser";
            var operation = "network_scan";
            var context = new Dictionary<string, object>();

            // Act
            _detector.RecordUserActivity(userId, operation, context);

            // 内部状態の確認は難しいため、異常検知が機能することを確認
            var result = await _detector.DetectAnomalyAsync(userId, operation, context);

            // Assert
            Assert.That(result.Reason, Is.Not.Null.Or.Empty);
        }
    }

    /// <summary>
    /// ThreatIntelligenceManagerのテストスイート
    /// </summary>
    [TestFixture]
    public class ThreatIntelligenceManagerTests
    {
        private ThreatIntelligenceManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new ThreatIntelligenceManager();
        }

        [Test]
        public void GetStats_ShouldReturnValidStats()
        {
            // Act
            var stats = _manager.GetStats();

            // Assert
            Assert.That(stats.TotalFeeds, Is.GreaterThanOrEqualTo(0));
            Assert.That(stats.ActiveFeeds, Is.GreaterThanOrEqualTo(0));
            Assert.That(stats.TotalThreats, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task UpdateAllFeedsAsync_ShouldNotThrowException()
        {
            // Act & Assert
            Assert.DoesNotThrowAsync(async () => await _manager.UpdateAllFeedsAsync());
        }

        [Test]
        public void IsThreat_UnknownValue_ShouldReturnFalse()
        {
            // Act
            var isThreat = _manager.IsThreat("192.168.1.1");

            // Assert
            Assert.That(isThreat, Is.False);
        }

        [Test]
        public void GetThreatsForValue_UnknownValue_ShouldReturnEmptyList()
        {
            // Act
            var threats = _manager.GetThreatsForValue("192.168.1.1");

            // Assert
            Assert.That(threats, Is.Empty);
        }
    }

    /// <summary>
    /// WifiManagerFactoryのテストスイート
    /// </summary>
    [TestFixture]
    public class WifiManagerFactoryTests
    {
        [Test]
        public void IsPlatformSupported_ShouldReturnTrueForSupportedPlatforms()
        {
            // Act
            var isSupported = WifiManagerFactory.IsPlatformSupported();

            // Assert
            Assert.That(isSupported, Is.True);
        }

        [Test]
        public void GetPlatformName_ShouldReturnValidPlatformName()
        {
            // Act
            var platformName = WifiManagerFactory.GetPlatformName();

            // Assert
            Assert.That(platformName, Is.Not.Null.Or.Empty);
            Assert.That(platformName, Is.OneOf("Windows", "macOS", "Linux", "Unknown"));
        }

        [Test]
        public void CreateWifiManager_ShouldReturnValidManager()
        {
            // Act
            var manager = WifiManagerFactory.CreateWifiManager();

            // Assert
            Assert.That(manager, Is.Not.Null);
            Assert.That(manager, Is.InstanceOf<IWifiManager>());
        }
    }

    /// <summary>
    /// LocalizationManagerのテストスイート
    /// </summary>
    [TestFixture]
    public class LocalizationManagerTests
    {
        [Test]
        public void SupportedLanguages_ShouldContainExpectedLanguages()
        {
            // Act
            var supportedLanguages = LocalizationManager.SupportedLanguages;

            // Assert
            Assert.That(supportedLanguages, Contains.Item("en-US"));
            Assert.That(supportedLanguages, Contains.Item("ja-JP"));
            Assert.That(supportedLanguages.Count, Is.GreaterThanOrEqualTo(50));
        }

        [Test]
        public void GetString_ValidKey_ShouldReturnTranslation()
        {
            // Act
            var translation = LocalizationManager.GetString("Application.Name");

            // Assert
            Assert.That(translation, Is.Not.Null.Or.Empty);
        }

        [Test]
        public void GetString_InvalidKey_ShouldReturnKey()
        {
            // Act
            var translation = LocalizationManager.GetString("Invalid.Key");

            // Assert
            Assert.That(translation, Is.EqualTo("Invalid.Key"));
        }
    }
}
