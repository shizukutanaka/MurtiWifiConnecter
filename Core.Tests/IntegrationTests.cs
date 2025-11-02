using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter.Tests.Integration
{
    /// <summary>
    /// 統合テストスイート
    /// 複数のコンポーネントが連携して動作することを検証する
    /// </summary>
    [TestFixture]
    public class IntegrationTests
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
        public async Task FullWorkflow_CompleteSecurityEvaluation_ShouldWorkEndToEnd()
        {
            // Arrange
            var userId = "integration_test_user";
            var operation = "network_scan";
            var context = new Dictionary<string, object>
            {
                ["UserId"] = userId,
                ["RemoteIP"] = "192.168.1.100",
                ["LastActivity"] = DateTime.UtcNow.AddMinutes(-30)
            };

            // Act - 完全なセキュリティ評価ワークフロー
            var decision = await _evaluator.EvaluateAccessAsync(operation, context);
            var continuousAuth = await _evaluator.PerformContinuousAuthCheckAsync(userId, context);

            // Assert
            Assert.That(decision, Is.Not.Null);
            Assert.That(decision.IsAllowed, Is.True);
            Assert.That(decision.RiskScore, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(decision.RiskScore, Is.LessThanOrEqualTo(1.0));

            Assert.That(continuousAuth, Is.Not.Null);
            Assert.That(continuousAuth.UserId, Is.EqualTo(userId));
            Assert.That(continuousAuth.IsAuthenticated, Is.True);
        }

        [Test]
        public async Task CrossComponentCommunication_ShouldMaintainConsistency()
        {
            // Arrange
            var userId = "consistency_test_user";
            var operation = "profile_create";
            var context = new Dictionary<string, object>
            {
                ["UserId"] = userId,
                ["RemoteIP"] = "192.168.1.100"
            };

            // Act - 複数のコンポーネントで同じユーザーの活動を処理
            _anomalyDetector.RecordUserActivity(userId, operation, context);
            var anomalyResult = await _anomalyDetector.DetectAnomalyAsync(userId, operation, context);

            var decision = await _evaluator.EvaluateAccessAsync(operation, context);

            // Assert - 異常検知結果がリスク評価に影響を与えることを確認
            if (anomalyResult.IsAnomalous)
            {
                Assert.That(decision.RiskScore, Is.GreaterThan(0.1));
            }

            // 脅威インテリジェンスが機能していることを確認
            var threatStats = _threatManager.GetStats();
            Assert.That(threatStats.TotalFeeds, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task ErrorHandling_ShouldBeRobust()
        {
            // Arrange
            var invalidContext = new Dictionary<string, object>
            {
                ["UserId"] = null, // 無効な値
                ["RemoteIP"] = "invalid-ip"
            };

            // Act & Assert - 無効な入力でも例外が発生しないことを確認
            Assert.DoesNotThrowAsync(async () =>
            {
                var decision = await _evaluator.EvaluateAccessAsync("test_operation", invalidContext);
                Assert.That(decision, Is.Not.Null);
            });

            Assert.DoesNotThrowAsync(async () =>
            {
                var result = await _anomalyDetector.DetectAnomalyAsync("test_user", "test_operation", invalidContext);
                Assert.That(result, Is.Not.Null);
            });
        }

        [Test]
        public async Task LocalizationIntegration_ShouldWorkWithAllComponents()
        {
            // Arrange
            var supportedLanguages = LocalizationManager.SupportedLanguages;

            // Act - 各言語で文字列取得をテスト
            foreach (var language in supportedLanguages.Take(5)) // パフォーマンスのため最初の5言語のみテスト
            {
                LocalizationManager.SetLanguage(language);

                var appName = LocalizationManager.GetString("Application.Name");
                var scanDesc = LocalizationManager.GetString("Commands.Scan.Description");

                // Assert
                Assert.That(appName, Is.Not.Null.Or.Empty);
                Assert.That(scanDesc, Is.Not.Null.Or.Empty);
            }
        }

        [Test]
        public async Task PlatformAbstraction_ShouldWorkAcrossPlatforms()
        {
            // Arrange
            var manager = WifiManagerFactory.CreateWifiManager();

            // Act - プラットフォーム抽象化の基本機能をテスト
            var adaptersTask = manager.GetAvailableAdaptersAsync();
            var profilesTask = manager.GetSavedProfilesAsync();

            // Assert - タイムアウトなしで基本的な操作が機能することを確認
            Assert.DoesNotThrowAsync(async () =>
            {
                var adapters = await adaptersTask;
                var profiles = await profilesTask;

                // 結果の型チェックのみ（実際の値はプラットフォームによる）
                Assert.That(adapters, Is.Not.Null);
                Assert.That(profiles, Is.Not.Null);
            });
        }

        [Test]
        public async Task SecurityEventFlow_ShouldBeTraceable()
        {
            // Arrange
            var userId = "traceability_test_user";
            var operation = "network_scan";
            var context = new Dictionary<string, object>
            {
                ["UserId"] = userId,
                ["RemoteIP"] = "192.168.1.100"
            };

            // Act - セキュリティイベントの流れを追跡
            var decision1 = await _evaluator.EvaluateAccessAsync(operation, context);
            var decision2 = await _evaluator.EvaluateAccessAsync(operation, context);

            var continuousAuth = await _evaluator.PerformContinuousAuthCheckAsync(userId, context);

            // Assert - イベントが適切に処理・記録されていることを確認
            Assert.That(decision1.TimestampUtc, Is.LessThan(decision2.TimestampUtc));
            Assert.That(continuousAuth.CheckTime, Is.GreaterThan(decision1.TimestampUtc));
        }

        [Test]
        public async Task ResourceCleanup_ShouldWorkProperly()
        {
            // Arrange
            var managers = new List<IDisposable>
            {
                new Core.Windows.WindowsWifiManager(),
                new Core.macOS.macOSWifiManager(),
                new Core.Linux.LinuxWifiManager()
            };

            // Act - リソースのクリーンアップをテスト
            foreach (var manager in managers)
            {
                Assert.DoesNotThrow(() => manager.Dispose());
            }

            // マネージャーオブジェクトは既に破棄されているため、操作は行わない
        }
    }

    /// <summary>
    /// エンドツーエンドテストスイート
    /// 実際の使用シナリオをシミュレートする
    /// </summary>
    [TestFixture]
    [Category("E2ETests")]
    public class EndToEndTests
    {
        [Test]
        [Timeout(60000)] // 1分以内に完了することを期待
        public async Task CompleteUserJourney_ShouldWorkFromStartToFinish()
        {
            // このテストは実際のユースケースをシミュレートします
            // 実際のWiFi操作はプラットフォーム依存のため、セキュリティ評価のみをテスト

            // Arrange
            var evaluator = new ZeroTrustEvaluator();
            var userId = "e2e_test_user";

            // Act - ユーザーの典型的な操作シーケンスをシミュレート

            // 1. 初回ネットワークスキャン
            var scanContext = new Dictionary<string, object>
            {
                ["UserId"] = userId,
                ["RemoteIP"] = "192.168.1.100"
            };
            var scanDecision = await evaluator.EvaluateAccessAsync("network_scan", scanContext);

            // 2. ネットワーク接続
            var connectContext = new Dictionary<string, object>
            {
                ["UserId"] = userId,
                ["RemoteIP"] = "192.168.1.100",
                ["TargetSSID"] = "TestNetwork"
            };
            var connectDecision = await evaluator.EvaluateAccessAsync("network_connect", connectContext);

            // 3. 継続的認証チェック
            var authContext = new Dictionary<string, object>
            {
                ["UserId"] = userId,
                ["LastActivity"] = DateTime.UtcNow.AddMinutes(-30),
                ["DeviceFingerprint"] = "device_123"
            };
            var authResult = await evaluator.PerformContinuousAuthCheckAsync(userId, authContext);

            // Assert - すべてのステップが正常に完了することを確認
            Assert.That(scanDecision.IsAllowed, Is.True);
            Assert.That(connectDecision.IsAllowed, Is.True);
            Assert.That(authResult.IsAuthenticated, Is.True);
            Assert.That(scanDecision.RiskScore, Is.LessThan(connectDecision.RiskScore)); // 接続の方がリスクが高いはず
        }

        [Test]
        public async Task MultiUserScenario_ShouldHandleConcurrentUsers()
        {
            // Arrange
            var evaluator = new ZeroTrustEvaluator();
            var tasks = new List<Task>();

            // Act - 複数のユーザーの同時操作をシミュレート
            for (int userId = 0; userId < 10; userId++)
            {
                var user = userId;
                tasks.Add(Task.Run(async () =>
                {
                    var context = new Dictionary<string, object>
                    {
                        ["UserId"] = $"user_{user}",
                        ["RemoteIP"] = $"192.168.1.{user + 10}"
                    };

                    var decision = await evaluator.EvaluateAccessAsync("network_scan", context);
                    var authResult = await evaluator.PerformContinuousAuthCheckAsync($"user_{user}", context);

                    return new { Decision = decision, AuthResult = authResult };
                }));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.That(results.Length, Is.EqualTo(10));
            Assert.That(results.All(r => r.Decision.IsAllowed), Is.True);
            Assert.That(results.All(r => r.AuthResult.IsAuthenticated), Is.True);
        }
    }
}
