using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;

/*  実行方法:
    cd benchmarks
    dotnet run -c Release

    期待値 (Apple M1 / i7-12700 目安):
      ProfileXmlBuilder.WPA2     < 10 µs
      ProfileXmlBuilder.Wpa3Ent < 15 µs
      WifiUri.Build              < 5  µs
      WifiUri.ParseRoundTrip     < 5  µs
      Contrast.Calc100           < 200 µs
      History.Record1000         < 5  ms
      Regulatory.US6GHz          < 50 µs
*/

namespace MWC.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class ProfileXmlBuilderBenchmarks
{
    private readonly WifiProfileSpec _wpa2;
    private readonly WifiProfileSpec _wpa3Ent;

    public ProfileXmlBuilderBenchmarks()
    {
        _wpa2 = new() { Ssid = "HomeNetwork", Auth = AuthMethod.WPA2PSK, Passphrase = "pass12345" };
        _wpa3Ent = new()
        {
            Ssid        = "CorpNet",
            Auth        = AuthMethod.WPA3Enterprise,
            EapType     = EapType.PEAP_MSCHAPv2,
            Username    = "user@corp.com",
            ServerNames = new[] { "radius.corp.com" }
        };
    }

    [Benchmark(Baseline = true)]
    public string Wpa2Psk()
        => ProfileXmlBuilder.Build(_wpa2);

    [Benchmark]
    public string Wpa3Enterprise()
        => ProfileXmlBuilder.Build(_wpa3Ent);
}

[MemoryDiagnoser]
[SimpleJob]
public class WifiUriBenchmarks
{
    private readonly WifiProfileSpec _spec;
    private readonly string          _uri;

    public WifiUriBenchmarks()
    {
        _spec = new() { Ssid = "BenchNet", Auth = AuthMethod.WPA2PSK, Passphrase = "bench12345" };
        _uri  = WifiUri.Build(_spec);
    }

    [Benchmark]
    public string Build()
        => WifiUri.Build(_spec);

    [Benchmark]
    public WifiProfileSpec? Parse()
        => WifiUri.TryParse(_uri);

    [Benchmark]
    public string RoundTrip()
    {
        var built  = WifiUri.Build(_spec);
        var parsed = WifiUri.TryParse(built);
        return WifiUri.Build(parsed!);
    }
}

[MemoryDiagnoser]
[SimpleJob]
public class AccessibilityBenchmarks
{
    private readonly AccessibilityAuditService _svc = new();
    private static readonly string[] Fgs = { "#E6E8EB", "#001518", "#839496", "#ECEFF4", "#cad3f5" };
    private static readonly string[] Bgs = { "#0F1115", "#00C4CC", "#002b36", "#2E3440", "#24273a" };

    [Benchmark]
    public double CalcContrast()
        => _svc.CalcContrast("#E6E8EB", "#0F1115");

    [Benchmark]
    public IReadOnlyList<ContrastResult> Audit100Pairs()
    {
        var pairs = Enumerable.Range(0, 100)
            .Select(i => new ColorPair(Fgs[i % Fgs.Length], Bgs[i % Bgs.Length], i % 3 == 0))
            .ToList();
        return _svc.AuditThemePairs(pairs);
    }
}

[MemoryDiagnoser]
[SimpleJob]
public class NetworkHistoryBenchmarks
{
    [Benchmark]
    public void Record1000()
    {
        var svc = new NetworkHistoryService();
        for (int i = 0; i < 1000; i++)
            svc.RecordConnection($"SSID_{i % 50}", i % 3 != 0);
    }

    [Benchmark]
    public NetworkStatsSummary Stats30Days()
    {
        var svc = new NetworkHistoryService();
        for (int i = 0; i < 100; i++)
            svc.RecordConnection($"N{i % 10}", true);
        return svc.GetStats(30);
    }
}

[MemoryDiagnoser]
[SimpleJob]
public class RegulatoryBenchmarks
{
    private readonly RegulatoryDomainService _svc = new();

    [Benchmark]
    public IReadOnlyList<ChannelInfo> US6GHzChannels()
        => _svc.GetAvailable6GHzChannels("US");

    [Benchmark]
    public RegulatoryRegion Detect()
        => _svc.DetectCurrentRegion();

    [Benchmark]
    public bool IsChannelLegal()
        => _svc.IsChannelLegal(37, "JP");
}

[MemoryDiagnoser]
[SimpleJob]
public class CatImportBenchmarks
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <EAPIdentityProviderList>
          <EAPIdentityProvider>
            <SSID>eduroam</SSID>
            <AuthenticationMethods>
              <AuthenticationMethod>
                <EAPMethod><Type>25</Type></EAPMethod>
              </AuthenticationMethod>
            </AuthenticationMethods>
            <CredentialApplicability>
              <IEEE80211>
                <ServerName>radius.example.ac.jp</ServerName>
              </IEEE80211>
            </CredentialApplicability>
            <ProviderInfo>
              <DisplayName>Example University</DisplayName>
              <Domain>example.ac.jp</Domain>
            </ProviderInfo>
          </EAPIdentityProvider>
        </EAPIdentityProviderList>
        """;

    private readonly CatImportService _svc = new();

    [Benchmark]
    public IReadOnlyList<CatProfile> ParseEapConfig()
        => _svc.ParseEapConfig(SampleXml);

    [Benchmark]
    public WifiProfileSpec BuildEduroamSpec()
    {
        var profiles = _svc.ParseEapConfig(SampleXml);
        return _svc.BuildEduroamSpec(profiles[0]);
    }
}

[MemoryDiagnoser]
[SimpleJob]
public class WifiDirectModelBenchmarks
{
    [Benchmark]
    public WifiDirectDiscoveryOptions DefaultOptions()
        => WifiDirectDiscoveryOptions.Default;

    [Benchmark]
    public WifiDirectGroupOwnerResult CreateResult()
        => new(true, "DIRECT-MWC-AB12", "pass1234", "192.168.49.1");
}
