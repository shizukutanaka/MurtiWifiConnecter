using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

/// <summary>
/// 子機ごとの接続好み設定の振る舞いテスト。
/// </summary>
public class AdapterPreferencesServiceTests
{
    private static readonly string[] Office = new[] { "Office" };
    private static readonly string[] HomeWiFi = new[] { "HomeWiFi" };
    private static readonly string[] MobileRouter = new[] { "MobileRouter" };
    [Fact]
    public void GetNewAdapterReturnsDefaults()
    {
        var svc = new AdapterPreferencesService();
        var p   = svc.Get(Guid.NewGuid());
        p.IsEnabled.Should().BeTrue();
        p.PreferredBand.Should().Be(BandPreference.Any);
        p.PinnedSsids.Should().BeEmpty();
        p.AutoConnectPriority.Should().BeEmpty();
    }

    [Fact]
    public void SetLabelPersistsCustomName()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetLabel(id, "自宅用ドングル");
        svc.Get(id).CustomLabel.Should().Be("自宅用ドングル");
    }

    [Fact]
    public void PinSsidAddsToTopNoDuplicates()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "Home");
        svc.PinSsid(id, "Office");
        svc.PinSsid(id, "Home");  // 重複
        var list = svc.Get(id).PinnedSsids.ToList();
        list.Should().HaveCount(2);
        list[0].Should().Be("Office");  // 最新が先頭
    }

    [Fact]
    public void UnpinSsidRemovesFromList()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "Home");
        svc.PinSsid(id, "Office");
        svc.UnpinSsid(id, "Home");
        svc.Get(id).PinnedSsids.Should().BeEquivalentTo(Office);
    }

    [Fact]
    public void PinSsidExceedsMaxTruncatesTo20()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        for (int i = 0; i < 25; i++)
            svc.PinSsid(id, $"Net{i}");
        svc.Get(id).PinnedSsids.Should().HaveCount(20);
    }

    [Fact]
    public void SetBandFilterPersistsBand()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetBandFilter(id, BandPreference.Only5GHz);
        svc.Get(id).PreferredBand.Should().Be(BandPreference.Only5GHz);
    }

    [Fact]
    public void SetEnabledTogglesIsEnabled()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetEnabled(id, false);
        svc.Get(id).IsEnabled.Should().BeFalse();
        svc.SetEnabled(id, true);
        svc.Get(id).IsEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(BandPreference.Any)]
    [InlineData(BandPreference.Only2_4GHz)]
    [InlineData(BandPreference.Only5GHz)]
    [InlineData(BandPreference.Only6GHz)]
    public void BandPreferenceAllValuesValid(BandPreference band)
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetBandFilter(id, band);
        svc.Get(id).PreferredBand.Should().Be(band);
    }

    [Fact]
    public void MultipleAdaptersHaveIndependentPreferences()
    {
        var svc = new AdapterPreferencesService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        svc.SetLabel(id1, "Adapter A");
        svc.SetLabel(id2, "Adapter B");
        svc.SetBandFilter(id1, BandPreference.Only2_4GHz);
        svc.SetBandFilter(id2, BandPreference.Only5GHz);
        svc.PinSsid(id1, "HomeWiFi");
        svc.PinSsid(id2, "MobileRouter");

        svc.Get(id1).CustomLabel.Should().Be("Adapter A");
        svc.Get(id2).CustomLabel.Should().Be("Adapter B");
        svc.Get(id1).PreferredBand.Should().Be(BandPreference.Only2_4GHz);
        svc.Get(id2).PreferredBand.Should().Be(BandPreference.Only5GHz);
        svc.Get(id1).PinnedSsids.Should().BeEquivalentTo(HomeWiFi);
        svc.Get(id2).PinnedSsids.Should().BeEquivalentTo(MobileRouter);
    }

    [Fact]
    public void AllReturnsAllConfiguredAdapters()
    {
        var svc = new AdapterPreferencesService();
        svc.SetLabel(Guid.NewGuid(), "X");
        svc.SetLabel(Guid.NewGuid(), "Y");
        svc.All().Count.Should().BeGreaterOrEqualTo(2);
    }
}

