using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MWC.Core.Abstractions;
using MWC.Core.Services;

namespace MWC.Cli;

/// <summary>
/// マルチアダプターコマンド。
/// MWC の本質である「子機ごとに別ネットワークに接続」を CLI で表現する。
///
///   mwc multi connect "Wi-Fi=HomeNet" "Wi-Fi 2=GuestNet"
///   mwc multi disconnect-all
///   mwc multi status
/// </summary>
internal static class MultiAdapterCommand
{
    internal static Command Build(ServiceProvider sp)
    {
        var multi = new Command("multi", "Multi-adapter operations");

        multi.AddCommand(BuildConnect(sp));
        multi.AddCommand(BuildDisconnectAll(sp));
        multi.AddCommand(BuildStatus(sp));

        return multi;
    }

    // ── multi connect "Wi-Fi=Home" "Wi-Fi 2=Guest" ────
    private static Command BuildConnect(ServiceProvider sp)
    {
        var pairs = new Argument<string[]>("pairs",
            "Adapter=SSID pairs (e.g. \"Wi-Fi=Home\" \"Wi-Fi 2=Guest\")")
        { Arity = ArgumentArity.OneOrMore };
        var pwOpt = new Option<string?>("--password",
            "Common password for all connections (or use $env:PW)");

        var cmd = new Command("connect", "Connect each adapter to its own network");
        cmd.AddArgument(pairs); cmd.AddOption(pwOpt);

        cmd.SetHandler(async (string[] specs, string? pw) =>
        {
            try
            {
                var svc      = sp.GetRequiredService<IWifiService>();
                var executor = sp.GetRequiredService<ConnectionExecutor>();
                var ads      = await svc.GetAdaptersAsync();
                pw ??= Environment.GetEnvironmentVariable("PW") ?? "";

                var tasks = new List<Task<(string adapter, string ssid, bool ok, string? error)>>();

                foreach (var spec in specs)
                {
                    var idx = spec.IndexOf('=');
                    if (idx < 0) { Console.Error.WriteLine($"invalid: {spec}"); continue; }
                    var adName = spec[..idx].Trim();
                    var ssid   = spec[(idx + 1)..].Trim();

                    var ad = ads.FirstOrDefault(a =>
                        a.Name.Equals(adName, StringComparison.OrdinalIgnoreCase));
                    if (ad is null)
                    {
                        Console.Error.WriteLine($"adapter not found: {adName}");
                        continue;
                    }

                    tasks.Add(ConnectOneAsync(svc, executor, ad.Id, adName, ssid, pw));
                }

                // すべての spec が不正 / アダプター不一致なら、暗黙の成功 (exit 0) を避けて
                // 入力エラーとして終了する。
                if (tasks.Count == 0)
                {
                    Console.Error.WriteLine("no valid adapter=SSID pairs to connect");
                    Environment.Exit(ExitCode.InvalidInput);
                    return;
                }

                var results = await Task.WhenAll(tasks);
                Console.WriteLine();
                Console.WriteLine($"{"Adapter",-18}  {"SSID",-24}  Result");
                Console.WriteLine(new string('-', 70));
                foreach (var (adapter, ssid, ok, err) in results)
                    Console.WriteLine($"{adapter,-18}  {ssid,-24}  {(ok ? "✓ connected" : $"✗ {err}")}");

                int success = results.Count(r => r.ok);
                Console.WriteLine();
                Console.WriteLine($"{success} / {results.Length} adapters connected");
                if (success < results.Length) Environment.Exit(ExitCode.GeneralError);
            }
            catch (Exception ex) { Console.Error.WriteLine($"error: {ex.Message}"); Environment.Exit(ExitCode.GeneralError); }
        }, pairs, pwOpt);

        return cmd;
    }

    private static async Task<(string, string, bool, string?)> ConnectOneAsync(
        IWifiService svc, ConnectionExecutor executor,
        Guid adapterId, string adName, string ssid, string passphrase)
    {
        try
        {
            // スキャンで認証方式を取得
            var nets = await svc.ScanAsync(adapterId);
            var net  = nets.FirstOrDefault(n => n.Ssid == ssid);
            if (net is null) return (adName, ssid, false, "SSID not found");

            var spec = new MWC.Core.Models.WifiProfileSpec
            {
                Ssid       = ssid,
                Auth       = net.Auth,
                Passphrase = passphrase
            };
            // executor 経由で接続 (セマフォ・OTel・履歴記録を一元管理)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var res = await executor.ConnectAsync(adapterId, spec, TimeSpan.FromSeconds(20), cts.Token);

            return res.Success
                ? (adName, ssid, true,  null)
                : (adName, ssid, false, res.Failure?.ToString() ?? "failed");
        }
        catch (OperationCanceledException)
        {
            return (adName, ssid, false, "timed out");
        }
        catch (Exception ex)
        {
            return (adName, ssid, false, ex.Message);
        }
    }

    // ── multi disconnect-all ───────────────────────────
    private static Command BuildDisconnectAll(ServiceProvider sp)
    {
        var cmd = new Command("disconnect-all", "Disconnect all adapters in parallel");
        cmd.SetHandler(async () =>
        {
            try
            {
                var svc = sp.GetRequiredService<IWifiService>();
                var ads = await svc.GetAdaptersAsync();
                await Task.WhenAll(ads.Select(a => svc.DisconnectAsync(a.Id)));
                Console.WriteLine($"{ads.Count} adapter(s) disconnected");
            }
            catch (Exception ex) { Console.Error.WriteLine($"error: {ex.Message}"); Environment.Exit(ExitCode.GeneralError); }
        });
        return cmd;
    }

    // ── multi status ───────────────────────────────────
    private static Command BuildStatus(ServiceProvider sp)
    {
        var cmd = new Command("status", "Show all adapters with their current connections");
        cmd.SetHandler(async () =>
        {
            try
            {
                var svc = sp.GetRequiredService<IWifiService>();
                var ads = await svc.GetAdaptersAsync();

                var results = await Task.WhenAll(ads.Select(async a =>
                {
                    try
                    {
                        var nets = await svc.ScanAsync(a.Id);
                        return (adapter: a, conn: nets.FirstOrDefault(n => n.IsConnected));
                    }
                    catch { return (adapter: a, conn: (MWC.Core.Models.WifiNetwork?)null); }
                }));

                Console.WriteLine($"{"Adapter",-18}  {"Connected SSID",-26}  {"Signal",6}  PHY");
                Console.WriteLine(new string('-', 72));
                foreach (var (a, c) in results)
                {
                    var ssid = c?.Ssid ?? "—";
                    var sig  = c != null ? $"{c.SignalQuality}%" : "—";
                    var phy  = c?.Phy.ToShortLabel() ?? "—";
                    Console.WriteLine($"{a.Name,-18}  {ssid,-26}  {sig,6}  {phy}");
                }
                Console.WriteLine();
                Console.WriteLine($"{results.Count(r => r.conn != null)} / {results.Length} connected");
            }
            catch (Exception ex) { Console.Error.WriteLine($"error: {ex.Message}"); Environment.Exit(ExitCode.GeneralError); }
        });
        return cmd;
    }
}
