using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MurtiWifiConnecter.Testing.TestFramework;
using MurtiWifiConnecter.Testing.Mocks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Testing.TestSuites
{
    /// <summary>
    /// WiFiサービステストスイート
    /// WiFi接続機能の包括的テスト
    /// </summary>
    public class WifiServiceTestSuite : ITestSuite
    {
        public string Name => "WiFiサービステスト";

        private MockWifiService _mockWifiService;
        private MockLoggingService _mockLoggingService;
        private MockProfileService _mockProfileService;

        public List<ITest> GetTests()
        {
            return new List<ITest>
            {
                new NetworkScanTest(),
                new BasicConnectionTest(),
                new ConnectionFailureTest(),
                new SignalStrengthVariationTest(),
                new MultipleNetworksTest(),
                new ConnectionTimeoutTest(),
                new ProfileManagementTest(),
                new DisconnectionTest(),
                new NetworkNotFoundTest(),
                new ConcurrentOperationTest()
            };
        }

        public async Task SetupAsync()
        {
            _mockWifiService = new MockWifiService();
            _mockLoggingService = new MockLoggingService();
            _mockProfileService = new MockProfileService();
            
            // テスト用ネットワークの追加
            _mockWifiService.AddTestNetwork(new WifiNetwork 
            { 
                SSID = "TestCorporate", 
                SignalStrength = 95, 
                SecurityType = "WPA2-Enterprise" 
            });
            
            _mockWifiService.SetConnectionResult("TestCorporate", true);
            _mockWifiService.SetConnectionDelay("TestCorporate", TimeSpan.FromMilliseconds(800));
            
            await Task.CompletedTask;
        }

        public async Task TeardownAsync()
        {
            _mockWifiService?.Reset();
            _mockLoggingService?.ClearLogs();
            _mockProfileService?.ClearAllProfiles();
            
            await Task.CompletedTask;
        }

        public async Task TestSetupAsync()
        {
            _mockWifiService.Reset();
            await Task.CompletedTask;
        }

        public async Task TestTeardownAsync()
        {
            await Task.CompletedTask;
        }

        #region Individual Tests

        /// <summary>
        /// ネットワークスキャンテスト
        /// </summary>
        private class NetworkScanTest : ITest
        {
            public string Name => "ネットワークスキャン基本テスト";

            public async Task ExecuteAsync(TestExecutionContext context)
            {
                var framework = TestingFramework.Instance;
                var wifiService = framework.GetMock<MockWifiService>();

                context.Log("ネットワークスキャンを実行");
                var networks = await wifiService.ScanNetworksAsync();

                context.Assert(networks != null, "スキャン結果はnullであってはならない");
                context.Assert(networks.Count > 0, "スキャンで少なくとも1つのネットワークが見つかるべき");
                context.AssertEquals(1, wifiService.ScanCount, "スキャン回数が正しく記録されているべき");
                
                // デフォルトネットワークの存在確認
                var testNetwork = networks.FirstOrDefault(n => n.SSID == "TestNetwork1");
                context.Assert(testNetwork != null, "TestNetwork1が見つかるべき");
                context.AssertEquals(85, testNetwork.SignalStrength, "TestNetwork1の信号強度が正しいべき");
            }
        }

        /// <summary>
        /// 基本接続テスト
        /// </summary>
        private class BasicConnectionTest : ITest
        {
            public string Name => "WiFi基本接続テスト";

            public async Task ExecuteAsync(TestExecutionContext context)
            {
                var framework = TestingFramework.Instance;
                var wifiService = framework.GetMock<MockWifiService>();

                context.Log("TestNetwork1への接続を試行");
                var result = await wifiService.ConnectToNetworkAsync("TestNetwork1", "testpassword");

                context.Assert(result.Success, "接続は成功するべき");
                context.AssertEquals("TestNetwork1", result.ConnectedSSID, "接続されたSSIDが正しいべき");
                context.AssertEquals("TestNetwork1", wifiService.CurrentlyConnectedSSID, "現在接続中のSSIDが正しいべき");
                context.AssertEquals(1, wifiService.ConnectionAttempts, "接続試行回数が正しいべき");
                
                // 接続履歴の確認
                context.Assert(wifiService.ConnectionHistory.Count > 0, "接続履歴が記録されているべき");
                context.Assert(wifiService.ConnectionHistory[0].Contains("TestNetwork1"), "履歴に正しいSSIDが記録されているべき");
            }
        }

        /// <summary>
        /// 接続失敗テスト
        /// </summary>
        private class ConnectionFailureTest : ITest
        {
            public string Name => "WiFi接続失敗テスト";

            public async Task ExecuteAsync(TestExecutionContext context)
            {
                var framework = TestingFramework.Instance;
                var wifiService = framework.GetMock<MockWifiService>();

                context.Log("WeakNetwork（失敗設定）への接続を試行");
                var result = await wifiService.ConnectToNetworkAsync("WeakNetwork", "testpassword");

                context.Assert(!result.Success, "接続は失敗するべき");
                context.Assert(result.ConnectedSSID == null, "失敗時はConnectedSSIDはnullであるべき");
                context.Assert(wifiService.CurrentlyConnectedSSID == null, "失敗時は現在接続中SSIDはnullであるべき");
                context.Assert(!string.IsNullOrEmpty(result.ErrorMessage ?? result.Message), "エラーメッセージが設定されているべき");
            }
        }

        /// <summary>
        /// 信号強度変動テスト
        /// </summary>
        private class SignalStrengthVariationTest : ITest
        {
            public string Name => "信号強度変動テスト";

            public async Task ExecuteAsync(TestExecutionContext context)
            {
                var framework = TestingFramework.Instance;
                var wifiService = framework.GetMock<MockWifiService>();

                // 初期スキャン
                var networks1 = await wifiService.ScanNetworksAsync();
                var network1 = networks1.FirstOrDefault(n => n.SSID == "TestNetwork1");
                context.AssertEquals(85, network1?.SignalStrength, "初期信号強度が正しいべき");

                // 信号強度を変更
                wifiService.UpdateSignalStrength("TestNetwork1", 50);

                // 再スキャン
                var networks2 = await wifiService.ScanNetworksAsync();
                var network2 = networks2.FirstOrDefault(n => n.SSID == "TestNetwork1");
                context.AssertEquals(50, network2?.SignalStrength, "変更後の信号強度が正しいべき");
                
                context.AssertEquals(2, wifiService.ScanCount, "スキャン回数が正しく増加しているべき");
            }
        }

        /// <summary>
        /// 複数ネットワークテスト
        /// </summary>
        private class MultipleNetworksTest : ITest
        {
            public string Name => "複数ネットワーク管理テスト";

            public async Task ExecuteAsync(TestExecutionContext context)
            {
                var framework = TestingFramework.Instance;
                var wifiService = framework.GetMock<MockWifiService>();

                // 追加ネットワーク作成
                wifiService.AddTestNetwork(new WifiNetwork 
                { 
                    SSID = "TempNetwork", 
                    SignalStrength = 70, 
                    SecurityType = "WPA2" 
                });
                wifiService.SetConnectionResult("TempNetwork", true);

                // スキャンして全ネットワーク確認
                var networks = await wifiService.ScanNetworksAsync();
                var networkCount = networks.Count;
                context.Assert(networkCount >= 5, "少なくとも5つのネットワークがあるべき"); // 4つのデフォルト + 1つ追加

                // 1つ目に接続
                await wifiService.ConnectToNetworkAsync("TestNetwork1", "pass1");
                context.AssertEquals("TestNetwork1", wifiService.CurrentlyConnectedSSID, "最初の接続が成功するべき");

                // 2つ目に接続（切り替え）
                await wifiService.ConnectToNetworkAsync("TempNetwork", "pass2");
                context.AssertEquals("TempNetwork", wifiService.CurrentlyConnectedSSID, "ネットワーク切り替えが成功するべき");

                // 接続状態の確認
                var currentNetworks = await wifiService.ScanNetworksAsync();
                var connectedNetworks = currentNetworks.Where(n => n.IsConnected).ToList();
                context.AssertEquals(1, connectedNetworks.Count, "接続中のネットワークは1つだけであるべき");
                context.AssertEquals("TempNetwork", connectedNetworks[0].SSID, "正しいネットワークが接続中として表示されるべき");
            }
        }

        /// <summary>
        /// 接続タイムアウトテスト
        /// </summary>
        private class ConnectionTimeoutTest : ITest
        {
            public string Name => "接続タイムアウトテスト";

            public async Task ExecuteAsync(TestExecutionContext context)
            {
                var framework = TestingFramework.Instance;
                var wifiService = framework.GetMock<MockWifiService>();

                // 長い遅延を設定
                wifiService.SetConnectionDelay("TestNetwork1", TimeSpan.FromMilliseconds(5000));

                var startTime = DateTime.UtcNow;
                var result = await wifiService.ConnectToNetworkAsync("TestNetwork1", "testpassword");
                var endTime = DateTime.UtcNow;

                var actualDelay = endTime - startTime;
                context.Assert(actualDelay >= TimeSpan.FromMilliseconds(4500), "設定された遅延時間が反映されているべき");
                context.Assert(result.Success, "遅延があっても最終的には接続成功するべき");

                context.Log($"実際の接続時間: {actualDelay.TotalMilliseconds}ms");
            }
        }

        /// <summary>
        /// プロファイル管理テスト
        /// </summary>
        private class ProfileManagementTest : ITest
        {
            public string Name => "プロファイル管理テスト";

            public async Task ExecuteAsync(TestExecutionContext context)
            {
                var framework = TestingFramework.Instance;
                var wifiService = framework.GetMock<MockWifiService>();
                var profileService = framework.GetMock<MockProfileService>();

                // プロファイル保存
                profileService.SaveProfile("TestNetwork1", "savedpassword");
                context.Assert(profileService.HasProfile("TestNetwork1"), "プロファイルが保存されているべき");

                // 保存されたプロファイルで接続
                var savedPassword = profileService.GetSavedPassword("TestNetwork1");
                context.AssertEquals("savedpassword", savedPassword, "保存されたパスワードが正しく取得できるべき");

                var result = await wifiService.ConnectToNetworkAsync("TestNetwork1", savedPassword);
                context.Assert(result.Success, "保存されたパスワードで接続成功するべき");

                // プロファイル一覧確認
                var profiles = profileService.GetSavedProfiles();
                context.Assert(profiles.Contains("TestNetwork1"), "プロファイル一覧にTestNetwork1が含まれているべき");

                // プロファイル削除
                profileService.RemoveProfile("TestNetwork1");
                context.Assert(!profileService.HasProfile("TestNetwork1"), "プロファイルが削除されているべき");
            }
        }

        /// <summary>
        /// 切断テスト
        /// </summary>
        private class DisconnectionTest : ITest
        {
            public string Name => "WiFi切断テスト";

            public async Task ExecuteAsync(TestExecutionContext context)
            {
                var framework = TestingFramework.Instance;
                var wifiService = framework.GetMock<MockWifiService>();

                // まず接続
                await wifiService.ConnectToNetworkAsync("TestNetwork1", "testpassword");
                context.AssertEquals("TestNetwork1", wifiService.CurrentlyConnectedSSID, "接続前提条件の確認");

                // 切断実行
                var disconnectResult = await wifiService.DisconnectAsync();
                context.Assert(disconnectResult, "切断処理が成功するべき");
                context.Assert(wifiService.CurrentlyConnectedSSID == null, "切断後は接続中SSIDがnullであるべき");

                // 再スキャンで切断確認
                var networks = await wifiService.ScanNetworksAsync();
                var connectedNetworks = networks.Where(n => n.IsConnected).ToList();
                context.AssertEquals(0, connectedNetworks.Count, "切断後は接続中ネットワークが0であるべき");

                // 未接続状態での切断試行
                var secondDisconnect = await wifiService.DisconnectAsync();
                context.Assert(!secondDisconnect, "未接続状態での切断は失敗するべき");
            }
        }

        /// <summary>
        /// 存在しないネットワークテスト
        /// </summary>
        private class NetworkNotFoundTest : ITest
        {
            public string Name => "存在しないネットワーク接続テスト";

            public async Task ExecuteAsync(TestExecutionContext context)
            {
                var framework = TestingFramework.Instance;
                var wifiService = framework.GetMock<MockWifiService>();

                context.Log("存在しないネットワークへの接続を試行");
                var result = await wifiService.ConnectToNetworkAsync("NonExistentNetwork", "anypassword");

                context.Assert(!result.Success, "存在しないネットワークへの接続は失敗するべき");
                context.Assert(result.ErrorMessage == "Network not found", "適切なエラーメッセージが返されるべき");
                context.Assert(wifiService.CurrentlyConnectedSSID == null, "失敗時は接続状態が変更されないべき");
            }
        }

        /// <summary>
        /// 同時操作テスト
        /// </summary>
        private class ConcurrentOperationTest : ITest
        {
            public string Name => "同時操作テスト";

            public async Task ExecuteAsync(TestExecutionContext context)
            {
                var framework = TestingFramework.Instance;
                var wifiService = framework.GetMock<MockWifiService>();

                context.Log("複数の操作を同時実行");

                // 同時にスキャンと接続を実行
                var scanTask = wifiService.ScanNetworksAsync();
                var connectTask = wifiService.ConnectToNetworkAsync("TestNetwork1", "testpassword");
                var statusTask = wifiService.GetCurrentConnectedSSIDAsync();

                await Task.WhenAll(scanTask, connectTask, statusTask);

                var scanResult = await scanTask;
                var connectResult = await connectTask;
                var statusResult = await statusTask;

                context.Assert(scanResult.Count > 0, "同時実行時でもスキャンが正常動作するべき");
                context.Assert(connectResult.Success, "同時実行時でも接続が正常動作するべき");
                
                // 最終状態の確認
                context.Log($"最終接続状態: {wifiService.CurrentlyConnectedSSID}");
                context.Log($"同時実行後の統計: スキャン{wifiService.ScanCount}回, 接続試行{wifiService.ConnectionAttempts}回");
            }
        }

        #endregion
    }
}