using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using MurtiWifiConnecter.Core;
using MurtiWifiConnecter.Core.Security;

namespace MurtiWifiConnecter.Tests.Performance
{
    /// <summary>
    /// パフォーマンステストスイート
    /// システムの応答性とスケーラビリティを検証する
    /// </summary>
    [TestFixture]
    public class PerformanceTests
    {
        private ZeroTrustEvaluator _evaluator;
        private MLAnomalyDetector _anomalyDetector;
        private ThreatIntelligenceManager _threatManager;

        [SetUp]
        public void SetUp()
        {
            _evaluator = new ZeroTrustEvaluator();
            _anomalyDetector = new MLAnomalyDetector();
            _threatManager = new ThreatIntelligenceManager();
        }

        [Test]
        [Timeout(5000)] // 5秒以内に完了することを期待
        public async Task ZeroTrustEvaluator_EvaluateAccessAsync_ShouldCompleteWithinTimeLimit()
        {
            // Arrange
            var operation = "network_scan";
            var context = new Dictionary<string, object>
            {
                ["UserId"] = "testuser",
                ["RemoteIP"] = "192.168.1.100"
            };

            // Act
            var stopwatch = Stopwatch.StartNew();
            var decision = await _evaluator.EvaluateAccessAsync(operation, context);
            stopwatch.Stop();

            // Assert
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(1000));
            Assert.That(decision, Is.Not.Null);
        }

        [Test]
        public async Task ZeroTrustEvaluator_ConcurrentEvaluations_ShouldHandleConcurrency()
        {
            // Arrange
            var tasks = new List<Task<ZeroTrustDecision>>();
            var context = new Dictionary<string, object>
            {
                ["UserId"] = "testuser",
                ["RemoteIP"] = "192.168.1.100"
            };

            // Act - 複数の同時評価を実行
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(_evaluator.EvaluateAccessAsync($"operation_{i}", context));
            }

            var decisions = await Task.WhenAll(tasks);

            // Assert
            Assert.That(decisions.Length, Is.EqualTo(10));
            Assert.That(decisions.All(d => d != null), Is.True);
        }

        [Test]
        [Timeout(10000)] // 10秒以内に完了することを期待
        public async Task MLAnomalyDetector_BulkActivityRecording_ShouldHandleVolume()
        {
            // Arrange
            var userId = "testuser";
            var operations = Enumerable.Range(1, 100).Select(i => $"operation_{i}").ToArray();

            // Act
            var stopwatch = Stopwatch.StartNew();
            foreach (var operation in operations)
            {
                _anomalyDetector.RecordUserActivity(userId, operation, new Dictionary<string, object>());
            }

            var anomalyResult = await _anomalyDetector.DetectAnomalyAsync(userId, "test_operation", new Dictionary<string, object>());
            stopwatch.Stop();

            // Assert
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000));
            Assert.That(anomalyResult, Is.Not.Null);
        }

        [Test]
        public async Task ThreatIntelligenceManager_UpdateFeeds_ShouldBeEfficient()
        {
            // Act
            var stopwatch = Stopwatch.StartNew();
            await _threatManager.UpdateAllFeedsAsync();
            stopwatch.Stop();

            // Assert
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(30000)); // 30秒以内
        }

        [Test]
        public async Task System_MemoryUsage_ShouldRemainStable()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(true);

            // Act - 負荷をかける
            for (int i = 0; i < 100; i++)
            {
                var context = new Dictionary<string, object>
                {
                    ["UserId"] = $"user_{i}",
                    ["RemoteIP"] = $"192.168.1.{i}"
                };

                await _evaluator.EvaluateAccessAsync("network_scan", context);
                _anomalyDetector.RecordUserActivity($"user_{i}", "network_scan", context);
            }

            // ガベージコレクションを強制実行
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var finalMemory = GC.GetTotalMemory(true);

            // Assert - メモリ使用量が過度に増加していないことを確認
            var memoryIncrease = finalMemory - initialMemory;
            Assert.That(memoryIncrease, Is.LessThan(50 * 1024 * 1024)); // 50MB以内
        }

        [Test]
        public async Task WifiManagerFactory_CreateManager_ShouldBeFast()
        {
            // Act
            var stopwatch = Stopwatch.StartNew();
            var manager = WifiManagerFactory.CreateWifiManager();
            stopwatch.Stop();

            // Assert
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(100));
            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public async Task LocalizationManager_StringRetrieval_ShouldBeFast()
        {
            // Arrange
            var keys = new[] { "Application.Name", "Commands.Scan.Description", "Errors.PermissionDenied" };

            // Act
            var stopwatch = Stopwatch.StartNew();
            foreach (var key in keys)
            {
                var translation = LocalizationManager.GetString(key);
                Assert.That(translation, Is.Not.Null.Or.Empty);
            }
            stopwatch.Stop();

            // Assert
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(100));
        }
    }

    /// <summary>
    /// 負荷テストスイート
    /// 高負荷状況でのシステム動作を検証する
    /// </summary>
    [TestFixture]
    [Category("LoadTests")]
    public class LoadTests
    {
        [Test]
        [Timeout(60000)] // 1分以内に完了することを期待
        public async Task HighVolumeEvaluations_ShouldMaintainPerformance()
        {
            // Arrange
            var evaluator = new ZeroTrustEvaluator();
            var context = new Dictionary<string, object>
            {
                ["UserId"] = "loadtestuser",
                ["RemoteIP"] = "192.168.1.100"
            };

            // Act - 1000回の評価を実行
            var tasks = new List<Task<ZeroTrustDecision>>();
            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(evaluator.EvaluateAccessAsync($"operation_{i}", context));
            }

            var stopwatch = Stopwatch.StartNew();
            var results = await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            Assert.That(results.Length, Is.EqualTo(1000));
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(30000)); // 30秒以内
            Assert.That(results.All(r => r != null), Is.True);
        }

        [Test]
        [Timeout(30000)] // 30秒以内に完了することを期待
        public async Task ConcurrentUserActivity_ShouldHandleConcurrency()
        {
            // Arrange
            var detector = new MLAnomalyDetector();
            var tasks = new List<Task>();

            // Act - 複数のユーザーの活動を同時に記録
            for (int userId = 0; userId < 50; userId++)
            {
                for (int operation = 0; operation < 10; operation++)
                {
                    var user = userId;
                    var op = operation;
                    tasks.Add(Task.Run(() =>
                        detector.RecordUserActivity($"user_{user}", $"operation_{op}", new Dictionary<string, object>())));
                }
            }

            await Task.WhenAll(tasks);

            // 異常検知を実行
            var anomalyTasks = new List<Task>();
            for (int userId = 0; userId < 50; userId++)
            {
                anomalyTasks.Add(detector.DetectAnomalyAsync($"user_{userId}", "test_operation", new Dictionary<string, object>()));
            }

            var stopwatch = Stopwatch.StartNew();
            var anomalyResults = await Task.WhenAll(anomalyTasks);
            stopwatch.Stop();

            // Assert
            Assert.That(anomalyResults.Length, Is.EqualTo(50));
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(10000)); // 10秒以内
        }
    }
}
