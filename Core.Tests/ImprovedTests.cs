using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using MurtiWifiConnecter.Core.Common;
using MurtiWifiConnecter.Core.Configuration;
using MurtiWifiConnecter.Core.Network.Windows;

namespace MurtiWifiConnecter.Tests.Core.Network.Windows
{
    /// <summary>
    /// WindowsNetworkScannerの包括的なテストスイート
    /// </summary>
    [TestFixture]
    public class WindowsNetworkScannerTests
    {
        private Mock<ILogger<WindowsNetworkScanner>> _loggerMock;
        private Mock<IMemoryCache> _cacheMock;
        private Mock<IRateLimiter> _rateLimiterMock;
        private Mock<IWifiAdapterManager> _adapterManagerMock;
        private WindowsNetworkScanner _sut;
        private CancellationToken _cancellationToken;

        [SetUp]
        public void SetUp()
        {
            _loggerMock = new Mock<ILogger<WindowsNetworkScanner>>();
            _cacheMock = new Mock<IMemoryCache>();
            _rateLimiterMock = new Mock<IRateLimiter>();
            _adapterManagerMock = new Mock<IWifiAdapterManager>();

            _sut = new WindowsNetworkScanner(
                _loggerMock.Object,
                _cacheMock.Object,
                _rateLimiterMock.Object,
                _adapterManagerMock.Object);

            _cancellationToken = CancellationToken.None;
        }

        [Test]
        public async Task ScanAsync_ValidInput_ShouldReturnNetworksFromCache()
        {
            // Arrange
            var expectedNetworks = new List<NetworkInfo>
            {
                new NetworkInfo { Ssid = "TestNetwork1", Signal = 80, Security = "WPA2" },
                new NetworkInfo { Ssid = "TestNetwork2", Signal = 75, Security = "WPA3" }
            };

            _cacheMock.Setup(c => c.TryGetValue("windows_network_scan", out expectedNetworks))
                      .Returns(true);

            // Act
            var result = await _sut.ScanAsync(_cancellationToken);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.EqualTo(expectedNetworks));
            _loggerMock.Verify(l => l.LogDebug("Returning cached network scan results"), Times.Once);
        }

