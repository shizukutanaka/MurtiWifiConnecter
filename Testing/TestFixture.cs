using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Testing
{
    public abstract class TestFixture : IDisposable
    {
        protected IServiceContainer ServiceContainer { get; private set; }
        protected MockLoggingService MockLogger { get; private set; }
        protected MockTelemetryService MockTelemetry { get; private set; }
        protected MockWifiService MockWifi { get; private set; }
        protected MockNetworkService MockNetwork { get; private set; }
        protected MockConfigurationService MockConfiguration { get; private set; }

        private bool _isDisposed;

        [Setup]
        public virtual async Task SetupAsync()
        {
            ServiceContainer = CreateServiceContainer();
            MockLogger = new MockLoggingService();
            MockTelemetry = new MockTelemetryService();
            MockWifi = new MockWifiService();
            MockNetwork = new MockNetworkService();
            MockConfiguration = new MockConfigurationService();

            RegisterServices();
            await InitializeServicesAsync();
        }

        [Teardown]
        public virtual async Task TeardownAsync()
        {
            await CleanupServicesAsync();
            ServiceContainer?.Dispose();
        }

        protected virtual IServiceContainer CreateServiceContainer()
        {
            return new ServiceContainer();
        }

        protected virtual void RegisterServices()
        {
            ServiceContainer.RegisterSingleton<ILoggingService>(MockLogger);
            ServiceContainer.RegisterSingleton<ITelemetryService>(MockTelemetry);
            ServiceContainer.RegisterSingleton<IWifiService>(MockWifi);
            ServiceContainer.RegisterSingleton<INetworkService>(MockNetwork);
            ServiceContainer.RegisterSingleton<IConfigurationService>(MockConfiguration);
        }

        protected virtual async Task InitializeServicesAsync()
        {
            await Task.CompletedTask;
        }

        protected virtual async Task CleanupServicesAsync()
        {
            MockLogger?.ClearLogs();
            MockTelemetry?.Clear();
            MockConfiguration?.Clear();
            await Task.CompletedTask;
        }

        protected T GetService<T>() where T : class
        {
            return ServiceContainer.GetService<T>();
        }

        protected T CreateInstance<T>() where T : class
        {
            return ServiceContainer.CreateInstance<T>();
        }

        protected void AssertNoErrors()
        {
            Assert.False(MockLogger.HasAnyLogEntry(LogLevel.Error), "Expected no error log entries");
        }

        protected void AssertNoExceptions()
        {
            Assert.Empty(MockTelemetry.GetExceptions(), "Expected no telemetry exceptions");
        }

        protected void AssertLogContains(LogLevel level, string message)
        {
            Assert.True(MockLogger.HasLogEntry(level, message), 
                $"Expected log entry with level {level} containing '{message}'");
        }

        protected void AssertEventTracked(string eventName)
        {
            Assert.True(MockTelemetry.HasEvent(eventName), 
                $"Expected telemetry event '{eventName}' to be tracked");
        }

        protected void AssertMetricTracked(string metricName)
        {
            Assert.True(MockTelemetry.HasMetric(metricName), 
                $"Expected telemetry metric '{metricName}' to be tracked");
        }

        protected void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    TeardownAsync().Wait();
                }
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public abstract class WifiTestFixture : TestFixture
    {
        protected const string TestSSID = "TestNetwork";
        protected const string TestPassword = "testpassword";
        protected const string SecureSSID = "SecureNetwork";
        protected const string OpenSSID = "OpenNetwork";

        protected override async Task InitializeServicesAsync()
        {
            await base.InitializeServicesAsync();

            // Setup common test networks
            MockWifi.AddMockNetwork(new WifiNetwork
            {
                SSID = TestSSID,
                SecurityType = WifiSecurityType.WPA2,
                SignalStrength = 85,
                IsConnected = false
            });

            MockWifi.AddMockNetwork(new WifiNetwork
            {
                SSID = SecureSSID,
                SecurityType = WifiSecurityType.WPA3,
                SignalStrength = 70,
                IsConnected = false
            });

            MockWifi.AddMockNetwork(new WifiNetwork
            {
                SSID = OpenSSID,
                SecurityType = WifiSecurityType.Open,
                SignalStrength = 60,
                IsConnected = false
            });
        }

        protected async Task ConnectToTestNetwork()
        {
            var result = await MockWifi.ConnectAsync(TestSSID, TestPassword);
            Assert.True(result, "Failed to connect to test network");
            Assert.True(MockWifi.IsConnected(), "WiFi service should report connected");
        }

        protected void SimulateNetworkDisconnection()
        {
            MockWifi.SimulateConnectionLost();
            MockNetwork.SimulateDisconnection();
        }

        protected void SimulateNetworkConnection()
        {
            MockNetwork.SimulateConnection();
        }
    }

    public abstract class IntegrationTestFixture : TestFixture
    {
        protected TestDatabaseContext TestDatabase { get; private set; }
        protected TestFileSystem TestFileSystem { get; private set; }

        protected override async Task InitializeServicesAsync()
        {
            await base.InitializeServicesAsync();

            TestDatabase = new TestDatabaseContext();
            TestFileSystem = new TestFileSystem();

            await TestDatabase.InitializeAsync();
            TestFileSystem.Initialize();
        }

        protected override async Task CleanupServicesAsync()
        {
            await TestDatabase?.CleanupAsync();
            TestFileSystem?.Cleanup();
            await base.CleanupServicesAsync();
        }
    }

    public class TestDatabaseContext
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();

        public async Task InitializeAsync()
        {
            await Task.Delay(10); // Simulate database setup
        }

        public async Task CleanupAsync()
        {
            _data.Clear();
            await Task.Delay(10);
        }

        public void Store<T>(string key, T value)
        {
            _data[key] = value;
        }

        public T Retrieve<T>(string key)
        {
            return _data.TryGetValue(key, out var value) ? (T)value : default(T);
        }

        public bool Exists(string key)
        {
            return _data.ContainsKey(key);
        }

        public void Remove(string key)
        {
            _data.Remove(key);
        }
    }

    public class TestFileSystem
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
        private readonly HashSet<string> _directories = new HashSet<string>();

        public void Initialize()
        {
            _files.Clear();
            _directories.Clear();
            _directories.Add("/");
        }

        public void Cleanup()
        {
            _files.Clear();
            _directories.Clear();
        }

        public void CreateFile(string path, string content = "")
        {
            _files[path] = content;
        }

        public void CreateDirectory(string path)
        {
            _directories.Add(path);
        }

        public string ReadFile(string path)
        {
            return _files.TryGetValue(path, out var content) ? content : null;
        }

        public void WriteFile(string path, string content)
        {
            _files[path] = content;
        }

        public bool FileExists(string path)
        {
            return _files.ContainsKey(path);
        }

        public bool DirectoryExists(string path)
        {
            return _directories.Contains(path);
        }

        public void DeleteFile(string path)
        {
            _files.Remove(path);
        }

        public void DeleteDirectory(string path)
        {
            _directories.Remove(path);
            var filesToRemove = new List<string>();
            foreach (var file in _files.Keys)
            {
                if (file.StartsWith(path + "/"))
                    filesToRemove.Add(file);
            }
            foreach (var file in filesToRemove)
                _files.Remove(file);
        }

        public List<string> GetFiles(string directory)
        {
            var files = new List<string>();
            foreach (var file in _files.Keys)
            {
                if (file.StartsWith(directory + "/") && 
                    file.Substring(directory.Length + 1).IndexOf('/') == -1)
                {
                    files.Add(file);
                }
            }
            return files;
        }

        public List<string> GetDirectories(string directory)
        {
            var directories = new List<string>();
            foreach (var dir in _directories)
            {
                if (dir.StartsWith(directory + "/") && 
                    dir.Substring(directory.Length + 1).IndexOf('/') == -1)
                {
                    directories.Add(dir);
                }
            }
            return directories;
        }
    }

    // Performance testing utilities
    public static class PerformanceTestHelper
    {
        public static async Task<TimeSpan> MeasureAsync(Func<Task> action)
        {
            var startTime = DateTime.UtcNow;
            await action();
            return DateTime.UtcNow - startTime;
        }

        public static TimeSpan Measure(Action action)
        {
            var startTime = DateTime.UtcNow;
            action();
            return DateTime.UtcNow - startTime;
        }

        public static async Task<PerformanceResult> BenchmarkAsync(Func<Task> action, int iterations = 10)
        {
            var times = new List<TimeSpan>();

            for (int i = 0; i < iterations; i++)
            {
                var time = await MeasureAsync(action);
                times.Add(time);
            }

            return new PerformanceResult
            {
                Iterations = iterations,
                Times = times,
                Average = TimeSpan.FromTicks((long)times.Select(t => t.Ticks).Average()),
                Minimum = times.Min(),
                Maximum = times.Max(),
                Total = TimeSpan.FromTicks(times.Sum(t => t.Ticks))
            };
        }

        public static PerformanceResult Benchmark(Action action, int iterations = 10)
        {
            var times = new List<TimeSpan>();

            for (int i = 0; i < iterations; i++)
            {
                var time = Measure(action);
                times.Add(time);
            }

            return new PerformanceResult
            {
                Iterations = iterations,
                Times = times,
                Average = TimeSpan.FromTicks((long)times.Select(t => t.Ticks).Average()),
                Minimum = times.Min(),
                Maximum = times.Max(),
                Total = TimeSpan.FromTicks(times.Sum(t => t.Ticks))
            };
        }
    }

    public class PerformanceResult
    {
        public int Iterations { get; set; }
        public List<TimeSpan> Times { get; set; }
        public TimeSpan Average { get; set; }
        public TimeSpan Minimum { get; set; }
        public TimeSpan Maximum { get; set; }
        public TimeSpan Total { get; set; }

        public double AverageMilliseconds => Average.TotalMilliseconds;
        public double MinimumMilliseconds => Minimum.TotalMilliseconds;
        public double MaximumMilliseconds => Maximum.TotalMilliseconds;
        public double TotalMilliseconds => Total.TotalMilliseconds;

        public override string ToString()
        {
            return $"Performance: {Iterations} iterations, Avg: {AverageMilliseconds:F2}ms, Min: {MinimumMilliseconds:F2}ms, Max: {MaximumMilliseconds:F2}ms";
        }
    }

    // Test data builders
    public class TestDataBuilder
    {
        public static WifiNetwork CreateWifiNetwork(string ssid = "TestNetwork", WifiSecurityType security = WifiSecurityType.WPA2, int signalStrength = 75)
        {
            return new WifiNetwork
            {
                SSID = ssid,
                SecurityType = security,
                SignalStrength = signalStrength,
                IsConnected = false
            };
        }

        public static NetworkInfo CreateNetworkInfo(bool isConnected = true, string ipAddress = "192.168.1.100")
        {
            return new NetworkInfo
            {
                IsConnected = isConnected,
                ConnectionType = NetworkConnectionType.Wifi,
                IPAddress = ipAddress,
                SubnetMask = "255.255.255.0",
                Gateway = "192.168.1.1",
                DNS = new[] { "8.8.8.8", "8.8.4.4" }
            };
        }

        public static List<WifiNetwork> CreateMultipleNetworks(int count = 5)
        {
            var networks = new List<WifiNetwork>();
            var securityTypes = new[] { WifiSecurityType.Open, WifiSecurityType.WPA2, WifiSecurityType.WPA3 };

            for (int i = 0; i < count; i++)
            {
                networks.Add(new WifiNetwork
                {
                    SSID = $"Network{i + 1}",
                    SecurityType = securityTypes[i % securityTypes.Length],
                    SignalStrength = 100 - (i * 10),
                    IsConnected = false
                });
            }

            return networks;
        }
    }
}