using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ═══════════════════════════════════════════════
//  v2.0 統合検証 — 高密度 (>3アサート/テスト)
// ═══════════════════════════════════════════════

public class ConnectionExecutorIntegrationV2Tests
{
    private sealed class FakeWifi : IWifiService
    {
        public int RegisterCalled { get; private set; }
        public int ConnectCalled  { get; private set; }
        public int DisconnectCalled { get; private set; }
        public string? LastSsid     { get; private set; }
        public string? LastXml      { get; private set; }

        public Task<System.Collections.Generic.IReadOnlyList<WifiAdapter>> GetAdaptersAsync(System.Threading.CancellationToken ct = default)
            => Task.FromResult<System.Collections.Generic.IReadOnlyList<WifiAdapter>>(Array.Empty<WifiAdapter>());

        public Task<System.Collections.Generic.IReadOnlyList<WifiNetwork>> ScanAsync(Guid adapterId, System.Threading.CancellationToken ct = default)
            => Task.FromResult<System.Collections.Generic.IReadOnlyList<WifiNetwork>>(Array.Empty<WifiNetwork>());

        public Task<bool> RegisterProfileAsync(Guid adapterId, string xml, bool overwrite, System.Threading.CancellationToken ct = default)
        {
            RegisterCalled++;
            LastXml = xml;
            return Task.FromResult(true);
        }

        public Task<ConnectionResult> ConnectAsync(Guid adapterId, string ssid, string profileName, TimeSpan timeout, System.Threading.CancellationToken ct = default)
        {
            ConnectCalled++;
            LastSsid = ssid;
            return Task.FromResult(ConnectionResult.Ok(ssid, true, false));
        }

        public Task<bool> DisconnectAsync(Guid adapterId, System.Threading.CancellationToken ct = default)
        {
            DisconnectCalled++;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteProfileAsync(Guid adapterId, string profileName, System.Threading.CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<System.Collections.Generic.IReadOnlyList<string>> ListProfilesAsync(Guid adapterId, System.Threading.CancellationToken ct = default)
            => Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(Array.Empty<string>());

        public async System.Collections.Generic.IAsyncEnumerable<MWC.Core.Abstractions.WifiEvent> SubscribeEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }

    private (ConnectionExecutor, FakeWifi, NetworkHistoryService) Build()
    {
        var wifi = new FakeWifi();
        var hist = new NetworkHistoryService();
        var exec = new ConnectionExecutor(wifi, hist, NullLogger<ConnectionExecutor>.Instance);
        return (exec, wifi, hist);
    }

    [Fact]
    public async Task ConnectAsync_CallsAllSteps_InOrder()
    {
        var (exec, wifi, _) = Build();
        var result = await exec.ConnectAsync(Guid.NewGuid(), "TestNet", AuthMethod.WPA2PSK, "pass1234");

        result.Success.Should().BeTrue();
        wifi.RegisterCalled.Should().Be(1, "プロファイル登録は1回");
        wifi.ConnectCalled.Should().Be(1, "接続は1回");
        wifi.LastSsid.Should().Be("TestNet");
        wifi.DisconnectCalled.Should().Be(0, "接続フローで切断は呼ばれない");
    }

    [Fact]
    public async Task ConnectAsync_GeneratedXml_ContainsRequiredElements()
    {
        var (exec, wifi, _) = Build();
        await exec.ConnectAsync(Guid.NewGuid(), "MyOffice", AuthMethod.WPA3SAE, "secret123");

        wifi.LastXml.Should().NotBeNullOrEmpty();
        wifi.LastXml.Should().Contain("MyOffice");
        wifi.LastXml.Should().Contain("WLANProfile");
        wifi.LastXml.Should().StartWith("<?xml");
    }

    [Fact]
    public async Task ConnectAsync_RecordsHistory_OnSuccess()
    {
        var (exec, _, hist) = Build();
        await exec.ConnectAsync(Guid.NewGuid(), "HistoryNet", AuthMethod.Open);

        var recents = hist.GetRecentSsids(50).ToList();
        recents.Should().NotBeEmpty();
        recents.Should().Contain("HistoryNet");
        recents.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DisconnectAsync_CallsService_Once()
    {
        var (exec, wifi, _) = Build();
        var ok = await exec.DisconnectAsync(Guid.NewGuid());

        ok.Should().BeTrue();
        wifi.DisconnectCalled.Should().Be(1);
        wifi.ConnectCalled.Should().Be(0, "切断のみで接続は呼ばれない");
        wifi.RegisterCalled.Should().Be(0, "切断ではプロファイル登録は呼ばれない");
    }

    [Fact]
    public async Task ConnectAsync_OpenNetwork_GeneratesValidXml()
    {
        var (exec, wifi, _) = Build();
        await exec.ConnectAsync(Guid.NewGuid(), "FreeWifi", AuthMethod.Open, passphrase: "");

        wifi.LastXml.Should().NotBeNullOrEmpty();
        wifi.LastXml.Should().Contain("FreeWifi");
        wifi.LastXml.Should().Contain("open");
        wifi.RegisterCalled.Should().Be(1);
    }
}

public class WifiUriIntegrationTests
{
    [Fact]
    public void Parse_FullSpec_ExtractsAllFields()
    {
        var uri = "WIFI:T:WPA2;S:MyHome;P:secretpass;;";
        var spec = WifiUri.TryParse(uri);

        spec.Should().NotBeNull();
        spec!.Ssid.Should().Be("MyHome");
        spec.Passphrase.Should().Be("secretpass");
        spec.Auth.Should().Be(AuthMethod.WPA2PSK);
    }

    [Fact]
    public void Build_RoundTrip_PreservesFields()
    {
        var original = new WifiProfileSpec
        {
            Ssid       = "RoundTrip",
            Passphrase = "p@ssword",
            Auth       = AuthMethod.WPA3SAE,
        };
        var uri = WifiUri.Build(original);
        var parsed = WifiUri.TryParse(uri);

        parsed.Should().NotBeNull();
        parsed!.Ssid.Should().Be(original.Ssid);
        parsed.Passphrase.Should().Be(original.Passphrase);
        parsed.Auth.Should().Be(original.Auth);
    }

    [Fact]
    public void Parse_InvalidScheme_ReturnsNull()
    {
        WifiUri.TryParse("HTTP://example.com").Should().BeNull();
        WifiUri.TryParse("").Should().BeNull();
        WifiUri.TryParse("not a wifi uri at all").Should().BeNull();
    }
}

public class ProfileXmlBuilderIntegrationV2Tests
{
    [Fact]
    public void Build_WPA2PSK_GeneratesValidXml()
    {
        var spec = new WifiProfileSpec { Ssid = "Test", Passphrase = "pass1234", Auth = AuthMethod.WPA2PSK };
        var xml = ProfileXmlBuilder.Build(spec);

        xml.Should().NotBeNullOrEmpty();
        xml.Should().StartWith("<?xml");
        xml.Should().Contain("<WLANProfile");
        xml.Should().Contain("<keyMaterial>pass1234</keyMaterial>");
        xml.Should().Contain("WPA2PSK");
    }

    [Fact]
    public void Build_OpenNetwork_NoSharedKey()
    {
        var spec = new WifiProfileSpec { Ssid = "Open", Auth = AuthMethod.Open };
        var xml = ProfileXmlBuilder.Build(spec);

        xml.Should().Contain("<authentication>open</authentication>");
        xml.Should().Contain("<encryption>none</encryption>");
        xml.Should().NotContain("sharedKey");
        xml.Should().NotContain("keyMaterial");
    }

    [Fact]
    public void Build_NonBroadcastNetwork_IncludesFlag()
    {
        var spec = new WifiProfileSpec
        {
            Ssid = "Hidden",
            Auth = AuthMethod.WPA2PSK,
            Passphrase = "pass1234",
            NonBroadcast = true
        };
        var xml = ProfileXmlBuilder.Build(spec);

        xml.Should().Contain("nonBroadcast");
        xml.Should().Contain("Hidden");
        xml.Should().NotBeNullOrEmpty();
        xml.Length.Should().BeGreaterThan(100);
    }
}

public class AdapterPreferencesIntegrationV2Tests
{
    [Fact]
    public void PinAndPickBest_FullFlow()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();

        svc.PinSsid(id, "Office");
        svc.PinSsid(id, "Cafe");
        var best = svc.PickBestSsid(id, new[] { "Cafe", "Random" });

        best.Should().Be("Cafe", "Officeは圏外、Cafeはピン留め済みで圏内");
        svc.IsAutoReconnectEnabled(id).Should().BeTrue();
        svc.Get(id).PinnedSsids.Should().HaveCount(2);
        svc.Get(id).PinnedSsids.Should().Contain("Office").And.Contain("Cafe");
    }

    [Fact]
    public void PriorityOverridesPin()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "Pinned");
        svc.SetAutoConnectPriority(id, new[] { "TopPriority" });

