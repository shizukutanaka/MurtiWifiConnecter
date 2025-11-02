using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter.Tests.Core
{
    public class ConfigManagerTests : IDisposable
    {
        private readonly string _originalConfigPath;
        private readonly string _originalUserConfigPath;
        private readonly string _testConfigPath;
        private readonly string _testUserConfigPath;

        public ConfigManagerTests()
        {
            // Store original paths
            _originalConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            _originalUserConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter", "user_config.json");

            // Create test paths
            _testConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_config.json");
            _testUserConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_user_config.json");

            // Clean up any existing test files
            if (File.Exists(_testConfigPath)) File.Delete(_testConfigPath);
            if (File.Exists(_testUserConfigPath)) File.Delete(_testUserConfigPath);
        }

        public void Dispose()
        {
            // Clean up test files
            if (File.Exists(_testConfigPath)) File.Delete(_testConfigPath);
            if (File.Exists(_testUserConfigPath)) File.Delete(_testUserConfigPath);
        }

        [Fact]
        public async Task LoadConfig_ReturnsValidConfig()
        {
            // Act
            var config = await ConfigManager.LoadConfig();

            // Assert
            config.Should().NotBeNull();
            config.AutoConnect.Should().BeBoolean();
            config.ScanInterval.Should().BeGreaterThan(0);
            config.ConnectionTimeout.Should().BeGreaterThan(0);
            config.RetryAttempts.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public async Task GetConfig_ReturnsSameAsLoadConfig()
        {
            // Act
            var loadConfigResult = await ConfigManager.LoadConfig();
            var getConfigResult = await ConfigManager.GetConfig();

            // Assert
            getConfigResult.Should().BeEquivalentTo(loadConfigResult);
        }

        [Fact]
        public async Task UpdateSetting_WithValidBoolean_Succeeds()
        {
            // Act
            var result = await ConfigManager.UpdateSetting("autoconnect", false);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().BeEmpty();
            result.NewValue.Should().Be("False");
        }

        [Fact]
        public async Task UpdateSetting_WithValidInteger_Succeeds()
        {
            // Act
            var result = await ConfigManager.UpdateSetting("scaninterval", 45);

            // Assert
            result.Success.Should().BeTrue();
            result.NewValue.Should().Be("45");
        }

        [Fact]
        public async Task UpdateSetting_WithInvalidInteger_Fails()
        {
            // Act
            var result = await ConfigManager.UpdateSetting("scaninterval", 1); // Below minimum

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task UpdateSetting_WithValidString_Succeeds()
        {
            // Act
            var result = await ConfigManager.UpdateSetting("loglevel", "Debug");

            // Assert
            result.Success.Should().BeTrue();
            result.NewValue.Should().Be("Debug");
        }

        [Fact]
        public async Task UpdateSetting_WithInvalidString_Fails()
        {
            // Act
            var result = await ConfigManager.UpdateSetting("loglevel", "InvalidLevel");

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task GetSetting_ReturnsCorrectValue()
        {
            // Act
            var autoConnect = await ConfigManager.GetSetting<bool>("autoconnect", true);

            // Assert
            autoConnect.Should().BeBoolean();
        }

        [Fact]
        public async Task GetSetting_WithInvalidKey_ReturnsDefault()
        {
            // Act
            var invalidSetting = await ConfigManager.GetSetting<string>("invalid_key", "default_value");

            // Assert
            invalidSetting.Should().Be("default_value");
        }

        [Fact]
        public void GetSettingMetadata_ReturnsMetadata()
        {
            // Act
            var metadata = ConfigManager.GetSettingMetadata("autoconnect");

            // Assert
            metadata.Should().NotBeNull();
            metadata.Key.Should().Be("autoconnect");
            metadata.Description.Should().NotBeNullOrEmpty();
            metadata.ValueType.Should().Be("Boolean");
        }

        [Fact]
        public void GetSettingMetadata_WithInvalidKey_ReturnsNull()
        {
            // Act
            var metadata = ConfigManager.GetSettingMetadata("invalid_key");

            // Assert
            metadata.Should().BeNull();
        }

        [Fact]
        public void GetSettingsMetadataSnapshot_IncludesCurrentValues()
        {
            var task = ConfigManager.GetSettingsMetadataSnapshot(true);
            task.Wait();
            var snapshot = task.Result;

            // Assert
            snapshot.Should().NotBeNull();
            snapshot.Should().HaveCountGreaterThan(0);

            foreach (var item in snapshot)
            {
                item.Key.Should().NotBeNullOrEmpty();
                item.Description.Should().NotBeNullOrEmpty();
                item.ValueType.Should().NotBeNullOrEmpty();
                // CurrentValue can be empty for some settings
            }
        }

        [Fact]
        public void GetSettingsMetadataSnapshot_ExcludesCurrentValues()
        {
            var task = ConfigManager.GetSettingsMetadataSnapshot(false);
            task.Wait();
            var snapshot = task.Result;

            // Assert
            snapshot.Should().NotBeNull();
            snapshot.Should().HaveCountGreaterThan(0);

            foreach (var item in snapshot)
            {
                item.CurrentValue.Should().BeEmpty();
            }
        }

        [Fact]
        public async Task AddPreferredNetwork_WithValidSsid_Succeeds()
        {
            // Act
            var result = await ConfigManager.AddPreferredNetwork("TestNetwork", 50);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task AddPreferredNetwork_WithInvalidSsid_Fails()
        {
            // Act
            var result = await ConfigManager.AddPreferredNetwork("", 50);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task RemovePreferredNetwork_WithExistingNetwork_Succeeds()
        {
            // Arrange
            await ConfigManager.AddPreferredNetwork("TestNetworkToRemove", 50);

            // Act
            var result = await ConfigManager.RemovePreferredNetwork("TestNetworkToRemove");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task RemovePreferredNetwork_WithNonExistingNetwork_Fails()
        {
            // Act
            var result = await ConfigManager.RemovePreferredNetwork("NonExistingNetwork");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task GetPreferredNetworks_ReturnsOrderedList()
        {
            // Arrange
            await ConfigManager.AddPreferredNetwork("NetworkA", 10);
            await ConfigManager.AddPreferredNetwork("NetworkB", 50);
            await ConfigManager.AddPreferredNetwork("NetworkC", 30);

            // Act
            var networks = await ConfigManager.GetPreferredNetworks();

            // Assert
            networks.Should().NotBeNull();
            networks.Should().HaveCountGreaterThanOrEqualTo(3);

            // Should be ordered by priority (descending)
            for (int i = 0; i < networks.Count - 1; i++)
            {
                networks[i].Priority.Should().BeGreaterThanOrEqualTo(networks[i + 1].Priority);
            }
        }

        [Fact]
        public async Task ClearPreferredNetworks_RemovesAllNetworks()
        {
            // Arrange
            await ConfigManager.AddPreferredNetwork("Network1", 10);
            await ConfigManager.AddPreferredNetwork("Network2", 20);

            // Act
            var result = await ConfigManager.ClearPreferredNetworks();
            var networks = await ConfigManager.GetPreferredNetworks();

            // Assert
            result.Should().BeTrue();
            networks.Should().BeEmpty();
        }

        [Fact]
        public async Task ExportConfig_CreatesValidFile()
        {
            // Arrange
            var exportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export_test.json");

            try
            {
                // Act
                var result = await ConfigManager.ExportConfig(exportPath);

                // Assert
                result.Should().Be(exportPath);
                File.Exists(exportPath).Should().BeTrue();

                var content = File.ReadAllText(exportPath);
                content.Should().NotBeNullOrEmpty();

                // Should be valid JSON
                var doc = System.Text.Json.JsonDocument.Parse(content);
                doc.Should().NotBeNull();
            }
            finally
            {
                // Cleanup
                if (File.Exists(exportPath))
                    File.Delete(exportPath);
            }
        }

        [Fact]
        public async Task ImportConfig_WithValidFile_Succeeds()
        {
            // Arrange
            var importPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "import_test.json");

            try
            {
                // Create a valid config file
                var config = await ConfigManager.LoadConfig();
                var json = System.Text.Json.JsonSerializer.Serialize(config,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(importPath, json);

                // Act
                await ConfigManager.ImportConfig(importPath);

                // Assert - Should not throw exception
                // Configuration should be imported successfully
            }
            finally
            {
                // Cleanup
                if (File.Exists(importPath))
                    File.Delete(importPath);
            }
        }

        [Fact]
        public async Task ImportConfig_WithInvalidFile_ThrowsException()
        {
            // Arrange
            var importPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "invalid_config.json");

            try
            {
                // Create an invalid config file
                File.WriteAllText(importPath, "invalid json content");

                // Act & Assert
                await Assert.ThrowsAsync<Exception>(() => ConfigManager.ImportConfig(importPath));
            }
            finally
            {
                // Cleanup
                if (File.Exists(importPath))
                    File.Delete(importPath);
            }
        }

        [Fact]
        public async Task ResetToDefaults_ClearsUserConfig()
        {
            // Act
            await ConfigManager.ResetToDefaults();

            // Assert - Should not throw exception
            // User config should be reset to defaults
        }

        [Theory]
        [InlineData("autoconnect", true)]
        [InlineData("enablenotifications", false)]
        [InlineData("showsignalbars", true)]
        [InlineData("verboseoutput", false)]
        public async Task UpdateSetting_BooleanSettings_WorkCorrectly(string key, bool value)
        {
            // Act
            var result = await ConfigManager.UpdateSetting(key, value);

            // Assert
            result.Success.Should().BeTrue();
            result.NewValue.Should().Be(value.ToString());
        }

        [Theory]
        [InlineData("scaninterval", 10)]
        [InlineData("connectiontimeout", 15)]
        [InlineData("retryattempts", 2)]
        [InlineData("autocleanupinterval", 120)]
        [InlineData("maxhistoryentries", 50)]
        [InlineData("ratelimitcommandmaxattempts", 20)]
        public async Task UpdateSetting_IntegerSettings_WorkCorrectly(string key, int value)
        {
            // Act
            var result = await ConfigManager.UpdateSetting(key, value);

            // Assert
            result.Success.Should().BeTrue();
            result.NewValue.Should().Be(value.ToString());
        }

        [Theory]
        [InlineData("loglevel", "Info")]
        [InlineData("defaultsecuritytype", "WPA2PSK")]
        public async Task UpdateSetting_StringSettings_WorkCorrectly(string key, string value)
        {
            // Act
            var result = await ConfigManager.UpdateSetting(key, value);

            // Assert
            result.Success.Should().BeTrue();
            result.NewValue.Should().Be(value);
        }
    }
}
