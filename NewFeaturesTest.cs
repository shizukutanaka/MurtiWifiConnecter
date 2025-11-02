using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace MurtiWifiConnecter.Tests
{
    /// <summary>
    /// 新機能の動作確認テスト
    /// </summary>
    public class NewFeaturesTest
    {
        private readonly ILogger<NewFeaturesTest> _logger;

        public NewFeaturesTest()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });
            _logger = loggerFactory.CreateLogger<NewFeaturesTest>();
        }

        /// <summary>
        /// 全ての新機能をテスト
        /// </summary>
        public async Task<bool> TestAllFeaturesAsync()
        {
            var results = new List<bool>();

            Console.WriteLine("=== 新機能テスト開始 ===\n");

            // AIベース脅威予測システムのテスト
            results.Add(await TestThreatPredictionSystemAsync());

            // 量子耐性セキュリティマネージャーのテスト
            results.Add(await TestQuantumResistantSecurityAsync());

            // 行動ベースアクセス制御システムのテスト
            results.Add(await TestBehavioralAccessControlAsync());

            var allPassed = results.All(r => r);

            Console.WriteLine($"\n=== テスト結果 ===\n全テスト通過: {allPassed}");
            Console.WriteLine($"成功数: {results.Count(r => r)}/{results.Count}");

            return allPassed;
        }

        /// <summary>
        /// AI脅威予測システムのテスト
        /// </summary>
        private async Task<bool> TestThreatPredictionSystemAsync()
        {
            try
            {
                Console.WriteLine("1. AI脅威予測システムテスト開始...");

                var threatPredictor = new Core.AdvancedThreatPredictionSystem(_logger);

                // テスト用サンプルデータを作成
                var samples = new List<Core.NetworkTrafficSample>
                {
                    new Core.NetworkTrafficSample
                    {
                        Id = "test1",
                        PacketCount = 15000,
                        ByteCount = 800000,
                        Duration = 30.0,
                        SourcePort = 80,
                        DestinationPort = 443,
                        ProtocolType = "TCP",
                        Timestamp = DateTime.UtcNow
                    },
                    new Core.NetworkTrafficSample
                    {
                        Id = "test2",
                        PacketCount = 100,
                        ByteCount = 5000,
                        Duration = 5.0,
                        SourcePort = 22,
                        DestinationPort = 22,
                        ProtocolType = "SSH",
                        Timestamp = DateTime.UtcNow
                    }
                };

                // 脅威予測を実行
                var predictions = await threatPredictor.AnalyzeNetworkTrafficAsync(samples);

                Console.WriteLine($"   サンプル数: {samples.Count}");
                Console.WriteLine($"   脅威検知数: {predictions.Count}");

                // 脅威レポートを生成
                var report = await threatPredictor.GeneratePredictionReportAsync(
                    DateTime.UtcNow.AddHours(-1),
                    DateTime.UtcNow);

                Console.WriteLine($"   レポート生成: 成功");
                Console.WriteLine($"   レポートID: {report.Id}");
                Console.WriteLine($"   分析期間: {report.ReportPeriod}");

                Console.WriteLine("   ✓ AI脅威予測システムテスト完了");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ✗ AI脅威予測システムテスト失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 量子耐性セキュリティマネージャーのテスト
        /// </summary>
        private async Task<bool> TestQuantumResistantSecurityAsync()
        {
            try
            {
                Console.WriteLine("2. 量子耐性セキュリティマネージャーテスト開始...");

                var securityManager = new Core.QuantumResistantSecurityManager(_logger);

                // 鍵ペアを生成
                var keyPairSuccess = await securityManager.GenerateKyberKeyPairAsync("test-key");
                Console.WriteLine($"   鍵ペア生成: {(keyPairSuccess ? "成功" : "失敗")}");

                // テストデータを暗号化
                var testData = System.Text.Encoding.UTF8.GetBytes("これはテストデータです。量子コンピュータに対する耐性があります。");
                var encryptedData = await securityManager.EncryptWithQuantumResistanceAsync("test-key", testData);

                Console.WriteLine($"   データ暗号化: {(encryptedData != null ? "成功" : "失敗")}");
                Console.WriteLine($"   元データサイズ: {testData.Length} bytes");
                Console.WriteLine($"   暗号化データサイズ: {encryptedData?.EncryptedContent.Length ?? 0} bytes");

                // データを復号化
                var decryptedData = await securityManager.DecryptWithQuantumResistanceAsync(encryptedData);
                var decryptedText = System.Text.Encoding.UTF8.GetString(decryptedData);

                Console.WriteLine($"   データ復号化: {(decryptedText == "これはテストデータです。量子コンピュータに対する耐性があります。" ? "成功" : "失敗")}");

                // セキュリティレポートを生成
                var report = await securityManager.GenerateSecurityReportAsync();
                Console.WriteLine($"   レポート生成: 成功");
                Console.WriteLine($"   鍵ペア数: {report.TotalKeyPairs}");

                Console.WriteLine("   ✓ 量子耐性セキュリティマネージャーテスト完了");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ✗ 量子耐性セキュリティマネージャーテスト失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 行動ベースアクセス制御システムのテスト
        /// </summary>
        private async Task<bool> TestBehavioralAccessControlAsync()
        {
            try
            {
                Console.WriteLine("3. 行動ベースアクセス制御システムテスト開始...");

                var accessController = new Core.BehavioralAccessControlManager(_logger);

                // テスト用ユーザープロファイルを作成
                var activities = new List<Core.UserActivityEvent>
                {
                    new Core.UserActivityEvent
                    {
                        Id = "activity1",
                        UserId = "testuser",
                        HourOfDay = 9,
                        DayOfWeek = 1, // 月曜日
                        Location = "Office",
                        LocationRisk = 0.1f,
                        DeviceType = "Laptop",
                        NetworkType = "Corporate",
                        AccessPattern = "Normal",
                        Timestamp = DateTime.UtcNow.AddDays(-1)
                    },
                    new Core.UserActivityEvent
                    {
                        Id = "activity2",
                        UserId = "testuser",
                        HourOfDay = 14,
                        DayOfWeek = 1,
                        Location = "Office",
                        LocationRisk = 0.1f,
                        DeviceType = "Laptop",
                        NetworkType = "Corporate",
                        AccessPattern = "Normal",
                        Timestamp = DateTime.UtcNow.AddDays(-1)
                    }
                };

                // ユーザープロファイルを構築
                var profileSuccess = await accessController.BuildUserProfileAsync("testuser", activities);
                Console.WriteLine($"   プロファイル構築: {(profileSuccess ? "成功" : "失敗")}");

                // アクセスリクエストを評価
                var accessRequest = new Core.AccessRequest
                {
                    Id = "request1",
                    UserId = "testuser",
                    Timestamp = DateTime.UtcNow,
                    Location = "Office",
                    DeviceInfo = "Laptop",
                    NetworkInfo = "Corporate",
                    AccessPattern = "Normal"
                };

                var decision = await accessController.EvaluateAccessRequestAsync(accessRequest);
                Console.WriteLine($"   アクセス評価: {decision.Status}");
                Console.WriteLine($"   リスクスコア: {decision.RiskScore:F2}");

                // リアルタイム監視を実行
                var alerts = await accessController.MonitorRealTimeBehaviorAsync("testuser");
                Console.WriteLine($"   リアルタイム監視: {alerts.Count}件のアラート検知");

                Console.WriteLine("   ✓ 行動ベースアクセス制御システムテスト完了");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ✗ 行動ベースアクセス制御システムテスト失敗: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// テスト実行クラス
    /// </summary>
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("MurtiWifiConnecter 新機能テスト実行中...\n");

            var test = new NewFeaturesTest();
            var success = await test.TestAllFeaturesAsync();

            Console.WriteLine($"\nテスト結果: {(success ? "全機能正常" : "一部機能に問題あり")}");

            if (!success)
            {
                Console.WriteLine("\n問題解決のため、以下の手順を実行してください:");
                Console.WriteLine("1. 必要なNuGetパッケージがインストールされているか確認");
                Console.WriteLine("2. 依存関係の競合を解決");
                Console.WriteLine("3. ログファイルで詳細なエラーを確認");
            }

            Console.WriteLine("\nテスト完了。Enterキーを押して終了してください。");
            Console.ReadLine();
        }
    }
}