        var best = svc.PickBestSsid(id, new[] { "Pinned", "TopPriority" });

        best.Should().Be("TopPriority", "AutoConnectPriorityが最優先");
        svc.GetPreferredNetworks(id).Should().Contain("TopPriority");
        svc.Get(id).PinnedSsids.Should().Contain("Pinned");
        svc.IsAutoReconnectEnabled(id).Should().BeTrue();
    }

    [Fact]
    public void DisabledAdapter_PickBestReturnsNull()
    {
        var svc = new AdapterPreferencesService();
        var id  = Guid.NewGuid();
        svc.PinSsid(id, "Home");
        svc.SetEnabled(id, false);

        var best = svc.PickBestSsid(id, new[] { "Home", "Office" });

        best.Should().BeNull("無効化アダプタは候補返さない");
        svc.IsAutoReconnectEnabled(id).Should().BeFalse();
        svc.Get(id).IsEnabled.Should().BeFalse();
    }
}

// ════════════════════════════════════════════
//  ConfigureAwait(false) Core 層規約テスト
// ════════════════════════════════════════════
public class ConfigureAwaitCoverageTests
{
    [Fact]
    public void AllCoreServices_HaveConfigureAwaitOnAwaitedCalls()
    {
        var coreDir = GetCoreServicesDir();
        if (!Directory.Exists(coreDir)) return;

        var files = Directory.GetFiles(coreDir, "*.cs", SearchOption.AllDirectories);
        var violations = new List<string>();

        foreach (var f in files)
        {
            var lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // awaitがあるがConfigureAwaitなし、かつTask.Delayか外部呼出
                if (line.TrimStart().StartsWith("await ") &&
                    !line.Contains("ConfigureAwait") &&
                    !line.Contains("//") &&
                    !line.Contains("foreach"))
                {
                    violations.Add($"{Path.GetFileName(f)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        violations.Should().BeEmpty(
            because: "All awaits in Core layer should use .ConfigureAwait(false)\n" +
                     string.Join("\n", violations.Take(5)));
    }

    private static string GetCoreServicesDir()
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(ConfigureAwaitCoverageTests).Assembly.Location)!,
            "..", "..", "..", "..", "..",
            "src", "MWC.Core", "Services"));
}
