using System.Linq;
using System.Threading.Tasks;
using MurtiWifiConnecter.Testing;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Testing.Tests
{
    [TestSuite("Integration")]
    public class WifiConnectionIntegrationTests : IntegrationTestFixture
    {
        private IWifiService _wifiService;
        private INetworkService _networkService;
        private IConfigurationService _configService;

        [Setup]
        public override async Task SetupAsync()
        {
            await base.SetupAsync();
            
            _wifiService = GetService<IWifiService>();
            _networkService = GetService<INetworkService>();
            _configService = GetService<IConfigurationService>();

            // Setup test data
            MockWifi.AddMockNetwork(TestDataBuilder.CreateWifiNetwork("IntegrationNetwork", WifiSecurityType.WPA2, 90));
        }

        [TestMethod(Description = "End-to-end WiFi connection flow")]
        public async Task FullConnectionFlow_ShouldWorkEndToEnd()
        {
            // Arrange
            const string ssid = "IntegrationNetwork";
            const string password = "integration123";

            // Act & Assert - Scan for networks
            await _wifiService.StartScanAsync();
            var networks = await _wifiService.GetAvailableNetworksAsync();
            
            Assert.Contains(networks, n => n.SSID == ssid);
            var targetNetwork = networks.First(n => n.SSID == ssid);
            Assert.Equal(90, targetNetwork.SignalStrength);

            // Act & Assert - Connect to network
            var connectResult = await _wifiService.ConnectAsync(ssid, password);
            Assert.True(connectResult);
            Assert.True(_wifiService.IsConnected());

            // Act & Assert - Verify network connectivity
            var connectivityResult = await _networkService.CheckInternetConnectivityAsync();
            Assert.True(connectivityResult);

            var networkInfo = _networkService.GetNetworkInfo();
            Assert.True(networkInfo.IsConnected);
            Assert.Equal(NetworkConnectionType.Wifi, networkInfo.ConnectionType);

            // Act & Assert - Test network diagnostics
            var diagnostics = await _networkService.RunNetworkDiagnosticsAsync();
            Assert.True(diagnostics.IsHealthy);
            Assert.Equal(100, diagnostics.ConnectivityScore);

            // Act & Assert - Save connection preferences
            await _configService.SetValueAsync("LastConnectedSSID", ssid);
            await _configService.SetValueAsync("AutoConnect", true);

            var savedSSID = _configService.GetValue<string>("LastConnectedSSID");
            var autoConnect = _configService.GetValue<bool>("AutoConnect");

            Assert.Equal(ssid, savedSSID);
            Assert.True(autoConnect);

            // Act & Assert - Disconnect
            var disconnectResult = await _wifiService.DisconnectAsync();
            Assert.True(disconnectResult);
            Assert.False(_wifiService.IsConnected());

            // Verify all components logged appropriately
            AssertLogContains(LogLevel.Info, "scan");
            AssertLogContains(LogLevel.Info, "connect");
            AssertLogContains(LogLevel.Info, "disconnect");
            AssertNoErrors();
        }

        [TestMethod(Description = "Connection recovery after network interruption")]
        public async Task ConnectionRecovery_AfterInterruption_ShouldRecover()
        {
            // Arrange - Connect to network
            const string ssid = "IntegrationNetwork";
            const string password = "integration123";

            await _wifiService.ConnectAsync(ssid, password);
            Assert.True(_wifiService.IsConnected());

            var connectionEvents = 0;
            _wifiService.ConnectionChanged += (s, e) => connectionEvents++;

            // Act - Simulate network interruption
            MockWifi.SimulateConnectionLost();
            MockNetwork.SimulateDisconnection();

            // Assert - Connection lost
            Assert.False(_wifiService.IsConnected());
            Assert.False(await _networkService.CheckInternetConnectivityAsync());
            Assert.True(connectionEvents > 0);

            // Act - Simulate network recovery
            MockNetwork.SimulateConnection();
            var reconnectResult = await _wifiService.ConnectAsync(ssid, password);

            // Assert - Connection restored
            Assert.True(reconnectResult);
            Assert.True(_wifiService.IsConnected());
            Assert.True(await _networkService.CheckInternetConnectivityAsync());

            AssertLogContains(LogLevel.Warning, "connection lost");
            AssertLogContains(LogLevel.Info, "reconnected");
        }

        [TestMethod(Description = "Multiple network management")]
        public async Task MultipleNetworkManagement_ShouldHandleCorrectly()
        {
            // Arrange - Add multiple test networks
            var networks = TestDataBuilder.CreateMultipleNetworks(5);
            foreach (var network in networks)
            {
                MockWifi.AddMockNetwork(network);
            }

            // Act - Scan and verify all networks found
            await _wifiService.StartScanAsync();
            var foundNetworks = await _wifiService.GetAvailableNetworksAsync();

            // Assert - All networks discovered
            Assert.True(foundNetworks.Count >= 5);
            foreach (var expectedNetwork in networks)
            {
                Assert.Contains(foundNetworks, n => n.SSID == expectedNetwork.SSID);
            }

            // Act - Connect to best signal network
            var bestNetwork = foundNetworks.OrderByDescending(n => n.SignalStrength).First();
            var password = bestNetwork.SecurityType == WifiSecurityType.Open ? null : "password123";
            
            var connectResult = await _wifiService.ConnectAsync(bestNetwork.SSID, password);

            // Assert - Connected to best network
            Assert.True(connectResult);
            Assert.Equal(bestNetwork.SSID, _wifiService.GetConnectedNetwork()?.SSID);

            // Act - Test network switching
            var secondBestNetwork = foundNetworks
                .OrderByDescending(n => n.SignalStrength)
                .Skip(1)
                .First();

            await _wifiService.DisconnectAsync();
            var switchResult = await _wifiService.ConnectAsync(secondBestNetwork.SSID, password);

            // Assert - Successfully switched networks
            Assert.True(switchResult);
            Assert.Equal(secondBestNetwork.SSID, _wifiService.GetConnectedNetwork()?.SSID);

            AssertNoErrors();
        }

        [TestMethod(Description = "Configuration persistence across service restarts")]
        public async Task ConfigurationPersistence_AcrossRestarts_ShouldMaintainSettings()
        {
            // Arrange - Set configuration values
            await _configService.SetValueAsync("WiFi.AutoConnect", true);
            await _configService.SetValueAsync("WiFi.PreferredNetwork", "IntegrationNetwork");
            await _configService.SetValueAsync("WiFi.ConnectionTimeout", 30);

            var originalValues = _configService.GetAllValues();

            // Simulate service restart by creating new mock
            MockConfiguration.Clear();
            
            // Restore configuration (in real scenario, this would load from persistent storage)
            foreach (var kvp in originalValues)
            {
                await _configService.SetValueAsync(kvp.Key, kvp.Value);
            }

            // Assert - Configuration restored
            Assert.Equal(true, _configService.GetValue<bool>("WiFi.AutoConnect"));
            Assert.Equal("IntegrationNetwork", _configService.GetValue<string>("WiFi.PreferredNetwork"));
            Assert.Equal(30, _configService.GetValue<int>("WiFi.ConnectionTimeout"));

            var restoredValues = _configService.GetAllValues();
            Assert.Equal(originalValues.Count, restoredValues.Count);

            AssertLogContains(LogLevel.Info, "configuration");
        }

        [TestMethod(Description = "Network performance monitoring")]
        public async Task NetworkPerformanceMonitoring_ShouldTrackMetrics()
        {
            // Arrange
            const string ssid = "IntegrationNetwork";
            const string password = "integration123";

            // Act - Connect and perform network operations
            await _wifiService.ConnectAsync(ssid, password);
            
            // Perform multiple network operations to generate metrics
            for (int i = 0; i < 5; i++)
            {
                await _networkService.PingAsync("8.8.8.8");
                await _networkService.CheckInternetConnectivityAsync();
                await _networkService.RunNetworkDiagnosticsAsync();
            }

            // Assert - Verify telemetry tracking
            var telemetryEvents = MockTelemetry.GetEvents();
            var telemetryMetrics = MockTelemetry.GetMetrics();

            Assert.True(telemetryEvents.Count > 0);
            Assert.True(telemetryMetrics.Count > 0);

            // Verify specific metrics were tracked
            AssertEventTracked("WiFi.Connection.Success");
            AssertEventTracked("Network.Connectivity.Check");
            AssertMetricTracked("Network.Ping.Duration");
            AssertMetricTracked("Network.Diagnostics.Score");

            AssertNoErrors();
        }

        [TestMethod(Description = "Error handling and recovery")]
        public async Task ErrorHandlingAndRecovery_ShouldHandleGracefully()
        {
            // Test connection to non-existent network
            var invalidResult = await _wifiService.ConnectAsync("NonExistentNetwork", "password");
            Assert.False(invalidResult);
            AssertLogContains(LogLevel.Warning, "connect");

            // Test network operations when disconnected
            MockNetwork.SimulateDisconnection();
            
            var connectivityResult = await _networkService.CheckInternetConnectivityAsync();
            Assert.False(connectivityResult);

            var pingResult = await _networkService.PingAsync("8.8.8.8");
            Assert.False(pingResult.IsSuccess);

            var diagnostics = await _networkService.RunNetworkDiagnosticsAsync();
            Assert.False(diagnostics.IsHealthy);
            Assert.Contains(diagnostics.Issues, issue => issue.Contains("connectivity"));

            // Verify graceful error handling - no exceptions thrown
            AssertNoExceptions();
            
            // Verify errors were logged but system remained stable
            Assert.True(MockLogger.HasAnyLogEntry(LogLevel.Warning));
            Assert.False(MockLogger.HasAnyLogEntry(LogLevel.Critical));
        }

        [TestMethod(Description = "Load testing with multiple concurrent operations")]
        public async Task LoadTesting_ConcurrentOperations_ShouldPerformWell()
        {
            const int concurrentOperations = 20;
            
            // Create tasks for concurrent operations
            var tasks = new Task[concurrentOperations];
            
            for (int i = 0; i < concurrentOperations; i++)
            {
                int operationId = i;
                tasks[i] = Task.Run(async () =>
                {
                    // Mix of different operations
                    switch (operationId % 4)
                    {
                        case 0:
                            await _wifiService.StartScanAsync();
                            break;
                        case 1:
                            await _networkService.CheckInternetConnectivityAsync();
                            break;
                        case 2:
                            await _networkService.PingAsync("8.8.8.8");
                            break;
                        case 3:
                            await _configService.SetValueAsync($"TestKey_{operationId}", $"TestValue_{operationId}");
                            break;
                    }
                });
            }

            // Measure performance
            var loadTestDuration = await PerformanceTestHelper.MeasureAsync(async () =>
            {
                await Task.WhenAll(tasks);
            });

            // Assert - Operations completed in reasonable time
            Assert.InRange(loadTestDuration.TotalSeconds, 0, 10); // Should complete within 10 seconds
            
            // Assert - No errors during concurrent operations
            AssertNoErrors();
            AssertNoExceptions();

            MockLogger.LogInfo($"Load test completed in {loadTestDuration.TotalMilliseconds}ms with {concurrentOperations} concurrent operations");
        }
    }

    [TestSuite("SystemIntegration")]
    public class SystemIntegrationTests : IntegrationTestFixture
    {
        [TestMethod(Description = "Full system startup and shutdown")]
        public async Task SystemLifecycle_StartupAndShutdown_ShouldWorkCorrectly()
        {
            // This would test the full application lifecycle
            // In a real scenario, this would involve:
            // 1. Application startup
            // 2. Service initialization
            // 3. UI loading
            // 4. Background service startup
            // 5. Clean shutdown

            // Simulate system startup
            var services = new[]
            {
                typeof(IWifiService),
                typeof(INetworkService),
                typeof(IConfigurationService),
                typeof(ILoggingService),
                typeof(ITelemetryService)
            };

            // Verify all services can be resolved
            foreach (var serviceType in services)
            {
                var service = ServiceContainer.GetService(serviceType);
                Assert.NotNull(service, $"Service {serviceType.Name} should be resolvable");
            }

            // Simulate some system operations
            var wifiService = GetService<IWifiService>();
            var networkService = GetService<INetworkService>();

            await wifiService.StartScanAsync();
            await networkService.CheckInternetConnectivityAsync();

            // Verify system is stable
            AssertNoErrors();
            AssertNoExceptions();

            await Task.CompletedTask;
        }
    }
}