        [Test]
        public async Task ScanAsync_CacheMiss_ShouldPerformNetworkScan()
        {
            // Arrange
            var expectedNetworks = new List<NetworkInfo>
            {
                new NetworkInfo { Ssid = "TestNetwork1", Signal = 80, Security = "WPA2" }
            };

            _cacheMock.Setup(c => c.TryGetValue(It.IsAny<string>(), out It.Ref<List<NetworkInfo>>.IsAny))
                      .Returns(false);

            _rateLimiterMock.Setup(r => r.CheckRateLimitAsync("network_scan", _cancellationToken))
                           .ReturnsAsync(RateLimitResult.Allowed());

            _adapterManagerMock.Setup(a => a.GetWifiInterfacesAsync(_cancellationToken))
                              .ReturnsAsync(new List<WifiInterfaceInfo>());

            // 実際のスキャン結果をモック
            var cacheEntryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            };
            _cacheMock.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<NetworkInfo>>(), cacheEntryOptions))
                      .Verifiable();

            // Act
            var result = await _sut.ScanAsync(_cancellationToken);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            _cacheMock.Verify(c => c.Set(It.IsAny<string>(), It.IsAny<List<NetworkInfo>>(), It.IsAny<MemoryCacheEntryOptions>()), Times.Once);
        }

        [Test]
        public async Task ScanAsync_RateLimitExceeded_ShouldReturnFailure()
        {
            // Arrange
            var rateLimitResult = RateLimitResult.Denied("Rate limit exceeded", TimeSpan.FromMinutes(1));

            _cacheMock.Setup(c => c.TryGetValue(It.IsAny<string>(), out It.Ref<List<NetworkInfo>>.IsAny))
                      .Returns(false);

            _rateLimiterMock.Setup(r => r.CheckRateLimitAsync("network_scan", _cancellationToken))
                           .ReturnsAsync(rateLimitResult);

            // Act
            var result = await _sut.ScanAsync(_cancellationToken);

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.RateLimitExceeded));
            Assert.That(result.Error.Message, Is.EqualTo("Rate limit exceeded"));
        }

        [Test]
        public async Task ScanAsync_OperationCancelled_ShouldReturnFailure()
        {
            // Arrange
            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            _cacheMock.Setup(c => c.TryGetValue(It.IsAny<string>(), out It.Ref<List<NetworkInfo>>.IsAny))
                      .Returns(false);

            _rateLimiterMock.Setup(r => r.CheckRateLimitAsync("network_scan", cancellationTokenSource.Token))
                           .ReturnsAsync(RateLimitResult.Allowed());

            // Act
            var result = await _sut.ScanAsync(cancellationTokenSource.Token);

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Cancelled));
        }

        [Test]
        public async Task ScanAsync_UnexpectedException_ShouldReturnFailure()
        {
            // Arrange
            _cacheMock.Setup(c => c.TryGetValue(It.IsAny<string>(), out It.Ref<List<NetworkInfo>>.IsAny))
                      .Returns(false);

            _rateLimiterMock.Setup(r => r.CheckRateLimitAsync("network_scan", _cancellationToken))
                           .ThrowsAsync(new InvalidOperationException("Test exception"));

            // Act
            var result = await _sut.ScanAsync(_cancellationToken);

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Unexpected));
            Assert.That(result.Error.Message, Is.EqualTo("Test exception"));
        }
    }

    /// <summary>
    /// Resultパターンのテストスイート
    /// </summary>
    [TestFixture]
    public class NetworkOperationResultTests
    {
        [Test]
        public void Success_WithData_ShouldCreateSuccessfulResult()
        {
            // Arrange
            var data = "test data";
            var metadata = new Dictionary<string, object> { ["test"] = "value" };

            // Act
            var result = NetworkOperationResult<string>.Success(data, metadata);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.EqualTo(data));
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Metadata, Is.EqualTo(metadata));
            Assert.That(result.Timestamp, Is.GreaterThan(DateTime.UtcNow.AddSeconds(-1)));
        }

        [Test]
        public void Failure_WithError_ShouldCreateFailedResult()
        {
            // Arrange
            var error = Error.Validation("Invalid input");

            // Act
            var result = NetworkOperationResult<string>.Failure(error);

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Data, Is.Null);
            Assert.That(result.Error, Is.EqualTo(error));
            Assert.That(result.Timestamp, Is.GreaterThan(DateTime.UtcNow.AddSeconds(-1)));
        }

        [Test]
        public void Map_SuccessResult_ShouldTransformData()
        {
            // Arrange
            var originalResult = NetworkOperationResult<string>.Success("test");
            var expected = "TEST";

            // Act
            var mappedResult = originalResult.Map(s => s.ToUpper());

            // Assert
            Assert.That(mappedResult.IsSuccess, Is.True);
            Assert.That(mappedResult.Data, Is.EqualTo(expected));
        }

        [Test]
        public void Map_FailureResult_ShouldReturnFailure()
        {
            // Arrange
            var error = Error.Validation("Invalid input");
            var originalResult = NetworkOperationResult<string>.Failure(error);

            // Act
            var mappedResult = originalResult.Map(s => s.ToUpper());

            // Assert
            Assert.That(mappedResult.IsSuccess, Is.False);
            Assert.That(mappedResult.Error, Is.EqualTo(error));
        }

        [Test]
        public async Task MapAsync_SuccessResult_ShouldTransformData()
        {
            // Arrange
            var originalResult = NetworkOperationResult<string>.Success("test");
            var expected = "TEST";

            // Act
            var mappedResult = await originalResult.MapAsync(async s =>
            {
                await Task.Delay(1); // 非同期処理をシミュレート
                return s.ToUpper();
            });

            // Assert
            Assert.That(mappedResult.IsSuccess, Is.True);
            Assert.That(mappedResult.Data, Is.EqualTo(expected));
        }

        [Test]
        public void OnSuccess_SuccessResult_ShouldExecuteAction()
        {
            // Arrange
            var originalResult = NetworkOperationResult<string>.Success("test");
            var executed = false;

            // Act
            var finalResult = originalResult.OnSuccess(s => executed = true);

            // Assert
            Assert.That(executed, Is.True);
            Assert.That(finalResult.IsSuccess, Is.True);
        }

        [Test]
        public void OnFailure_FailureResult_ShouldExecuteAction()
        {
            // Arrange
            var error = Error.Validation("Invalid input");
            var originalResult = NetworkOperationResult<string>.Failure(error);
            var executed = false;

            // Act
            var finalResult = originalResult.OnFailure(e => executed = true);

            // Assert
            Assert.That(executed, Is.True);
            Assert.That(finalResult.IsSuccess, Is.False);
        }
    }

    /// <summary>
    /// ConfigurationManagerのテストスイート
    /// </summary>
    [TestFixture]
    public class ConfigurationManagerTests
    {
        private string _testConfigPath;
        private ConfigurationManager _sut;

        [SetUp]
        public void SetUp()
        {
            _testConfigPath = Path.Combine(Path.GetTempPath(), "test-config.json");
            _sut = new ConfigurationManager(new Mock<ILogger<ConfigurationManager>>().Object, _testConfigPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_testConfigPath))
            {
                File.Delete(_testConfigPath);
            }
        }

        [Test]
        public async Task GetConfigAsync_ConfigFileNotExists_ShouldCreateDefaultConfig()
        {
            // Arrange
            if (File.Exists(_testConfigPath))
            {
                File.Delete(_testConfigPath);
            }

            // Act
            var config = await _sut.GetConfigAsync();

            // Assert
            Assert.That(config, Is.Not.Null);
            Assert.That(config.Network.MaxRetryAttempts, Is.EqualTo(3));
            Assert.That(config.Security.EnableAuditLogging, Is.True);

            // 設定ファイルが作成されていることを確認
            Assert.That(File.Exists(_testConfigPath), Is.True);
        }

        [Test]
        public async Task GetConfigAsync_ConfigFileExists_ShouldLoadFromFile()
        {
            // Arrange
            var customConfig = new AppConfig
            {
                Network = new NetworkOperationsConfig { MaxRetryAttempts = 5 },
                Security = new SecurityConfig { EnableAuditLogging = false }
            };

            await AppConfig.SaveAsync(_testConfigPath, customConfig);

            // Act
            var config = await _sut.GetConfigAsync();

            // Assert
            Assert.That(config.Network.MaxRetryAttempts, Is.EqualTo(5));
            Assert.That(config.Security.EnableAuditLogging, Is.False);
        }

        [Test]
        public async Task UpdateConfigAsync_ValidUpdate_ShouldSaveToFile()
        {
            // Arrange
            var originalConfig = await _sut.GetConfigAsync();

            // Act
            await _sut.UpdateConfigAsync(config =>
            {
                config.Network.MaxRetryAttempts = 7;
                config.Security.CredentialRotationDays = 60;
            });

            // Assert
            var updatedConfig = await _sut.GetConfigAsync();
            Assert.That(updatedConfig.Network.MaxRetryAttempts, Is.EqualTo(7));
            Assert.That(updatedConfig.Security.CredentialRotationDays, Is.EqualTo(60));

            // ファイルに保存されていることを確認
            var fileConfig = await AppConfig.LoadAsync(_testConfigPath);
            Assert.That(fileConfig.Network.MaxRetryAttempts, Is.EqualTo(7));
        }

        [Test]
        public async Task UpdateConfigAsync_InvalidUpdate_ShouldThrowException()
        {
            // Act & Assert
            var ex = Assert.ThrowsAsync<ArgumentException>(() =>
                _sut.UpdateConfigAsync(config => config.Network.MaxRetryAttempts = 100)); // 無効な値

            Assert.That(ex.Message, Contains.Substring("Invalid configuration"));
        }
    }

    /// <summary>
    /// パフォーマンステストスイート
    /// </summary>
    [TestFixture]
    public class WindowsNetworkScannerPerformanceTests
    {
        private WindowsNetworkScanner _sut;

        [SetUp]
        public void SetUp()
        {
            var loggerMock = new Mock<ILogger<WindowsNetworkScanner>>();
            var cacheMock = new Mock<IMemoryCache>();
            var rateLimiterMock = new Mock<IRateLimiter>();
            var adapterManagerMock = new Mock<IWifiAdapterManager>();

            _sut = new WindowsNetworkScanner(
                loggerMock.Object,
                cacheMock.Object,
                rateLimiterMock.Object,
                adapterManagerMock.Object);
        }

        [Test]
        [Timeout(5000)] // 5秒以内に完了することを期待
        public async Task ScanAsync_ShouldCompleteWithinTimeout()
        {
            // Arrange
            var cancellationToken = CancellationToken.None;

            // Act
            var result = await _sut.ScanAsync(cancellationToken);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
        }

        [Test]
        public async Task ScanAsync_MultipleConcurrentCalls_ShouldHandleConcurrency()
        {
            // Arrange
            var tasks = new List<Task<NetworkOperationResult<List<NetworkInfo>>>>();
            var cancellationToken = CancellationToken.None;

            // Act
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(_sut.ScanAsync(cancellationToken));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.That(results.All(r => r.IsSuccess), Is.True);
            Assert.That(results.All(r => r.Data != null), Is.True);
        }
    }
}
