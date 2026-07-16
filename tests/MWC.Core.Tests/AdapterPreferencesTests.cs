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
    [Fact]
    public void Get_NewAdapter_ReturnsDefaults()
    {
        var svc = new AdapterPreferencesService();
        var p   = svc.Get(Guid.NewGuid());
        p.IsEnabled.Should().BeTrue();
        p.PreferredBand.Should().Be(BandPreference.Any);
        p.PinnedSsids.Should().BeEmpty();
        p.AutoConnectPriority.Should().BeEmpty();
    }

    [Fact]
    public void SetLabel_PersistsCustomName()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetLabel(id, "自宅用ドングル");
        svc.Get(id).CustomLabel.Should().Be("自宅用ドングル");
    }

    [Fact]
    public void PinSsid_AddsToTop_NoDuplicates()
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
    public void UnpinSsid_RemovesFromList()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "Home");
        svc.PinSsid(id, "Office");
        svc.UnpinSsid(id, "Home");
        svc.Get(id).PinnedSsids.Should().BeEquivalentTo(new[] { "Office" });
    }

    [Fact]
    public void PinSsid_ExceedsMax_TruncatesTo20()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        for (int i = 0; i < 25; i++)
            svc.PinSsid(id, $"Net{i}");
        svc.Get(id).PinnedSsids.Should().HaveCount(20);
    }

    [Fact]
    public void SetBandFilter_PersistsBand()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetBandFilter(id, BandPreference.Only5GHz);
        svc.Get(id).PreferredBand.Should().Be(BandPreference.Only5GHz);
    }

    [Fact]
    public void SetEnabled_TogglesIsEnabled()
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
    public void BandPreference_AllValuesValid(BandPreference band)
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetBandFilter(id, band);
        svc.Get(id).PreferredBand.Should().Be(band);
    }

    [Fact]
    public void MultipleAdapters_HaveIndependentPreferences()
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
        svc.Get(id1).PinnedSsids.Should().BeEquivalentTo(new[] { "HomeWiFi" });
        svc.Get(id2).PinnedSsids.Should().BeEquivalentTo(new[] { "MobileRouter" });
    }

    [Fact]
    public void All_ReturnsAllConfiguredAdapters()
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
    [Fact]
    public void IsAutoReconnectEnabled_WithPinnedSsids_ReturnsTrue()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "Home");
        svc.IsAutoReconnectEnabled(id).Should().BeTrue();
    }

    [Fact]
    public void IsAutoReconnectEnabled_NoPins_ReturnsFalse()
    {
        var svc = new AdapterPreferencesService();
        svc.IsAutoReconnectEnabled(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void IsAutoReconnectEnabled_DisabledAdapter_ReturnsFalse()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "X");
        svc.SetEnabled(id, false);
        svc.IsAutoReconnectEnabled(id).Should().BeFalse();
    }

    [Fact]
    public void PickBestSsid_ReturnsFirst_FromPriority()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, new[] { "First", "Second" });
        var best = svc.PickBestSsid(id, new[] { "Second", "First", "Third" });
        best.Should().Be("First");
    }

    [Fact]
    public void PickBestSsid_FallsBackToPinned()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "Pinned1");
        var best = svc.PickBestSsid(id, new[] { "Pinned1", "Other" });
        best.Should().Be("Pinned1");
    }

    [Fact]
    public void PickBestSsid_NoMatch_ReturnsNull()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "Home");
        var best = svc.PickBestSsid(id, new[] { "Office", "Cafe" });
        best.Should().BeNull();
    }

    [Fact]
    public void AddPreferred_NoDuplicates()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "Net1");
        svc.AddPreferred(id, "Net1");
        svc.GetPreferredNetworks(id).Should().HaveCount(1);
    }

    [Fact]
    public void RemovePreferred_RemovesFromList()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "A");
        svc.AddPreferred(id, "B");
        svc.RemovePreferred(id, "A");
        svc.GetPreferredNetworks(id).Should().BeEquivalentTo(new[] { "B" });
    }

    [Fact]
    public void MoveUp_SwapsWithPrevious()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, new[] { "A", "B", "C" });
        svc.MoveUp(id, "B");
        svc.GetPreferredNetworks(id).Should().ContainInOrder("B", "A", "C");
    }

    [Fact]
    public void MoveUp_FirstElement_NoChange()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, new[] { "A", "B" });
        svc.MoveUp(id, "A");
        svc.GetPreferredNetworks(id).Should().ContainInOrder("A", "B");
    }

    [Fact]
    public async Task ConcurrentSaveAndRead_NoCrash()
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

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 80 && !cts.IsCancellationRequested; i++)
            {
                _ = svc.All();
                foreach (var id in ids)
                {
                    _ = svc.Get(id);
                    _ = svc.PickBestSsid(id, new[] { "SSID0", "SSID3" });
                }
            }
        }, cts.Token));

        await Task.WhenAll(writers.Concat(readers));
        svc.All().Should().NotBeEmpty();
    }
}
