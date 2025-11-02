using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter.Tests
{
    [TestClass]
    public class NetworkDiagnosticsTests
    {
        [TestMethod]
        public async Task PerformFullDiagnosticsAsync_ValidTarget_ReturnsReport()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();

            // Act
            var report = await diagnostics.PerformFullDiagnosticsAsync("8.8.8.8", CancellationToken.None);

            // Assert
            Assert.IsNotNull(report);
            Assert.IsTrue(report.Timestamp <= DateTime.Now);
            Assert.IsNotNull(report.Tests);
            Assert.IsTrue(report.Tests.Count > 0);
            Assert.IsTrue(report.OverallScore >= 0 && report.OverallScore <= 100);
        }

        [TestMethod]
        public async Task TestBasicConnectivityAsync_ValidConnection_ReturnsSuccess()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();

            // Act
            var result = await diagnostics.TestBasicConnectivityAsync(CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(string.IsNullOrEmpty(result.TestName));
            Assert.IsFalse(string.IsNullOrEmpty(result.Description));
            Assert.IsTrue(result.Score >= 0 && result.Score <= 100);
        }

        [TestMethod]
        public async Task TestDnsResolutionAsync_ValidHost_ReturnsResult()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();

            // Act
            var result = await diagnostics.TestDnsResolutionAsync("google.com", CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("DNS Resolution", result.TestName);
            Assert.IsTrue(result.Score >= 0 && result.Score <= 100);
        }

        [TestMethod]
        public async Task TestLatencyAsync_ValidHost_ReturnsLatencyMetrics()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();

            // Act
            var result = await diagnostics.TestLatencyAsync("8.8.8.8", CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Latency Test", result.TestName);
            Assert.IsTrue(result.Score >= 0 && result.Score <= 100);

            if (result.Success && result.Metrics != null)
            {
                Assert.IsTrue(result.Metrics.ContainsKey("averageLatency"));
                Assert.IsTrue(result.Metrics.ContainsKey("minLatency"));
                Assert.IsTrue(result.Metrics.ContainsKey("maxLatency"));
                Assert.IsTrue(result.Metrics.ContainsKey("jitter"));
            }
        }

        [TestMethod]
        public async Task TestPacketLossAsync_ValidHost_ReturnsPacketLossMetrics()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();

            // Act
            var result = await diagnostics.TestPacketLossAsync("8.8.8.8", CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Packet Loss Test", result.TestName);
            Assert.IsTrue(result.Score >= 0 && result.Score <= 100);

            if (result.Metrics != null)
            {
                Assert.IsTrue(result.Metrics.ContainsKey("packetLossRate"));
                Assert.IsTrue(result.Metrics.ContainsKey("successfulPings"));
                Assert.IsTrue(result.Metrics.ContainsKey("totalPings"));
            }
        }

        [TestMethod]
        public async Task TestWifiSpecificAsync_ReturnsWifiStatus()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();

            // Act
            var result = await diagnostics.TestWifiSpecificAsync(CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("WiFi Specific Tests", result.TestName);
            Assert.IsTrue(result.Score >= 0 && result.Score <= 100);
        }

        [TestMethod]
        public async Task TestNetworkAdaptersAsync_ReturnsAdapterInfo()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();

            // Act
            var result = await diagnostics.TestNetworkAdaptersAsync(CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Network Adapters", result.TestName);
            Assert.IsTrue(result.Score >= 0 && result.Score <= 100);

            if (result.Metrics != null)
            {
                Assert.IsTrue(result.Metrics.ContainsKey("totalAdapters"));
                Assert.IsTrue(result.Metrics.ContainsKey("wifiAdapters"));
                Assert.IsTrue(result.Metrics.ContainsKey("activeAdapters"));
            }
        }

        [TestMethod]
        public async Task CalculateOverallScore_ValidTests_ReturnsWeightedScore()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();
            var tests = new List<DiagnosticTestResult>
            {
                new DiagnosticTestResult { TestName = "Test1", Success = true, Score = 80 },
                new DiagnosticTestResult { TestName = "Test2", Success = false, Score = 20 },
                new DiagnosticTestResult { TestName = "Test3", Success = true, Score = 100 }
            };

            // Act
            var overallScore = diagnostics.GetType()
                .GetMethod("CalculateOverallScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.Invoke(null, new object[] { tests }) as int?;

            // Assert
            Assert.IsNotNull(overallScore);
            Assert.IsTrue(overallScore.Value >= 0 && overallScore.Value <= 100);
        }

        [TestMethod]
        public void CalculateLatencyScore_VariousLatencies_ReturnsAppropriateScores()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();
            var calculateMethod = diagnostics.GetType()
                .GetMethod("CalculateLatencyScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            // Act & Assert
            var lowLatencyScore = calculateMethod?.Invoke(null, new object[] { 15.0, 5.0 }) as int?;
            var mediumLatencyScore = calculateMethod?.Invoke(null, new object[] { 75.0, 25.0 }) as int?;
            var highLatencyScore = calculateMethod?.Invoke(null, new object[] { 150.0, 75.0 }) as int?;

            // Assert
            Assert.IsNotNull(lowLatencyScore);
            Assert.IsNotNull(mediumLatencyScore);
            Assert.IsNotNull(highLatencyScore);

            Assert.IsTrue(lowLatencyScore >= mediumLatencyScore);
            Assert.IsTrue(mediumLatencyScore >= highLatencyScore);
        }

        [TestMethod]
        public void CalculatePacketLossScore_VariousLossRates_ReturnsAppropriateScores()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();
            var calculateMethod = diagnostics.GetType()
                .GetMethod("CalculatePacketLossScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            // Act & Assert
            var noLossScore = calculateMethod?.Invoke(null, new object[] { 0.0 }) as int?;
            var lowLossScore = calculateMethod?.Invoke(null, new object[] { 2.0 }) as int?;
            var highLossScore = calculateMethod?.Invoke(null, new object[] { 15.0 }) as int?;

            // Assert
            Assert.IsNotNull(noLossScore);
            Assert.IsNotNull(lowLossScore);
            Assert.IsNotNull(highLossScore);

            Assert.IsTrue(noLossScore >= lowLossScore);
            Assert.IsTrue(lowLossScore >= highLossScore);
        }

        [TestMethod]
        public void CalculateAdapterScore_VariousAdapterCounts_ReturnsAppropriateScores()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();
            var calculateMethod = diagnostics.GetType()
                .GetMethod("CalculateAdapterScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            // Mock network interfaces (simplified for testing)
            var wifiAdapters = new List<System.Net.NetworkInformation.NetworkInterface>();
            var activeAdapters = new List<System.Net.NetworkInformation.NetworkInterface>();

            // Act & Assert
            var noWifiScore = calculateMethod?.Invoke(null, new object[] { wifiAdapters, activeAdapters }) as int?;
            Assert.IsNotNull(noWifiScore);
            Assert.AreEqual(0, noWifiScore.Value);
        }

        [TestMethod]
        public async Task TestFirewallAsync_ReturnsFirewallStatus()
        {
            // Arrange
            var diagnostics = new NetworkDiagnostics();

            // Act
            var result = await diagnostics.TestFirewallAsync(CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Firewall Check", result.TestName);
            Assert.IsTrue(result.Score >= 0 && result.Score <= 100);
        }
    }
}
