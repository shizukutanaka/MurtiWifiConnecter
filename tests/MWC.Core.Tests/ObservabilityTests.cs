using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  MwcLog — 構造化ログ (SSID ハッシュ化 / PII 保護)
// ══════════════════════════════════════════════════════════════
public class MwcLogTests
{
    [Fact]
    public void HashSsid_SameInput_SameHash()
    {
        var h1 = MwcLog.HashSsid("HomeWiFi");
        var h2 = MwcLog.HashSsid("HomeWiFi");
        h1.Should().Be(h2, "同一SSIDは同一ハッシュ (追跡可能)");
    }

    [Fact]
    public void HashSsid_DifferentInput_DifferentHash()
    {
        MwcLog.HashSsid("NetA").Should().NotBe(MwcLog.HashSsid("NetB"));
    }

    [Fact]
    public void HashSsid_DoesNotContainOriginal()
    {
        var ssid = "MySecretNetwork";
        var hash = MwcLog.HashSsid(ssid);
        // 元のSSIDがハッシュに含まれない (PII保護)
        hash.Should().NotContain(ssid);
        hash.Should().HaveLength(8, "FNV-1a 32bit = 8 hex chars");
    }

    [Fact]
    public void HashSsid_Empty_ReturnsPlaceholder()
    {
        MwcLog.HashSsid("").Should().Be("(empty)");
    }

    [Fact]
    public void HashSsid_IsHexFormat()
    {
        var hash = MwcLog.HashSsid("TestNetwork123");
        hash.Should().MatchRegex("^[0-9a-f]{8}$");
    }
}

// ══════════════════════════════════════════════════════════════
//  HealthCheckService — ヘルスチェック
// ══════════════════════════════════════════════════════════════
public class HealthCheckServiceTests
{
    private readonly HealthCheckService _svc = new();

    private static WifiAdapter Adapter(AdapterState state) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Test Adapter",
            Description = "Test",
            State = state
        };

    [Fact]
    public void CheckAdapters_NoAdapters_Unhealthy()
    {
        var report = _svc.CheckAdapters(Array.Empty<WifiAdapter>());
        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Checks.Should().Contain(c => c.Name == "adapters" && !c.Passed);
    }

    [Fact]
    public void CheckAdapters_ConnectedAdapter_Healthy()
    {
        var report = _svc.CheckAdapters(new[] { Adapter(AdapterState.Connected) });
        report.Status.Should().Be(HealthStatus.Healthy);
        report.Checks.Should().AllSatisfy(c => c.Passed.Should().BeTrue());
    }

    [Fact]
    public void CheckAdapters_AllNotReady_Unhealthy()
    {
        var report = _svc.CheckAdapters(new[]
        {
            Adapter(AdapterState.NotReady),
            Adapter(AdapterState.NotReady)
        });
        report.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void CheckAdapters_MixedStates_ReportsConnected()
    {
        var report = _svc.CheckAdapters(new[]
        {
            Adapter(AdapterState.Connected),
            Adapter(AdapterState.Disconnected)
        });
        report.Checks.Should().Contain(c => c.Name == "adapters.connected");
        report.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void IsLive_HealthyReport_True()
    {
        var report = _svc.CheckAdapters(new[] { Adapter(AdapterState.Connected) });
        _svc.IsLive(report).Should().BeTrue();
    }

    [Fact]
    public void IsLive_UnhealthyReport_False()
    {
        var report = _svc.CheckAdapters(Array.Empty<WifiAdapter>());
        _svc.IsLive(report).Should().BeFalse();
    }
}

// ══════════════════════════════════════════════════════════════
//  PII 非含有検証 (I5 準拠)
// ══════════════════════════════════════════════════════════════
public class PiiVerificationTests
{
    private readonly HealthCheckService _svc = new();

    [Fact]
    public void VerifyNoPii_CleanLog_Passes()
    {
        var ok = _svc.VerifyNoPii("接続成功 adapter=abc123 ssidHash=deadbeef elapsedMs=150", out var detected);
        ok.Should().BeTrue();
        detected.Should().BeEmpty();
    }

    [Fact]
    public void VerifyNoPii_ContainsIpv4_Fails()
    {
        var ok = _svc.VerifyNoPii("gateway 192.168.1.1 reachable", out var detected);
        ok.Should().BeFalse();
        detected.Should().Contain("IPv4 アドレス");
    }

    [Fact]
    public void VerifyNoPii_ContainsMac_Fails()
    {
        var ok = _svc.VerifyNoPii("bssid AA:BB:CC:DD:EE:FF detected", out var detected);
        ok.Should().BeFalse();
        detected.Should().Contain("MAC アドレス");
    }

    [Fact]
    public void VerifyNoPii_ContainsEmail_Fails()
    {
        var ok = _svc.VerifyNoPii("user admin@example.com logged in", out var detected);
        ok.Should().BeFalse();
        detected.Should().Contain("メールアドレス");
    }

    [Fact]
    public void VerifyNoPii_ContainsPhone_Fails()
    {
        var ok = _svc.VerifyNoPii("contact 03-1234-5678 office", out var detected);
        ok.Should().BeFalse();
        detected.Should().Contain("電話番号");
    }

    [Fact]
    public void VerifyNoPii_HashedSsidLog_Passes()
    {
        // MwcLog の出力形式は PII を含まない
        var ssidHash = MwcLog.HashSsid("RealNetwork");
        var logLine = $"接続試行開始 adapter=guid ssidHash={ssidHash} auth=WPA3SAE";
        _svc.VerifyNoPii(logLine, out _).Should().BeTrue();
    }

    [Fact]
    public void VerifyNoPii_MultiplePii_DetectsAll()
    {
        var ok = _svc.VerifyNoPii(
            "192.168.1.1 admin@test.com AA:BB:CC:DD:EE:FF", out var detected);
        ok.Should().BeFalse();
        detected.Should().HaveCountGreaterOrEqualTo(3);
    }
}
