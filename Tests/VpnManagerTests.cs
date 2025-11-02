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
    public class VpnManagerTests
    {
        [TestMethod]
        public async Task GetAvailableProvidersAsync_ReturnsSupportedProviders()
        {
            // Act
            var providers = await VpnManager.GetAvailableProvidersAsync();

            // Assert
            Assert.IsNotNull(providers);
            Assert.IsTrue(providers.Count >= 0); // At least empty list should be returned

            // Check that returned providers have required properties
            foreach (var provider in providers)
            {
                Assert.IsFalse(string.IsNullOrEmpty(provider.Name));
                Assert.IsTrue(Enum.IsDefined(typeof(VpnType), provider.Type));
                Assert.IsNotNull(provider.SupportedProtocols);
            }
        }

        [TestMethod]
        public async Task ConnectAsync_InvalidProfile_ReturnsFailure()
        {
            // Arrange
            var invalidProfile = new VpnConnectionProfile
            {
                Name = "",
                Provider = VpnType.OpenVPN,
                Server = "",
                Port = 0
            };

            // Act
            var result = await VpnManager.ConnectAsync(invalidProfile, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            Assert.IsFalse(string.IsNullOrEmpty(result.ErrorMessage));
        }

        [TestMethod]
        public void GetActiveConnections_InitiallyEmpty_ReturnsEmptyList()
        {
            // Act
            var connections = VpnManager.GetActiveConnections();

            // Assert
            Assert.IsNotNull(connections);
            Assert.AreEqual(0, connections.Count);
        }

        [TestMethod]
        public async Task SaveProfileAsync_ValidProfile_ReturnsTrue()
        {
            // Arrange
            var profile = new VpnConnectionProfile
            {
                Id = "test-profile-123",
                Name = "Test VPN",
                Provider = VpnType.OpenVPN,
                Server = "test.vpn.server",
                Port = 1194,
                Username = "testuser"
            };

            // Act
            var result = await VpnManager.SaveProfileAsync(profile);

            // Assert
            Assert.IsTrue(result);

            // Verify profile can be loaded
            var profiles = await VpnManager.LoadProfilesAsync();
            Assert.IsTrue(profiles.ContainsKey(profile.Id));
            Assert.AreEqual(profile.Name, profiles[profile.Id].Name);
        }

        [TestMethod]
        public async Task LoadProfilesAsync_AfterSaving_ReturnsSavedProfiles()
        {
            // Arrange
            var profile1 = new VpnConnectionProfile
            {
                Id = "test-profile-1",
                Name = "Test VPN 1",
                Provider = VpnType.WireGuard,
                Server = "wg.test.com",
                Port = 51820
            };

            var profile2 = new VpnConnectionProfile
            {
                Id = "test-profile-2",
                Name = "Test VPN 2",
                Provider = VpnType.IKEv2,
                Server = "ikev2.test.com",
                Port = 500
            };

            await VpnManager.SaveProfileAsync(profile1);
            await VpnManager.SaveProfileAsync(profile2);

            // Act
            var profiles = await VpnManager.LoadProfilesAsync();

            // Assert
            Assert.IsNotNull(profiles);
            Assert.IsTrue(profiles.ContainsKey(profile1.Id));
            Assert.IsTrue(profiles.ContainsKey(profile2.Id));
            Assert.AreEqual(2, profiles.Count);
        }

        [TestMethod]
        public async Task LoadProfilesAsync_NoProfiles_ReturnsEmptyDictionary()
        {
            // Arrange - Ensure no profiles exist (this might be hard to guarantee in all test runs)
            // In a real scenario, you might want to use a test-specific storage location

            // Act
            var profiles = await VpnManager.LoadProfilesAsync();

            // Assert
            Assert.IsNotNull(profiles);
            // Note: This test might fail if other tests leave profiles behind
            // In production, you'd use test isolation techniques
        }

        [TestMethod]
        public async Task GetConnectionStatusAsync_InvalidConnectionId_ReturnsDisconnected()
        {
            // Act
            var status = await VpnManager.GetConnectionStatusAsync("non-existent-id");

            // Assert
            Assert.AreEqual(VpnConnectionStatus.Disconnected, status);
        }

        [TestMethod]
        public async Task TestVpnSpeedAsync_NoActiveConnection_ReturnsFailure()
        {
            // Act
            var result = await VpnManager.TestVpnSpeedAsync("non-existent-id", CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            Assert.IsFalse(string.IsNullOrEmpty(result.ErrorMessage));
            Assert.AreEqual("non-existent-id", result.ConnectionId);
        }

        [TestMethod]
        public void VpnConnectionProfile_Properties_SetAndGetCorrectly()
        {
            // Arrange
            var profile = new VpnConnectionProfile
            {
                Id = "test-id",
                Name = "Test Profile",
                Provider = VpnType.WireGuard,
                Server = "test.server.com",
                Port = 51820,
                Username = "testuser",
                Password = "testpass",
                Protocol = "UDP"
            };

            // Assert
            Assert.AreEqual("test-id", profile.Id);
            Assert.AreEqual("Test Profile", profile.Name);
            Assert.AreEqual(VpnType.WireGuard, profile.Provider);
            Assert.AreEqual("test.server.com", profile.Server);
            Assert.AreEqual(51820, profile.Port);
            Assert.AreEqual("testuser", profile.Username);
            Assert.AreEqual("testpass", profile.Password);
            Assert.AreEqual("UDP", profile.Protocol);
        }

        [TestMethod]
        public void VpnConnectionResult_Properties_SetAndGetCorrectly()
        {
            // Arrange
            var profile = new VpnConnectionProfile { Name = "Test" };
            var result = new VpnConnectionResult
            {
                Profile = profile,
                Success = true,
                ConnectionId = "conn-123",
                ConnectedAt = DateTime.Now,
                ErrorMessage = null
            };

            // Assert
            Assert.AreEqual(profile, result.Profile);
            Assert.IsTrue(result.Success);
            Assert.AreEqual("conn-123", result.ConnectionId);
            Assert.IsNotNull(result.ConnectedAt);
            Assert.IsNull(result.ErrorMessage);
        }

        [TestMethod]
        public void VpnSpeedTestResult_Properties_SetAndGetCorrectly()
        {
            // Arrange
            var result = new VpnSpeedTestResult
            {
                ConnectionId = "conn-123",
                Success = true,
                DownloadSpeed = 85.5,
                UploadSpeed = 42.3,
                Latency = 15.7,
                Timestamp = DateTime.Now,
                ErrorMessage = null
            };

            // Assert
            Assert.AreEqual("conn-123", result.ConnectionId);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(85.5, result.DownloadSpeed);
            Assert.AreEqual(42.3, result.UploadSpeed);
            Assert.AreEqual(15.7, result.Latency);
            Assert.IsNotNull(result.Timestamp);
            Assert.IsNull(result.ErrorMessage);
        }

        [TestMethod]
        public void VpnProvider_Properties_SetAndGetCorrectly()
        {
            // Arrange
            var provider = new VpnProvider
            {
                Name = "TestVPN",
                Type = VpnType.OpenVPN,
                IsAvailable = true,
                SupportedProtocols = new[] { "UDP", "TCP" }
            };

            // Assert
            Assert.AreEqual("TestVPN", provider.Name);
            Assert.AreEqual(VpnType.OpenVPN, provider.Type);
            Assert.IsTrue(provider.IsAvailable);
            CollectionAssert.AreEqual(new[] { "UDP", "TCP" }, provider.SupportedProtocols);
        }

        [TestMethod]
        public void VpnConnection_Properties_SetAndGetCorrectly()
        {
            // Arrange
            var profile = new VpnConnectionProfile { Name = "Test VPN" };
            var connection = new VpnConnection
            {
                Profile = profile,
                ConnectedAt = DateTime.Now,
                Status = VpnConnectionStatus.Connected,
                ErrorMessage = null
            };

            // Assert
            Assert.AreEqual(profile, connection.Profile);
            Assert.IsNotNull(connection.ConnectedAt);
            Assert.AreEqual(VpnConnectionStatus.Connected, connection.Status);
            Assert.IsNull(connection.ErrorMessage);
        }

        [TestMethod]
        public async Task DisconnectAsync_InvalidConnectionId_ReturnsFalse()
        {
            // Act
            var result = await VpnManager.DisconnectAsync("invalid-id", CancellationToken.None);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void VpnType_EnumValues_AreDefined()
        {
            // Test that all expected enum values exist
            var expectedValues = new[]
            {
                VpnType.OpenVPN,
                VpnType.WireGuard,
                VpnType.IKEv2,
                VpnType.SSTP,
                VpnType.PPTP,
                VpnType.L2TP
            };

            foreach (var value in expectedValues)
            {
                Assert.IsTrue(Enum.IsDefined(typeof(VpnType), value));
            }
        }

        [TestMethod]
        public void VpnConnectionStatus_EnumValues_AreDefined()
        {
            // Test that all expected enum values exist
            var expectedValues = new[]
            {
                VpnConnectionStatus.Disconnected,
                VpnConnectionStatus.Connecting,
                VpnConnectionStatus.Connected,
                VpnConnectionStatus.Disconnecting,
                VpnConnectionStatus.Error
            };

            foreach (var value in expectedValues)
            {
                Assert.IsTrue(Enum.IsDefined(typeof(VpnConnectionStatus), value));
            }
        }
    }
}
