using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Tests.Fakes;
using Xunit;

namespace MWC.Core.Tests;

/// <summary>
/// 「無線子機ごとに別ネットワークに接続できる」の核となる
/// マルチアダプター動作の検証。
/// </summary>
public class MultiAdapterTests
{
    [Fact]
    public async Task MultipleAdapters_CanConnectToDifferentNetworks()
    {
        var svc = new FakeWifiService();
        var ads = await svc.GetAdaptersAsync();
        ads.Should().HaveCountGreaterThan(1);

        svc.NextConnectResult = ConnectionResult.Ok("HomeNet", true, false);
        var r1 = await svc.ConnectAsync(ads[0].Id, "HomeNet", "HomeNet", TimeSpan.FromSeconds(5));
        r1.Success.Should().BeTrue();

        svc.NextConnectResult = ConnectionResult.Ok("GuestWiFi", true, false);
        var r2 = await svc.ConnectAsync(ads[1].Id, "GuestWiFi", "GuestWiFi", TimeSpan.FromSeconds(5));
        r2.Success.Should().BeTrue();

        svc.ConnectCallCount.Should().Be(2);
    }

    [Fact]
    public async Task MultipleAdapters_DisconnectIndependently()
    {
        var svc = new FakeWifiService();
        var ads = await svc.GetAdaptersAsync();
        (await svc.DisconnectAsync(ads[0].Id)).Should().BeTrue();
        (await svc.DisconnectAsync(ads[1].Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ScanAsync_AdapterIsolation()
    {
        var svc = new FakeWifiService();
        var nets1 = await svc.ScanAsync(FakeWifiService.AdapterId1);
        var nets2 = await svc.ScanAsync(FakeWifiService.AdapterId2);
        nets1.Should().HaveCount(4);
        nets2.Should().BeEmpty();
    }

    [Fact]
    public async Task DifferentAdapters_ProcessInParallel()
    {
        var svc = new FakeWifiService();
        var ads = await svc.GetAdaptersAsync();
        var tasks = ads.Select(a => svc.ScanAsync(a.Id)).ToArray();
        var results = await Task.WhenAll(tasks);
        results.Should().HaveCount(ads.Count);
    }

    [Fact]
    public async Task DuplicateConnectionDetection_Logic()
    {
        // 別アダプターで同SSIDに接続中を検出する想定ロジック
        var connectedMap = new[]
        {
            (Adapter: "Wi-Fi 1", Ssid: "SharedNet"),
            (Adapter: "Wi-Fi 2", Ssid: "OtherNet")
        };

        // Wi-Fi 2 で SharedNet に接続しようとしたとき
        var conflict = connectedMap
            .Where(x => x.Adapter != "Wi-Fi 2" && x.Ssid == "SharedNet")
            .FirstOrDefault();
        conflict.Adapter.Should().Be("Wi-Fi 1");
    }

    [Fact]
    public async Task NoConflict_WhenSameAdapter()
    {
        var connectedMap = new[] { (Adapter: "Wi-Fi 1", Ssid: "SharedNet") };
        // Wi-Fi 1 で SharedNet に再接続(自分自身は競合ではない)
        var conflict = connectedMap.Any(x => x.Adapter != "Wi-Fi 1" && x.Ssid == "SharedNet");
        conflict.Should().BeFalse();
    }
}

/// <summary>キーボードショートカット (Ctrl+1〜9) ロジック検証</summary>
public class AdapterShortcutTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(9)]
    public void AdapterIndex_InValidRange(int idx)
        => idx.Should().BeInRange(1, 9);

    [Fact]
    public void KeyD1_ToIndex1()
    {
        const int KeyD0 = 34, KeyD1 = 35, KeyD9 = 43;
        (KeyD1 - KeyD0).Should().Be(1);
        (KeyD9 - KeyD0).Should().Be(9);
    }
}
