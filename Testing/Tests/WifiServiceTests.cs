using System.Threading.Tasks;
using MurtiWifiConnecter.Testing;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Testing.Tests
{
    [TestSuite("WifiService")]
    public class WifiServiceTests : WifiTestFixture
    {
        private IWifiService _wifiService;

        [Setup]
        public override async Task SetupAsync()
        {
            await base.SetupAsync();
            _wifiService = GetService<IWifiService>();
        }

        [TestMethod(Description = "Should return available networks")]
        public async Task GetAvailableNetworks_ShouldReturnNetworks()
        {
            var networks = await _wifiService.GetAvailableNetworksAsync();

            Assert.NotNull(networks);
            Assert.Count(networks, 3);
            Assert.Contains(networks, n => n.SSID == TestSSID);
            Assert.Contains(networks, n => n.SSID == SecureSSID);
            Assert.Contains(networks, n => n.SSID == OpenSSID);
        }

        [TestMethod(Description = "Should connect to network with valid credentials")]
        public async Task Connect_WithValidCredentials_ShouldSucceed()
        {
            var result = await _wifiService.ConnectAsync(TestSSID, TestPassword);

            Assert.True(result);
            Assert.True(_wifiService.IsConnected());
            
            var connectedNetwork = _wifiService.GetConnectedNetwork();
            Assert.NotNull(connectedNetwork);
            Assert.Equal(TestSSID, connectedNetwork.SSID);

            AssertLogContains(LogLevel.Info, "connected");
            AssertNoErrors();
        }

        [TestMethod(Description = "Should fail to connect without password for secured network")]
        public async Task Connect_WithoutPasswordForSecuredNetwork_ShouldFail()
        {
            var result = await _wifiService.ConnectAsync(TestSSID, null);

            Assert.False(result);
            Assert.False(_wifiService.IsConnected());
            Assert.Null(_wifiService.GetConnectedNetwork());
        }

        [TestMethod(Description = "Should connect to open network without password")]
        public async Task Connect_ToOpenNetworkWithoutPassword_ShouldSucceed()
        {
            var result = await _wifiService.ConnectAsync(OpenSSID);

            Assert.True(result);
            Assert.True(_wifiService.IsConnected());
            
            var connectedNetwork = _wifiService.GetConnectedNetwork();
            Assert.NotNull(connectedNetwork);
            Assert.Equal(OpenSSID, connectedNetwork.SSID);
        }

        [TestMethod(Description = "Should fail to connect to non-existent network")]
        public async Task Connect_ToNonExistentNetwork_ShouldFail()
        {
            var result = await _wifiService.ConnectAsync("NonExistentNetwork", "password");

            Assert.False(result);
            Assert.False(_wifiService.IsConnected());
        }

        [TestMethod(Description = "Should disconnect from connected network")]
        public async Task Disconnect_FromConnectedNetwork_ShouldSucceed()
        {
            await ConnectToTestNetwork();
            
            var result = await _wifiService.DisconnectAsync();

            Assert.True(result);
            Assert.False(_wifiService.IsConnected());
            Assert.Null(_wifiService.GetConnectedNetwork());
        }

        [TestMethod(Description = "Should handle disconnect when not connected")]
        public async Task Disconnect_WhenNotConnected_ShouldReturnFalse()
        {
            var result = await _wifiService.DisconnectAsync();

            Assert.False(result);
            Assert.False(_wifiService.IsConnected());
        }

        [TestMethod(Description = "Should complete network scan")]
        public async Task StartScan_ShouldCompleteSuccessfully()
        {
            var scanCompleted = false;
            _wifiService.ScanCompleted += (sender, args) =>
            {
                scanCompleted = true;
                Assert.NotNull(args.Networks);
                Assert.True(args.ScanDuration.TotalMilliseconds > 0);
            };

            await _wifiService.StartScanAsync();

            Assert.True(scanCompleted, "Scan completed event should be fired");
            Assert.False(_wifiService.IsScanning(), "Should not be scanning after completion");
        }

        [TestMethod(Description = "Should handle multiple scan requests")]
        public async Task StartScan_MultipleConcurrentRequests_ShouldHandleGracefully()
        {
            var task1 = _wifiService.StartScanAsync();
            var task2 = _wifiService.StartScanAsync();
            var task3 = _wifiService.StartScanAsync();

            await Task.WhenAll(task1, task2, task3);

            Assert.False(_wifiService.IsScanning());
            AssertNoErrors();
        }

        [TestMethod(Description = "Should forget network")]
        public async Task ForgetNetwork_ShouldSucceed()
        {
            var result = await _wifiService.ForgetNetworkAsync(TestSSID);

            Assert.True(result);
            AssertLogContains(LogLevel.Info, "forget");
        }

        [TestMethod(Description = "Should raise connection changed events")]
        public async Task ConnectionEvents_ShouldBeRaisedCorrectly()
        {
            var connectionEvents = 0;
            var lastConnectionState = false;

            _wifiService.ConnectionChanged += (sender, args) =>
            {
                connectionEvents++;
                lastConnectionState = args.IsConnected;
            };

            // Connect
            await _wifiService.ConnectAsync(TestSSID, TestPassword);
            Assert.Equal(1, connectionEvents);
            Assert.True(lastConnectionState);

            // Disconnect
            await _wifiService.DisconnectAsync();
            Assert.Equal(2, connectionEvents);
            Assert.False(lastConnectionState);
        }

        [TestMethod(Description = "Should handle connection loss")]
        public async Task ConnectionLoss_ShouldRaiseEvent()
        {
            await ConnectToTestNetwork();

            var connectionLost = false;
            _wifiService.ConnectionChanged += (sender, args) =>
            {
                if (!args.IsConnected)
                    connectionLost = true;
            };

            SimulateNetworkDisconnection();

            Assert.True(connectionLost, "Connection lost event should be raised");
            Assert.False(_wifiService.IsConnected());
        }

        [TestMethod(Description = "Should track connection metrics")]
        public async Task Connect_ShouldTrackTelemetryMetrics()
        {
            await _wifiService.ConnectAsync(TestSSID, TestPassword);

            AssertEventTracked("WiFi.Connection.Attempt");
            AssertEventTracked("WiFi.Connection.Success");
            AssertMetricTracked("WiFi.Connection.Duration");
        }

        [TestMethod(Description = "Should handle performance under load", Description = "Performance test")]
        public async Task PerformanceTest_MultipleOperations_ShouldMeetThresholds()
        {
            const int iterations = 100;
            
            var scanResult = await PerformanceTestHelper.BenchmarkAsync(async () =>
            {
                await _wifiService.StartScanAsync();
            }, iterations);

            var connectResult = await PerformanceTestHelper.BenchmarkAsync(async () =>
            {
                await _wifiService.ConnectAsync(TestSSID, TestPassword);
                await _wifiService.DisconnectAsync();
            }, iterations / 2);

            // Performance assertions
            Assert.InRange(scanResult.AverageMilliseconds, 0, 1000); // Scan should be under 1 second
            Assert.InRange(connectResult.AverageMilliseconds, 0, 500); // Connect/disconnect under 500ms
            
            MockLogger.LogInfo($"Scan performance: {scanResult}");
            MockLogger.LogInfo($"Connect performance: {connectResult}");

            AssertNoErrors();
        }

        [Skip("Requires physical hardware")]
        [TestMethod(Description = "Integration test with real hardware")]
        public async Task IntegrationTest_RealHardware_ShouldWork()
        {
            // This test would only run with real WiFi hardware
            // Skipped in mock environment
            await Task.CompletedTask;
        }
    }
}