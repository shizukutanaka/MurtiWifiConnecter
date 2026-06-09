using System;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;
using MWC.Platform.Windows;

namespace MWC.Cli;

/// <summary>
/// mwc CLI — Apple "consistent CLI experience" 原則:
///   - 短い動詞コマンド (list/scan/connect/qr/export/quality)
///   - --json フラグで全コマンドが JSON 出力
///   - 終了コードが意味を持つ (0=成功 1=汎用エラー 2=引数不正 5=接続失敗)
///   - stderr = ログ/進捗 / stdout = データ (パイプ安全)
/// </summary>
public static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        // グローバル例外ハンドラ
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Console.Error.WriteLine($"FATAL: {ex.Message}");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine($"Background task error: {e.Exception.Message}");
            e.SetObserved();
        };

        var sp   = BuildServices();
        var root = new RootCommand("mwc — Multi WiFi Connector");

        root.AddCommand(BuildList(sp));
        root.AddCommand(MultiAdapterCommand.Build(sp));
        root.AddCommand(BuildScan(sp));
        root.AddCommand(BuildConnect(sp));
        root.AddCommand(BuildDisconnect(sp));
        root.AddCommand(BuildProfile(sp));
        root.AddCommand(BuildQr());
        root.AddCommand(BuildQrParse());
        root.AddCommand(BuildExport(sp));
        root.AddCommand(BuildQuality(sp));
        root.AddCommand(BuildHistory(sp));
        root.AddCommand(AdapterCommand.Build(sp));

        return await root.InvokeAsync(args);
    }

    // ── DI ───────────────────────────────────────────────
    private static ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging(b => b
            .AddSimpleConsole(o => { o.SingleLine = true; o.IncludeScopes = false; })
            .SetMinimumLevel(LogLevel.Warning));
        sc.AddSingleton<IConnectivityChecker, HttpConnectivityChecker>();
        sc.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        sc.AddSingleton<IWifiService, WindowsWifiService>();
        sc.AddSingleton<NetworkHistoryService>();
        sc.AddSingleton<NetworkQualityService>();
        sc.AddSingleton<OuiLookupService>();
        sc.AddSingleton<AdapterPreferencesService>();
        return sc.BuildServiceProvider();
    }

    // ── list ─────────────────────────────────────────────
    private static Command BuildList(ServiceProvider sp)
    {
        var json   = new Option<bool>("--json", "Output JSON");
        var status = new Option<bool>("--status", "Show connection status for each adapter");
        var cmd    = new Command("list", "List wireless adapters");
        cmd.AddOption(json); cmd.AddOption(status);

        cmd.SetHandler(async (bool j, bool s) =>
        {
            var svc = sp.GetRequiredService<IWifiService>();
            var ads = await svc.GetAdaptersAsync();

            if (j)
            {
                if (s)
                {
                    var rich = new System.Collections.Generic.List<object>();
                    foreach (var a in ads)
                    {
                        var nets = await svc.ScanAsync(a.Id);
                        var conn = nets.FirstOrDefault(n => n.IsConnected);
                        rich.Add(new
                        {
                            id    = a.Id,
                            name  = a.Name,
                            state = a.State.ToString(),
                            connected = conn?.Ssid,
                            signal    = conn?.SignalQuality ?? 0,
                            phy       = conn?.Phy.ToShortLabel(),
                            band      = conn?.Band.ToString()
                        });
                    }
                    Print(rich);
                }
                else Print(ads);
                return;
            }

            if (s)
            {
                Console.WriteLine($"{"#",2}  {"Name",-18}  {"State",-12}  {"Connected SSID",-30}  {"Signal",6}  PHY");
                Console.WriteLine(new string('-', 90));
                int i = 1;
                foreach (var a in ads)
                {
                    var nets = await svc.ScanAsync(a.Id);
                    var conn = nets.FirstOrDefault(n => n.IsConnected);
                    var phy  = conn?.Phy.ToShortLabel() ?? "";
                    var ssid = conn?.Ssid ?? "(not connected)";
                    var sig  = conn != null ? $"{conn.SignalQuality}%" : "-";
                    Console.WriteLine($"{i,2}  {Trunc(a.Name,18),-18}  {a.State,-12}  {Trunc(ssid,30),-30}  {sig,6}  {phy}");
                    i++;
                }
                Console.WriteLine();
                int connectedCount = 0;
                foreach (var a in ads) if ((await svc.ScanAsync(a.Id)).Any(n => n.IsConnected)) connectedCount++;
                Console.WriteLine($"{connectedCount} / {ads.Count} adapters connected");
            }
            else
            {
                Console.WriteLine($"{"GUID",-36}  {"State",-14}  Name");
                foreach (var a in ads)
                    Console.WriteLine($"{a.Id}  {a.State,-14}  {a.Name}");
            }
        }, json, status);
        return cmd;
    }

    // ── scan ─────────────────────────────────────────────
    private static Command BuildScan(ServiceProvider sp)
    {
        var adapter = new Option<string?>("--adapter", "Adapter GUID or name (default: first)");
        var json    = new Option<bool>("--json");
        var advise  = new Option<bool>("--advise", "Show security advisories (warnings) per network");
        var cmd     = new Command("scan", "Scan available networks");
        cmd.AddOption(adapter); cmd.AddOption(json); cmd.AddOption(advise);
        cmd.SetHandler(async (string? af, bool j, bool adv) =>
        {
            var svc  = sp.GetRequiredService<IWifiService>();
            var oui  = sp.GetRequiredService<OuiLookupService>();
            var ad   = await Resolve(svc, af);
            if (ad is null) { Err("adapter not found"); return; }

            Console.Error.Write("Scanning…");
            var nets = await svc.ScanAsync(ad.Id);
            Console.Error.WriteLine($" {nets.Count} networks");

            // OUI 解決
            var enriched = nets.Select(n =>
            {
                var v = n.BssEntries.Count > 0 ? oui.Lookup(n.BssEntries[0].Bssid) : null;
                return v is null ? n : n with { VendorName = v };
            }).ToList();

            if (j) { Print(enriched); return; }

            Console.WriteLine($"{"SSID",-32} {"Auth",-14} {"Band",4} {"PHY",-8} {"Signal",6} {"Vendor"}");
            foreach (var n in enriched)
                Console.WriteLine($"{Trunc(n.Ssid,32),-32} {n.Auth,-14} {BandLabel(n.Band),4} " +
                    $"{n.Phy.ToShortLabel(),-8} {n.SignalQuality,5}%  {n.VendorName}");

            if (adv)
            {
                var sec = new SecurityAdvisoryService();
                foreach (var n in enriched)
                {
                    var warns = sec.Analyze(n)
                        .Where(a => a.Severity is AdvisorySeverity.Warning or AdvisorySeverity.Critical)
                        .ToList();
                    if (warns.Count == 0) continue;
                    Console.WriteLine();
                    Console.WriteLine($"! {n.Ssid}");
                    foreach (var a in warns)
                        Console.WriteLine($"    [{a.Severity}] {a.Code}: {a.Title}");
                }
            }
        }, adapter, json, advise);
        return cmd;
    }

    // ── connect ──────────────────────────────────────────
    private static Command BuildConnect(ServiceProvider sp)
    {
        var ssid    = new Argument<string>("ssid");
        var pw      = new Option<string?>(new[]{"-p","--password"});
        var auth    = new Option<AuthMethod>("--auth", () => AuthMethod.WPA2PSK);
        var adapter = new Option<string?>("--adapter");
        var timeout = new Option<int>("--timeout", () => 30);
        var hidden  = new Option<bool>("--hidden");
        var cmd     = new Command("connect", "Connect to a network");
        cmd.AddArgument(ssid); cmd.AddOption(pw); cmd.AddOption(auth);
        cmd.AddOption(adapter); cmd.AddOption(timeout); cmd.AddOption(hidden);

        cmd.SetHandler(async (string s, string? p, AuthMethod a, string? af, int to, bool h) =>
        {
            var svc  = sp.GetRequiredService<IWifiService>();
            var hist = sp.GetRequiredService<NetworkHistoryService>();
            var ad   = await Resolve(svc, af);
            if (ad is null) { Err("adapter not found"); Environment.Exit(2); return; }

            string xml;
            try { xml = ProfileXmlBuilder.Build(new(){ Ssid=s, Auth=a, Passphrase=p, NonBroadcast=h }); }
            catch (Exception ex) { Err($"profile: {ex.Message}"); Environment.Exit(2); return; }

            if (!await svc.RegisterProfileAsync(ad.Id, xml, true))
                { Err("profile registration failed"); Environment.Exit(4); return; }

            ConnectionResult res;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(to + 5));
            try { res = await svc.ConnectAsync(ad.Id, s, s, TimeSpan.FromSeconds(to), cts.Token); }
            catch (OperationCanceledException) { Err("connection timed out"); Environment.Exit(5); return; }

            hist.RecordConnection(s, res.Success);

            if (res.Success)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    ssid       = res.ConnectedSsid,
                    internet   = res.HasInternet,
                    captive    = res.BehindCaptivePortal
                }));
                Environment.Exit(0);
            }
            else
            {
                Err($"failed: {res.Failure}");
                Environment.Exit(5);
            }
        }, ssid, pw, auth, adapter, timeout, hidden);
        return cmd;
    }

    // ── disconnect ───────────────────────────────────────
    private static Command BuildDisconnect(ServiceProvider sp)
    {
        var adapter = new Option<string?>("--adapter");
        var cmd     = new Command("disconnect", "Disconnect current network");
        cmd.AddOption(adapter);
        cmd.SetHandler(async (string? af) =>
        {
            var svc = sp.GetRequiredService<IWifiService>();
            var ad  = await Resolve(svc, af);
            if (ad is null) { Err("adapter not found"); return; }
            Console.WriteLine(await svc.DisconnectAsync(ad.Id) ? "disconnected" : "no-op");
        }, adapter);
        return cmd;
    }

    // ── profile ──────────────────────────────────────────
    private static Command BuildProfile(ServiceProvider sp)
    {
        var profile = new Command("profile", "Profile management");
        var adapter = new Option<string?>("--adapter");

        var listCmd = new Command("list", "List saved profiles");
        listCmd.AddOption(adapter);
        listCmd.SetHandler(async (string? af) =>
        {
            var svc = sp.GetRequiredService<IWifiService>();
            var ad  = await Resolve(svc, af);
            if (ad is null) return;
            foreach (var p in await svc.ListProfilesAsync(ad.Id)) Console.WriteLine(p);
        }, adapter);

        var delArg  = new Argument<string>("name");
        var delCmd  = new Command("delete", "Delete a profile");
        delCmd.AddArgument(delArg); delCmd.AddOption(adapter);
        delCmd.SetHandler(async (string n, string? af) =>
        {
            var svc = sp.GetRequiredService<IWifiService>();
            var ad  = await Resolve(svc, af);
            if (ad is null) return;
            Console.WriteLine(await svc.DeleteProfileAsync(ad.Id, n) ? "deleted" : "not found");
        }, delArg, adapter);

        profile.AddCommand(listCmd); profile.AddCommand(delCmd);
        return profile;
    }

    // ── qr ───────────────────────────────────────────────
    private static Command BuildQr()
    {
        var ssid = new Argument<string>("ssid");
        var pw   = new Option<string?>(new[]{"-p","--password"});
        var auth = new Option<AuthMethod>("--auth", () => AuthMethod.WPA2PSK);
        var hid  = new Option<bool>("--hidden");
        var cmd  = new Command("qr", "Generate WIFI: URI for QR code");
        cmd.AddArgument(ssid); cmd.AddOption(pw); cmd.AddOption(auth); cmd.AddOption(hid);
        cmd.SetHandler((string s, string? p, AuthMethod a, bool h) =>
            Console.WriteLine(WifiUri.Build(new(){ Ssid=s, Auth=a, Passphrase=p, NonBroadcast=h })),
            ssid, pw, auth, hid);
        return cmd;
    }

    // ── qr-parse ─────────────────────────────────────────
    private static Command BuildQrParse()
    {
        var uri = new Argument<string>("uri");
        var cmd = new Command("qr-parse", "Parse a WIFI: URI");
        cmd.AddArgument(uri);
        cmd.SetHandler((string u) =>
        {
            var p = WifiUri.Parse(u);
            if (p is null) { Err("invalid URI"); Environment.Exit(2); return; }
            Print(new { ssid=p.Ssid, auth=p.Auth.ToString(), password=p.Passphrase, hidden=p.NonBroadcast });
        }, uri);
        return cmd;
    }

    // ── export ───────────────────────────────────────────
    private static Command BuildExport(ServiceProvider sp)
    {
        var adapter = new Option<string?>("--adapter");
        var format  = new Option<string>("--format", () => "csv",
            "Output format: csv | json | txt");
        format.AddCompletions("csv", "json", "txt");
        var output  = new Option<string>(
            "--output", () => $"mwc-scan-{DateTime.Now:yyyyMMdd-HHmmss}",
            "Output file basename (extension added automatically)");

        var cmd = new Command("export", "Export scan results to file");
        cmd.AddOption(adapter); cmd.AddOption(format); cmd.AddOption(output);

        cmd.SetHandler(async (string? af, string fmt, string outBase) =>
        {
            var svc = sp.GetRequiredService<IWifiService>();
            var oui = sp.GetRequiredService<OuiLookupService>();
            var ad  = await Resolve(svc, af);
            if (ad is null) { Err("adapter not found"); Environment.Exit(2); return; }

            Console.Error.Write($"Scanning {ad.Name}…");
            var nets = await svc.ScanAsync(ad.Id);
            Console.Error.WriteLine($" {nets.Count} networks");

            // OUI解決
            var enriched = nets.Select(n =>
            {
                var v = n.BssEntries.Count > 0 ? oui.Lookup(n.BssEntries[0].Bssid) : null;
                return v is null ? n : n with { VendorName = v };
            }).ToList();

            var ext  = fmt.ToLowerInvariant() switch { "json"=>"json","txt"=>"txt",_=>"csv" };
            var path = $"{outBase}.{ext}";

            try
            {
                switch (ext)
                {
                    case "json": ExportService.ToJson(enriched, path); break;
                    case "txt":  ExportService.ToText(enriched, path); break;
                    default:     ExportService.ToCsv (enriched, path); break;
                }
                Console.WriteLine(path);
            }
            catch (Exception ex) { Err($"export failed: {ex.Message}"); Environment.Exit(1); }
        }, adapter, format, output);
        return cmd;
    }

    private static void Print(object obj)   => CliHelpers.Print(obj);
    private static void Err(string msg)     => CliHelpers.Err(msg);
    private static string Trunc(string s, int n) => CliHelpers.Trunc(s, n);
    private static string BandLabel(WifiBand b)  => CliHelpers.BandLabel(b);
}
