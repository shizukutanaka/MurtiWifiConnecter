using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MWC.Core.Abstractions;
using MWC.Core.Models;

namespace MWC.Core.Tests.Fakes;

/// <summary>
/// ハードウェア不要のテスト用 IWifiService 実装。
/// 決定論的な応答を返す。CI で実行可能。
/// </summary>
public sealed class FakeWifiService : IWifiService
{
    public static readonly Guid AdapterId1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid AdapterId2 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    public List<WifiAdapter> FakeAdapters { get; } = new()
    {
        new WifiAdapter { Id = AdapterId1, Name = "Wi-Fi", Description = "Intel Wi-Fi 6E AX211",
                          State = AdapterState.Connected, ConnectedSsid = "HomeNet" },
        new WifiAdapter { Id = AdapterId2, Name = "Wi-Fi 2", Description = "Realtek RTL8852AE",
                          State = AdapterState.Disconnected }
    };

    public List<WifiNetwork> FakeNetworks { get; } = new()
    {
        new WifiNetwork
        {
            Ssid = "HomeNet", SignalQuality = 90, Rssi = -45,
            Auth = AuthMethod.WPA3SAE, Cipher = CipherType.AES,
            Band = WifiBand.Band5GHz, Channel = 36, ChannelWidth = 80,
            Phy = PhyType.Dot11ax, MaxLinkSpeedMbps = 2400,
            IsConnected = true, HasProfile = true, ProfileName = "HomeNet",
            BssEntries = new[]
            {
                new BssInfo { Bssid = "70:DE:E2:AA:BB:CC", Rssi = -45, Channel = 36,
                              FrequencyMhz = 5180, Phy = PhyType.Dot11ax, ChannelWidth = 80 }
            }
        },
        new WifiNetwork
        {
            Ssid = "GuestWiFi", SignalQuality = 60, Rssi = -65,
            Auth = AuthMethod.WPA2PSK, Cipher = CipherType.AES,
            Band = WifiBand.Band2_4GHz, Channel = 6, ChannelWidth = 20,
            Phy = PhyType.Dot11n, MaxLinkSpeedMbps = 300,
            BssEntries = new[]
            {
                new BssInfo { Bssid = "04:18:D6:CC:DD:EE", Rssi = -65, Channel = 6,
                              FrequencyMhz = 2437, Phy = PhyType.Dot11n, ChannelWidth = 20 }
            }
        },
        new WifiNetwork
        {
            Ssid = "Corp-Enterprise", SignalQuality = 40, Rssi = -75,
            Auth = AuthMethod.WPA2Enterprise, Cipher = CipherType.AES,
            Band = WifiBand.Band5GHz, Channel = 100, ChannelWidth = 40,
            Phy = PhyType.Dot11ac,
            BssEntries = new[]
            {
                new BssInfo { Bssid = "00:06:53:FF:FF:FF", Rssi = -75, Channel = 100,
                              FrequencyMhz = 5500, Phy = PhyType.Dot11ac, ChannelWidth = 40 }
            }
        },
        new WifiNetwork
        {
            Ssid = "WiFi7-Test", SignalQuality = 80, Rssi = -50,
            Auth = AuthMethod.WPA3SAE, Cipher = CipherType.GCMP256,
            Band = WifiBand.Band6GHz, Channel = 37, ChannelWidth = 320,
            Phy = PhyType.Dot11be, MaxLinkSpeedMbps = 46000,
            BssEntries = new[]
            {
                new BssInfo { Bssid = "34:FC:8B:11:22:33", Rssi = -50, Channel = 37,
                              FrequencyMhz = 6135, Phy = PhyType.Dot11be, ChannelWidth = 320 }
            }
        },
    };

    // 接続シミュレーション
    public ConnectionResult NextConnectResult { get; set; } =
        ConnectionResult.Ok("HomeNet", true, false);
    public bool NextRegisterResult { get; set; } = true;
    public int ScanCallCount  { get; private set; }
    public int ConnectCallCount { get; private set; }

    private readonly Channel<WifiEvent> _events = Channel.CreateUnbounded<WifiEvent>();

    public Task<IReadOnlyList<WifiAdapter>> GetAdaptersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WifiAdapter>>(FakeAdapters);

    public Task<IReadOnlyList<WifiNetwork>> ScanAsync(Guid adapterId, CancellationToken ct = default)
    {
        ScanCallCount++;
        // adapterId に応じて返す(Adapter2 は空)
        if (adapterId == AdapterId2)
            return Task.FromResult<IReadOnlyList<WifiNetwork>>(Array.Empty<WifiNetwork>());
        return Task.FromResult<IReadOnlyList<WifiNetwork>>(FakeNetworks);
    }

    public Task<bool> RegisterProfileAsync(Guid adapterId, string profileXml, bool overwrite,
        CancellationToken ct = default)
        => Task.FromResult(NextRegisterResult);

    public async Task<ConnectionResult> ConnectAsync(Guid adapterId, string profileName, string ssid,
        TimeSpan timeout, CancellationToken ct = default)
    {
        ConnectCallCount++;
        await _events.Writer.WriteAsync(
            new WifiEvent(adapterId, WifiEventType.Connecting, ssid, DateTimeOffset.UtcNow), ct);
        await Task.Delay(50, ct);  // 非同期を演出
        await _events.Writer.WriteAsync(
            new WifiEvent(adapterId,
                NextConnectResult.Success ? WifiEventType.Connected : WifiEventType.Failed,
                ssid, DateTimeOffset.UtcNow), ct);
        return NextConnectResult;
    }

    public Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> DeleteProfileAsync(Guid adapterId, string profileName,
        CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<IReadOnlyList<string>> ListProfilesAsync(Guid adapterId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(new[] { "HomeNet", "GuestWiFi" });

    public async IAsyncEnumerable<WifiEvent> SubscribeEventsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var ev in _events.Reader.ReadAllAsync(ct))
            yield return ev;
    }
}
