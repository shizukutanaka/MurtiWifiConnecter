using System;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

/// <summary>
/// 子機(アダプター)ごとの優先ネットワーク管理。
/// 主要シナリオを網羅。
/// </summary>
public class PerAdapterPreferencesServiceTests
{
    private static readonly Guid Adapter1 = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid Adapter2 = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

    [Fact]
    public void NewService_NoPreferences_ReturnsEmpty()
    {
        var svc = new AdapterPreferencesService();
        // 一意なGuidなのでテスト環境での既存ファイル影響なし
        var newId = Guid.NewGuid();
        svc.GetPreferredNetworks(newId).Should().BeEmpty();
        svc.GetPreferredNetworks(newId).Should().NotBeNull();
        svc.Get(newId).PinnedSsids.Should().BeEmpty();
    }

    [Fact]
    public void AddPreferred_Single_AppearsInList()
    {
        var svc = new AdapterPreferencesService();
        var id = Guid.NewGuid();
        svc.AddPreferred(id, "TestNet");
        svc.GetPreferredNetworks(id).Should().Contain("TestNet");
    }

    [Fact]
    public void AddPreferred_Duplicate_NotAddedTwice()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "TestNet");
        svc.AddPreferred(id, "TestNet");
        svc.GetPreferredNetworks(id).Should().HaveCount(1);
    }

    [Fact]
    public void DifferentAdapters_HaveSeparatePreferences()
    {
        var svc = new AdapterPreferencesService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        svc.AddPreferred(id1, "Home");
        svc.AddPreferred(id2, "Office");

        svc.GetPreferredNetworks(id1).Should().ContainSingle("Home");
        svc.GetPreferredNetworks(id2).Should().ContainSingle("Office");
        svc.GetPreferredNetworks(id1).Should().NotContain("Office");
    }

    [Fact]
    public void SetAutoConnectPriority_ReplacesEntireList()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "A");
        svc.AddPreferred(id, "B");
        svc.SetAutoConnectPriority(id, new[] { "X", "Y", "Z" });
        svc.GetPreferredNetworks(id).Should().BeEquivalentTo(new[] { "X", "Y", "Z" });
    }

    [Fact]
    public void RemovePreferred_DropsEntry()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "A");
        svc.AddPreferred(id, "B");
        svc.RemovePreferred(id, "A");
        svc.GetPreferredNetworks(id).Should().BeEquivalentTo(new[] { "B" });
    }

    [Fact]
    public void MoveUp_PromotesEntry()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, new[] { "A", "B", "C" });
        svc.MoveUp(id, "C");
        svc.GetPreferredNetworks(id).Should().BeEquivalentTo(new[] { "A", "C", "B" });
    }

    [Fact]
    public void MoveUp_FirstEntry_NoChange()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, new[] { "A", "B" });
        svc.MoveUp(id, "A");
        svc.GetPreferredNetworks(id)[0].Should().Be("A");
    }

    [Fact]
    public void PickBestSsid_ReturnsFirstAvailable()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, new[] { "Home", "Office", "IoT" });

        var best = svc.PickBestSsid(id, new[] { "Office", "Guest", "IoT" });
        best.Should().Be("Office", because: "Office is preferred over IoT");
    }

    [Fact]
    public void PickBestSsid_NoneInRange_ReturnsNull()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, new[] { "Home", "Office" });
        svc.PickBestSsid(id, new[] { "Stranger" }).Should().BeNull();
    }

    [Fact]
    public void PickBestSsid_NoPreferences_ReturnsNull()
    {
        var svc = new AdapterPreferencesService();
        svc.PickBestSsid(Guid.NewGuid(), new[] { "Home" }).Should().BeNull();
    }

    [Fact]
    public void PickBestSsid_CaseSensitive_NoMatch()
    {
        // SSID matching is case-sensitive (uses HashSet without OrdinalIgnoreCase)
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, new[] { "HomeNet" });
        svc.PickBestSsid(id, new[] { "homenet" })
           .Should().BeNull("SSID matching is case-sensitive");
    }

    [Fact]
    public void IsAutoReconnectEnabled_DefaultFalse_WhenNoSsidsConfigured()
    {
        // IsEnabled=true by default but both PinnedSsids and AutoConnectPriority are empty
        // → IsAutoReconnectEnabled returns false
        var svc = new AdapterPreferencesService();
        svc.IsAutoReconnectEnabled(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void IsAutoReconnectEnabled_TrueAfterAddingPreferred()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "HomeNet");
        svc.IsAutoReconnectEnabled(id).Should().BeTrue();
    }

    [Fact]
    public void SetAutoReconnect_False_ClearsListsAndDisables()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "HomeNet");
        svc.IsAutoReconnectEnabled(id).Should().BeTrue("precondition");

        svc.SetAutoReconnect(id, false);

        svc.IsAutoReconnectEnabled(id).Should().BeFalse();
        svc.GetPreferredNetworks(id).Should().BeEmpty("SetAutoReconnect(false) clears lists");
    }

    [Fact]
    public void AdapterPreferences_Record_CanBeCloned()
    {
        var p1 = new AdapterPreferences
        {
            AutoConnectPriority = new[] { "A" },
            IsEnabled           = true
        };
        var p2 = p1 with { IsEnabled = false };
        p2.IsEnabled.Should().BeFalse();
        p2.AutoConnectPriority.Should().BeEquivalentTo(new[] { "A" });
        p1.IsEnabled.Should().BeTrue("record with creates a copy, p1 is unchanged");
    }
}