// ═══════════════════════════════════════════════
//  v1.9.3 新メソッド テスト
// ═══════════════════════════════════════════════
public class AdapterPreferencesExtendedTests
{
    private static readonly string[] FirstSecond = new[] { "First", "Second" };
    private static readonly string[] SecondFirstThird = new[] { "Second", "First", "Third" };
    private static readonly string[] Pinned1Other = new[] { "Pinned1", "Other" };
    private static readonly string[] OfficeCafe = new[] { "Office", "Cafe" };
    private static readonly string[] B = new[] { "B" };
    private static readonly string[] ABC = new[] { "A", "B", "C" };
    private static readonly string[] AB = new[] { "A", "B" };
    private static readonly string[] SSID0SSID3 = new[] { "SSID0", "SSID3" };
    [Fact]
    public void IsAutoReconnectEnabledWithPinnedSsidsReturnsTrue()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "Home");
        svc.IsAutoReconnectEnabled(id).Should().BeTrue();
    }

    [Fact]
    public void IsAutoReconnectEnabledNoPinsReturnsFalse()
    {
        var svc = new AdapterPreferencesService();
        svc.IsAutoReconnectEnabled(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void IsAutoReconnectEnabledDisabledAdapterReturnsFalse()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "X");
        svc.SetEnabled(id, false);
        svc.IsAutoReconnectEnabled(id).Should().BeFalse();
    }

    [Fact]
    public void PickBestSsidReturnsFirstFromPriority()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, FirstSecond);
        var best = svc.PickBestSsid(id, SecondFirstThird);
        best.Should().Be("First");
    }

    [Fact]
    public void PickBestSsidFallsBackToPinned()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "Pinned1");
        var best = svc.PickBestSsid(id, Pinned1Other);
        best.Should().Be("Pinned1");
    }

    [Fact]
    public void PickBestSsidNoMatchReturnsNull()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "Home");
        var best = svc.PickBestSsid(id, OfficeCafe);
        best.Should().BeNull();
    }

    [Fact]
    public void AddPreferredNoDuplicates()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "Net1");
        svc.AddPreferred(id, "Net1");
        svc.GetPreferredNetworks(id).Should().HaveCount(1);
    }

    [Fact]
    public void RemovePreferredRemovesFromList()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "A");
        svc.AddPreferred(id, "B");
        svc.RemovePreferred(id, "A");
        svc.GetPreferredNetworks(id).Should().BeEquivalentTo(B);
    }

    [Fact]
    public void MoveUpSwapsWithPrevious()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, ABC);
        svc.MoveUp(id, "B");
        svc.GetPreferredNetworks(id).Should().ContainInOrder("B", "A", "C");
    }

    [Fact]
    public void MoveUpFirstElementNoChange()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, AB);
        svc.MoveUp(id, "A");
        svc.GetPreferredNetworks(id).Should().ContainInOrder("A", "B");
    }

    [Fact]
    public async Task ConcurrentSaveAndReadNoCrash()
    {
        // AutoReconnectService(背景スレッド)が Get/PickBestSsid で読みつつ
        // UI スレッドが Save する状況を再現。内部 Dictionary がロック無しだと
        // "collection was modified" 等で落ちるため、それが起きないことを検証。
        var svc = new AdapterPreferencesService();
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var writers = ids.Select(id => Task.Run(() =>
        {
            for (int i = 0; i < 40 && !cts.IsCancellationRequested; i++)
                svc.PinSsid(id, $"SSID{i % 5}");
        }, cts.Token));

        // 仮引数を `_` にしない: 内側の `_ = svc.All();` が破棄ではなく
        // この int への代入として束縛され、CS0029 でビルドが落ちる。
        var readers = Enumerable.Range(0, 4).Select(reader => Task.Run(() =>
        {
            for (int i = 0; i < 80 && !cts.IsCancellationRequested; i++)
            {
                _ = svc.All();
                foreach (var id in ids)
                {
                    _ = svc.Get(id);
                    _ = svc.PickBestSsid(id, SSID0SSID3);
                }
            }
        }, cts.Token));

        await Task.WhenAll(writers.Concat(readers));
        svc.All().Should().NotBeEmpty();
    }
}
