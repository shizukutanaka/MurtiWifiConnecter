using System;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  DiagnosticBundleService — PII 秘匿サポートバンドル生成 (D9)
// ══════════════════════════════════════════════════════════════
public class DiagnosticBundleServiceTests
{
    private readonly DiagnosticBundleService _svc = new();
    private readonly HealthCheckService _health = new();

    // ── 秘匿ユーティリティ ──────────────────────────────────────

    [Fact]
    public void MaskSsid_KeepsFirstTwoChars()
    {
        DiagnosticBundleService.MaskSsid("MyHomeNetwork").Should().StartWith("My");
        DiagnosticBundleService.MaskSsid("MyHomeNetwork").Should().NotContain("Home");
        DiagnosticBundleService.MaskSsid("AB").Should().Be("AB*");
        DiagnosticBundleService.MaskSsid("").Should().Be("(empty)");
    }

    [Fact]
    public void MaskMac_KeepsOuiOnly()
    {
        DiagnosticBundleService.MaskMac("AA:BB:CC:11:22:33")
            .Should().Be("aa:bb:cc:**:**:**");
        DiagnosticBundleService.MaskMac("garbage")
            .Should().Be("**:**:**:**:**:**");
    }

    [Fact]
    public void Redact_RemovesIpMacEmailPhone()
    {
        var input = "client 192.168.1.50 mac aa:bb:cc:dd:ee:ff mail a@b.com tel 03-1234-5678";
        var r = DiagnosticBundleService.Redact(input);

        r.Should().NotContain("192.168.1.50");
        r.Should().Contain("x.x.x.x");
        r.Should().NotContain("dd:ee:ff");
        r.Should().NotContain("a@b.com");
        r.Should().NotContain("03-1234-5678");
    }

    [Fact]
    public void Redact_NullOrEmpty_Safe()
    {
        DiagnosticBundleService.Redact(null).Should().Be("");
        DiagnosticBundleService.Redact("").Should().Be("");
    }

    // ── バンドル生成 ────────────────────────────────────────────

    [Fact]
    public void Build_IncludesAllSections()
    {
        var adapters = new[]
        {
            new WifiAdapter
            {
                Id = Guid.NewGuid(), Name = "Intel AX211", Description = "Wi-Fi 6E",
                State = AdapterState.Connected, ConnectedSsid = "SecretHomeWiFi"
            }
        };
        var ctx = new DiagnosticContext
        {
            AppVersion = "3.11.0",
            OsDescription = "Windows 11 24H2",
            Adapters = adapters,
            Health = _health.CheckAdapters(adapters),
            Quality = new NetworkQualityResult(15, 12, 20, 0, QualityGrade.Excellent, DateTimeOffset.UtcNow),
            LastFailure = ConnectionFailure.Timeout
        };

        var md = _svc.Build(ctx);

        md.Should().Contain("# MWC Diagnostic Bundle");
        md.Should().Contain("3.11.0");
        md.Should().Contain("Intel AX211");
        md.Should().Contain("## Health");
        md.Should().Contain("## Quality Measurement");
        md.Should().Contain("Timeout");
    }

    [Fact]
    public void Build_MasksConnectedSsid()
    {
        var adapters = new[]
        {
            new WifiAdapter
            {
                Id = Guid.NewGuid(), Name = "Adapter", Description = "",
                State = AdapterState.Connected, ConnectedSsid = "SecretHomeWiFi"
            }
        };
        var md = _svc.Build(new DiagnosticContext { Adapters = adapters });

        md.Should().NotContain("SecretHomeWiFi");
        md.Should().Contain("Se");  // 先頭2文字は残る
    }

    [Fact]
    public void Build_RedactsUserNote()
    {
        var md = _svc.Build(new DiagnosticContext
        {
            UserNote = "私のIPは 10.0.0.5 でメールは me@example.com です"
        });

        md.Should().Contain("## Notes");
        md.Should().NotContain("10.0.0.5");
        md.Should().NotContain("me@example.com");
    }

    [Fact]
    public void Build_NoAdapters_StillValid()
    {
        var md = _svc.Build(new DiagnosticContext());
        md.Should().Contain("(none detected)");
    }

    [Fact]
    public void Build_OutputContainsNoRawPii()
    {
        // 生成物全体に PII が残らないことを HealthCheckService で検証
        var adapters = new[]
        {
            new WifiAdapter
            {
                Id = Guid.NewGuid(), Name = "NIC", Description = "",
                State = AdapterState.Connected, ConnectedSsid = "HomeNet"
            }
        };
        var md = _svc.Build(new DiagnosticContext
        {
            Adapters = adapters,
            UserNote = "router 192.168.0.1 bssid 11:22:33:44:55:66"
        });

        // 各行が PII を含まないこと
        foreach (var line in md.Split('\n'))
            _health.VerifyNoPii(line, out _).Should().BeTrue($"行に PII: {line}");
    }
}
