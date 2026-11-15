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
    private static readonly string[] XYZ = new[] { "X", "Y", "Z" };
    private static readonly string[] B = new[] { "B" };
    private static readonly string[] ABC = new[] { "A", "B", "C" };
    private static readonly string[] AB = new[] { "A", "B" };
    private static readonly string[] HomeOfficeIoT = new[] { "Home", "Office", "IoT" };
    private static readonly string[] OfficeGuestIoT = new[] { "Office", "Guest", "IoT" };
    private static readonly string[] HomeOffice = new[] { "Home", "Office" };
    private static readonly string[] Stranger = new[] { "Stranger" };
    private static readonly string[] Home = new[] { "Home" };
    private static readonly string[] HomeNet = new[] { "HomeNet" };
    private static readonly string[] Homenet = new[] { "homenet" };
    private static readonly string[] A = new[] { "A" };

    [Fact]
    public void NewServiceNoPreferencesReturnsEmpty()
    {
        var svc = new AdapterPreferencesService();
        // 一意なGuidなのでテスト環境での既存ファイル影響なし
        var newId = Guid.NewGuid();
        svc.GetPreferredNetworks(newId).Should().BeEmpty();
        svc.GetPreferredNetworks(newId).Should().NotBeNull();
        svc.Get(newId).PinnedSsids.Should().BeEmpty();
    }

    [Fact]
    public void AddPreferredSingleAppearsInList()
    {
        var svc = new AdapterPreferencesService();
        var id = Guid.NewGuid();
        svc.AddPreferred(id, "TestNet");
        svc.GetPreferredNetworks(id).Should().Contain("TestNet");
    }

    [Fact]
    public void AddPreferredDuplicateNotAddedTwice()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "TestNet");
        svc.AddPreferred(id, "TestNet");
        svc.GetPreferredNetworks(id).Should().HaveCount(1);
    }

    [Fact]
    public void DifferentAdaptersHaveSeparatePreferences()
    {
        var svc = new AdapterPreferencesService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        svc.AddPreferred(id1, "Home");
        svc.AddPreferred(id2, "Office");

        svc.GetPreferredNetworks(id1).Should().ContainSingle().Which.Should().Be("Home");
        svc.GetPreferredNetworks(id2).Should().ContainSingle().Which.Should().Be("Office");
        svc.GetPreferredNetworks(id1).Should().NotContain("Office");
    }

    [Fact]
    public void SetAutoConnectPriorityReplacesEntireList()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "A");
        svc.AddPreferred(id, "B");
        svc.SetAutoConnectPriority(id, XYZ);
        svc.GetPreferredNetworks(id).Should().ContainInOrder("X", "Y", "Z").And.HaveCount(3);
    }

    [Fact]
    public void RemovePreferredDropsEntry()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "A");
        svc.AddPreferred(id, "B");
        svc.RemovePreferred(id, "A");
        svc.GetPreferredNetworks(id).Should().BeEquivalentTo(B);
    }

    [Fact]
    public void MoveUpPromotesEntry()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, ABC);
        svc.MoveUp(id, "C");
        svc.GetPreferredNetworks(id).Should().ContainInOrder("A", "C", "B");
    }

    [Fact]
    public void MoveUpFirstEntryNoChange()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, AB);
        svc.MoveUp(id, "A");
        svc.GetPreferredNetworks(id)[0].Should().Be("A");
    }

    [Fact]
    public void PickBestSsidReturnsFirstAvailable()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, HomeOfficeIoT);

        var best = svc.PickBestSsid(id, OfficeGuestIoT);
        best.Should().Be("Office", because: "Office is preferred over IoT");
    }

    [Fact]
    public void PickBestSsidNoneInRangeReturnsNull()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, HomeOffice);
        svc.PickBestSsid(id, Stranger).Should().BeNull();
    }

    [Fact]
    public void PickBestSsidNoPreferencesReturnsNull()
    {
        var svc = new AdapterPreferencesService();
        svc.PickBestSsid(Guid.NewGuid(), Home).Should().BeNull();
    }

    [Fact]
    public void PickBestSsidCaseSensitiveNoMatch()
    {
        // SSID matching is case-sensitive (uses HashSet without OrdinalIgnoreCase)
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.SetAutoConnectPriority(id, HomeNet);
        svc.PickBestSsid(id, Homenet)
           .Should().BeNull("SSID matching is case-sensitive");
    }

    [Fact]
    public void IsAutoReconnectEnabledDefaultFalseWhenNoSsidsConfigured()
    {
        // IsEnabled=true by default but both PinnedSsids and AutoConnectPriority are empty
        // → IsAutoReconnectEnabled returns false
        var svc = new AdapterPreferencesService();
        svc.IsAutoReconnectEnabled(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void IsAutoReconnectEnabledTrueAfterAddingPreferred()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.AddPreferred(id, "HomeNet");
        svc.IsAutoReconnectEnabled(id).Should().BeTrue();
    }

    [Fact]
    public void SetAutoReconnectFalseClearsListsAndDisables()
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
    public void AdapterPreferencesRecordCanBeCloned()
    {
        var p1 = new AdapterPreferences
        {
            AutoConnectPriority = new[] { "A" },
            IsEnabled           = true
        };
        var p2 = p1 with { IsEnabled = false };
        p2.IsEnabled.Should().BeFalse();
        p2.AutoConnectPriority.Should().BeEquivalentTo(A);
        p1.IsEnabled.Should().BeTrue("record with creates a copy, p1 is unchanged");
    }
}
