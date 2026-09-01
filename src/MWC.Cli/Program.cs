using System;
using System.CommandLine;
using System.CommandLine.Invocation;
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
///   - 終了コードが意味を持つ → <see cref="ExitCode"/> 参照
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
        root.AddCommand(BuildEapStats(sp));
        root.AddCommand(BuildPlanChannels(sp));
        root.AddCommand(BuildVpnAdvice(sp));
        root.AddCommand(BuildPrivacy(sp));
        root.AddCommand(BuildImportCat(sp));
        root.AddCommand(BuildPasspoint(sp));
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
        sc.AddSingleton<EapAuthStatsService>();
        sc.AddSingleton<ConnectionExecutor>();
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
            try
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
                    int connectedCount = 0;
                    foreach (var a in ads)
                    {
                        var nets = await svc.ScanAsync(a.Id);
                        var conn = nets.FirstOrDefault(n => n.IsConnected);
                        if (conn is not null) connectedCount++;
                        var phy  = conn?.Phy.ToShortLabel() ?? "";
                        var ssid = conn?.Ssid ?? "(not connected)";
                        var sig  = conn != null ? $"{conn.SignalQuality}%" : "-";
                        Console.WriteLine($"{i,2}  {Trunc(a.Name,18),-18}  {a.State,-12}  {Trunc(ssid,30),-30}  {sig,6}  {phy}");
                        i++;
                    }
                    Console.WriteLine();
                    Console.WriteLine($"{connectedCount} / {ads.Count} adapters connected");
                }
                else
                {
                    Console.WriteLine($"{"GUID",-36}  {"State",-14}  Name");
                    foreach (var a in ads)
                        Console.WriteLine($"{a.Id}  {a.State,-14}  {a.Name}");
                }
            }
            catch (Exception ex) { Err(ex.Message); Environment.Exit(ExitCode.GeneralError); }
        }, json, status);
        return cmd;
    }

    // ── scan ─────────────────────────────────────────────
    private static Command BuildScan(ServiceProvider sp)
    {
        var adapter   = new Option<string?>("--adapter", "Adapter GUID or name (default: first)");
        var json      = new Option<bool>("--json");
        var advise    = new Option<bool>("--advise", "Show security advisories (warnings) per network");
        var recommend = new Option<bool>("--recommend", "Rank networks by overall recommendation score");
        var evilTwin     = new Option<bool>("--evil-twin", "Flag rogue/evil-twin APs (stateless: same-SSID security-mismatch heuristic only)");
        var interference = new Option<bool>("--interference", "Show per-channel interference analysis");
        var mesh         = new Option<bool>("--mesh", "Detect mesh network groups");
        var cmd          = new Command("scan", "Scan available networks");
        cmd.AddOption(adapter); cmd.AddOption(json); cmd.AddOption(advise);
        cmd.AddOption(recommend); cmd.AddOption(evilTwin);
        cmd.AddOption(interference); cmd.AddOption(mesh);
        cmd.SetHandler(async (string? af, bool j, bool adv, bool rec, bool et, bool ifr, bool msh) =>
        {
          try
          {
            var svc  = sp.GetRequiredService<IWifiService>();
            var oui  = sp.GetRequiredService<OuiLookupService>();
            var ad   = await Resolve(svc, af);
            if (ad is null) { Err("adapter not found"); Environment.Exit(ExitCode.InvalidInput); return; }

            Console.Error.Write("Scanning…");
            var nets = await svc.ScanAsync(ad.Id);
            Console.Error.WriteLine($" {nets.Count} networks");

            // OUI 解決
            var enriched = nets.Select(n =>
            {
                var v = n.BssEntries.Count > 0 ? oui.Lookup(n.BssEntries[0].Bssid) : null;
                return v is null ? n : n with { VendorName = v };
            }).ToList();

            // OWE Transition Mode: 同一 SSID の Open ビーコンは後方互換用のプレースホルダーであり
            // (RFC 8110)、OWE 対応クライアントは常に暗号化された OWE 側を使うべき。重複表示を防ぐ。
            enriched = new OweSelectionService().ApplyOwePreference(enriched).ToList();

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

            if (rec)
            {
                var engine = new NetworkRecommendationEngine();
                Console.WriteLine();
                Console.WriteLine($"Recommended (best first):");
                Console.WriteLine($"{"#",2} {"Score",6} {"Grade",-10} {"SSID",-32} {"Top factor"}");
                int rank = 1;
                foreach (var s in engine.Rank(enriched))
                {
                    var top = engine.Explain(s).TopFactor;
                    Console.WriteLine($"{rank++,2} {s.Total,5:F0}  {s.Grade,-10} {Trunc(s.Network.Ssid,32),-32} {top}");
                }
            }

            if (et)
            {
                // CLI は接続履歴を持たないステートレス実行のため、EvilTwinDetector の
                // 4 ヒューリスティックのうち履歴非依存の 1 つ (同一 SSID に異なる
                // セキュリティ設定が混在) のみが発火する。残り 3 つ (BSSID/ベンダー/
                // セキュリティ降格の履歴照合) はデスクトップアプリ側でのみ機能する。
                var detector = new EvilTwinDetector(oui);
                var suspects = enriched
                    .Select(n => (Network: n, Verdict: detector.Analyze(n, enriched)))
                    .Where(x => x.Verdict.IsSuspect)
                    .ToList();
                Console.WriteLine();
                Console.WriteLine("Evil Twin Check (stateless scan — flags same-SSID security mismatches;");
                Console.WriteLine("BSSID/vendor/downgrade history checks require the desktop app):");
                if (suspects.Count == 0)
                {
                    Console.WriteLine("  No suspicious APs detected.");
                }
                else
                {
                    Console.WriteLine($"  {"SSID",-32} {"Risk",-12} Reasons");
                    foreach (var (n, v) in suspects)
                        Console.WriteLine($"  {Trunc(n.Ssid,32),-32} {v.Risk,-12} {string.Join("; ", v.Reasons)}");
                }
            }

            if (ifr)
            {
                var analyzer = new InterferenceAnalyzer();
                Console.WriteLine();
                Console.WriteLine($"{"SSID",-32} {"Ch",3} {"Band",4} {"Level",-10} {"Score",5}  Factors");
                foreach (var n in enriched.OrderByDescending(x => x.SignalQuality))
                {
                    var r = analyzer.Analyze(n, enriched);
                    var factor = r.Factors.Count > 0
                        ? CliHelpers.InterferenceFactorLabel(r.Factors[0])
                        : CliHelpers.InterferenceRecommendationLabel(r.Recommendation);
                    Console.WriteLine($"{Trunc(n.Ssid,32),-32} {n.Channel,3} {BandLabel(n.Band),4} {r.Level,-10} {r.Score,5}  {Trunc(factor, 50)}");
                }
            }

            if (msh)
            {
                var detector = new MeshNetworkDetector(oui);
                var groups = detector.Detect(enriched);
                Console.WriteLine();
                if (groups.Count == 0)
                {
                    Console.WriteLine("Mesh Check: no mesh networks detected.");
                }
                else
                {
                    Console.WriteLine($"{"SSID",-32} {"Nodes",5} {"Bands",-14} {"FT",4} {"Confidence"}");
                    foreach (var g in groups)
                    {
                        var bands = string.Join("+", g.BandCoverage.Select(BandLabel));
                        Console.WriteLine($"{Trunc(g.Ssid,32),-32} {g.NodeCount,5} {bands,-14} {(g.HasFastTransition ? "Yes" : "No"),4} {g.Confidence}");
                    }
                }
            }
          }
          catch (Exception ex) { Err(ex.Message); Environment.Exit(ExitCode.GeneralError); }
        }, adapter, json, advise, recommend, evilTwin, interference, mesh);
        return cmd;
    }

    // ── connect ──────────────────────────────────────────
    private static Command BuildConnect(ServiceProvider sp)
    {
        var ssid    = new Argument<string>("ssid");
        var pw      = new Option<string?>(new[]{"-p","--password"},
            "Passphrase (PSK/WEP) or EAP password (Enterprise). " +
            "Omit to read from the MWC_PASSWORD environment variable instead " +
            "(avoids exposing the secret in the process command line / ps output).");
        var auth    = new Option<AuthMethod>("--auth", () => AuthMethod.WPA2PSK);
        var adapter = new Option<string?>("--adapter");
        var timeout = new Option<int>("--timeout", () => 30);
        var hidden  = new Option<bool>("--hidden");
        // ── 802.1X Enterprise (PEAP/EAP-TLS/EAP-TTLS) ──
        var eapType  = new Option<EapType?>("--eap-type",
            "EAP method for Enterprise auth: PEAP_MSCHAPv2 | EAP_TLS | EAP_TTLS");
        eapType.AddCompletions("PEAP_MSCHAPv2", "EAP_TLS", "EAP_TTLS");
        var username = new Option<string?>("--username", "EAP username (PEAP/EAP-TTLS)");
        var domain   = new Option<string?>("--domain", "Authentication domain (optional)");
        var serverName = new Option<string[]>("--server-name",
            "RADIUS server FQDN to validate the server certificate against (repeatable)")
        { AllowMultipleArgumentsPerToken = true };
        var trustedCa = new Option<string[]>("--trusted-root-ca",
            "Trusted root CA certificate thumbprint (SHA-1 hex) to pin for RADIUS server " +
            "validation; prevents accepting a rogue server signed by a different CA (repeatable)")
        { AllowMultipleArgumentsPerToken = true };

        var cmd     = new Command("connect",
            "Connect to a network. For Enterprise: --auth WPA2Enterprise --eap-type PEAP_MSCHAPv2 " +
            "--username u@univ.ac.jp -p PASS --server-name radius.univ.ac.jp");
        cmd.AddArgument(ssid); cmd.AddOption(pw); cmd.AddOption(auth);
        cmd.AddOption(adapter); cmd.AddOption(timeout); cmd.AddOption(hidden);
        cmd.AddOption(eapType); cmd.AddOption(username); cmd.AddOption(domain);
        cmd.AddOption(serverName); cmd.AddOption(trustedCa);

        // オプション数が System.CommandLine の SetHandler ジェネリック上限 (8) を超えるため、
        // InvocationContext から個別に値を取得する束縛方式を使う。
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var s  = ctx.ParseResult.GetValueForArgument(ssid);
            // -p を省略した場合は MWC_PASSWORD 環境変数から取得する。argv に平文パスワードを
            // 置くと ps / /proc から他ユーザーに見えるため、秘密情報の露出を避ける経路を用意する
            // (MultiAdapterCommand の $env:PW と同じ思想。CLAUDE.md のセキュリティ重視に沿う)。
            var p  = ctx.ParseResult.GetValueForOption(pw)
                     ?? Environment.GetEnvironmentVariable("MWC_PASSWORD");
            var a  = ctx.ParseResult.GetValueForOption(auth);
            var af = ctx.ParseResult.GetValueForOption(adapter);
            var to = ctx.ParseResult.GetValueForOption(timeout);
            var h  = ctx.ParseResult.GetValueForOption(hidden);
            var eap         = ctx.ParseResult.GetValueForOption(eapType);
            var user        = ctx.ParseResult.GetValueForOption(username);
            var dom         = ctx.ParseResult.GetValueForOption(domain);
            var serverNames = ctx.ParseResult.GetValueForOption(serverName) ?? Array.Empty<string>();
            var trustedCas  = ctx.ParseResult.GetValueForOption(trustedCa) ?? Array.Empty<string>();

            try
            {
                if (to <= 0) { Err("--timeout must be a positive number of seconds"); Environment.Exit(ExitCode.InvalidInput); return; }

                // SSID を非 null に確定させてから spec を組む。GetValueForArgument は
                // T? を返すため、そのまま required な WifiProfileSpec.Ssid へ代入すると
                // CS8601 になり、TreatWarningsAsErrors=true の本ビルドでは**エラー**になる。
                // 空 SSID は ProfileXmlBuilder でも弾かれるが、ここで弾いた方が
                // メッセージが分かりやすい。
                if (string.IsNullOrEmpty(s))
                {
                    Err("SSID is required");
                    Environment.Exit(ExitCode.InvalidInput);
                    return;
                }

                var svc      = sp.GetRequiredService<IWifiService>();
                var executor = sp.GetRequiredService<ConnectionExecutor>();
                var ad       = await Resolve(svc, af);
                if (ad is null) { Err("adapter not found"); Environment.Exit(ExitCode.InvalidInput); return; }

                // 認証方式で spec を分岐。Enterprise では -p を EAP パスワードとして流用する。
                bool isEnterprise = a is AuthMethod.WPA2Enterprise
                    or AuthMethod.WPA3Enterprise or AuthMethod.WPA3Enterprise192;

                // 落とし穴防止: Enterprise 専用オプションを指定したのに --auth が Enterprise でない場合、
                // 黙って PSK 接続に流れ (EAP パスワードを PSK パスフレーズとして使い、username/eap-type を
                // 無視して) 「パスフレーズ誤り」の紛らわしい失敗になる。接続前に明示エラーで弾く。
                bool hasEnterpriseOpts = eap is not null || !string.IsNullOrEmpty(user)
                    || !string.IsNullOrEmpty(dom) || serverNames.Length > 0 || trustedCas.Length > 0;
                if (hasEnterpriseOpts && !isEnterprise)
                {
                    Err($"--eap-type/--username/--domain/--server-name/--trusted-root-ca require an " +
                        $"Enterprise --auth (WPA2Enterprise/WPA3Enterprise/WPA3Enterprise192); got --auth {a}");
                    Environment.Exit(ExitCode.InvalidInput);
                    return;
                }

                var spec = isEnterprise
                    ? new WifiProfileSpec
                    {
                        Ssid = s, Auth = a, NonBroadcast = h,
                        EapType = eap, Username = user, Password = p,
                        Domain = dom, ServerNames = serverNames,
                        TrustedRootCaThumbprints = trustedCas,
                    }
                    : new WifiProfileSpec { Ssid = s, Auth = a, Passphrase = p, NonBroadcast = h };

                // spec を先に検証: 不正な場合は接続前に分かりやすいエラーを出す。
                // executor 内でも同じ Build を呼ぶが、エラーが ConnectionResult.OsError に吸収されるため
                // ここで早期エラーを返す。ValidateEnterprise が EAP type 必須・PEAP/TTLS の
                // username+password 必須のエラー文言を提供する。
                try { ProfileXmlBuilder.Build(spec); }
                catch (Exception ex) { Err($"profile: {ex.Message}"); Environment.Exit(ExitCode.InvalidInput); return; }

                // executor 経由で接続 (セマフォ・OTel・履歴記録を一元管理)
                ConnectionResult res;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(to + 5));
                try { res = await executor.ConnectAsync(ad.Id, spec, TimeSpan.FromSeconds(to), cts.Token); }
                catch (OperationCanceledException) { Err("connection timed out"); Environment.Exit(ExitCode.ConnectionFailed); return; }

                if (res.Success)
                {
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        ssid       = res.ConnectedSsid,
                        internet   = res.HasInternet,
                        captive    = res.BehindCaptivePortal
                    }));
                    Environment.Exit(ExitCode.Success);
                }
                else
                {
                    var advice = TroubleshootingHelper.GetAdvice(
                        res.Failure ?? ConnectionFailure.Unknown, a);
                    Err($"failed: {res.Failure} — {advice.Reason}");
                    foreach (var step in advice.Steps)
                        Console.Error.WriteLine($"  • {step}");
                    Environment.Exit(ExitCode.ConnectionFailed);
                }
            }
            catch (Exception ex) { Err($"connect failed: {ex.Message}"); Environment.Exit(ExitCode.GeneralError); }
        });
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
            try
            {
                var svc = sp.GetRequiredService<IWifiService>();
                var ad  = await Resolve(svc, af);
                if (ad is null) { Err("adapter not found"); Environment.Exit(ExitCode.InvalidInput); return; }
                Console.WriteLine(await svc.DisconnectAsync(ad.Id) ? "disconnected" : "no-op");
            }
            catch (Exception ex) { Err(ex.Message); Environment.Exit(ExitCode.GeneralError); }
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
            try
            {
                var svc = sp.GetRequiredService<IWifiService>();
                var ad  = await Resolve(svc, af);
                if (ad is null) { Err("adapter not found"); Environment.Exit(ExitCode.InvalidInput); return; }
                foreach (var p in await svc.ListProfilesAsync(ad.Id)) Console.WriteLine(p);
            }
            catch (Exception ex) { Err(ex.Message); Environment.Exit(ExitCode.GeneralError); }
        }, adapter);

        var delArg  = new Argument<string>("name");
        var delCmd  = new Command("delete", "Delete a profile");
        delCmd.AddArgument(delArg); delCmd.AddOption(adapter);
        delCmd.SetHandler(async (string n, string? af) =>
        {
            try
            {
                var svc = sp.GetRequiredService<IWifiService>();
                var ad  = await Resolve(svc, af);
                if (ad is null) { Err("adapter not found"); Environment.Exit(ExitCode.InvalidInput); return; }
                Console.WriteLine(await svc.DeleteProfileAsync(ad.Id, n) ? "deleted" : "not found");
            }
            catch (Exception ex) { Err(ex.Message); Environment.Exit(ExitCode.GeneralError); }
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
        {
            try { Console.WriteLine(WifiUri.Build(new(){ Ssid=s, Auth=a, Passphrase=p, NonBroadcast=h })); }
            catch (Exception ex) { Err(ex.Message); Environment.Exit(ExitCode.InvalidInput); }
        }, ssid, pw, auth, hid);
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
            if (p is null) { Err("invalid URI"); Environment.Exit(ExitCode.InvalidInput); return; }
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
            try
            {
                var svc = sp.GetRequiredService<IWifiService>();
                var oui = sp.GetRequiredService<OuiLookupService>();
                var ad  = await Resolve(svc, af);
                if (ad is null) { Err("adapter not found"); Environment.Exit(ExitCode.InvalidInput); return; }

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

                switch (ext)
                {
                    case "json": ExportService.ToJson(enriched, path); break;
                    case "txt":  ExportService.ToText(enriched, path); break;
                    default:     ExportService.ToCsv (enriched, path); break;
                }
                Console.WriteLine(path);
            }
            catch (Exception ex) { Err($"export failed: {ex.Message}"); Environment.Exit(ExitCode.GeneralError); }
        }, adapter, format, output);
        return cmd;
    }

    private static void Print(object obj)   => CliHelpers.Print(obj);
    private static void Err(string msg)     => CliHelpers.Err(msg);
    private static string Trunc(string s, int n) => CliHelpers.Trunc(s, n);
    private static string BandLabel(WifiBand b)  => CliHelpers.BandLabel(b);
}

/// <summary>
/// mwc CLI 終了コード規約。スクリプトから参照可能な定数。
/// </summary>
/// <remarks>
/// 0  Success          — 正常終了
/// 1  GeneralError     — 予期しない実行時エラー
/// 2  InvalidInput     — 不正な引数 / アダプター・ネットワーク未発見
/// 3  (予約)
/// 4  ProfileError     — プロファイル登録失敗
/// 5  ConnectionFailed — 接続失敗 / タイムアウト / 圏外
/// </remarks>
public static class ExitCode
{
    public const int Success          = 0;
    public const int GeneralError     = 1;
    public const int InvalidInput     = 2;
    public const int ProfileError     = 4;
    public const int ConnectionFailed = 5;
}
