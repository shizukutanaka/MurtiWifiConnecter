using System;
using System.CommandLine;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MWC.Core.Services;

namespace MWC.Cli;

// quality / history コマンド (Program.cs から分離)
public static partial class Program
{
// ── quality ──────────────────────────────────────────
    private static Command BuildQuality(ServiceProvider sp)
    {
        var host    = new Option<string>("--host", () => "8.8.8.8", "Ping target");
        var samples = new Option<int>("--samples", () => 5, "Ping count");
        var json    = new Option<bool>("--json");
        var bloat   = new Option<bool>("--bufferbloat", "Also measure working latency (RPM + bufferbloat grade) under download load");
        var loadUrl = new Option<string>("--load-url",
            () => "https://speed.cloudflare.com/__down?bytes=104857600",
            "Download URL used to generate load for --bufferbloat");
        var cmd     = new Command("quality", "Measure network quality (latency + packet loss)");
        cmd.AddOption(host); cmd.AddOption(samples); cmd.AddOption(json);
        cmd.AddOption(bloat); cmd.AddOption(loadUrl);

        cmd.SetHandler(async (string h, int s, bool j, bool bb, string url) =>
        {
            try
            {
                var svc = sp.GetRequiredService<NetworkQualityService>();

                if (bb)
                {
                    Console.Error.Write($"Measuring responsiveness to {h} under load…");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    var rr = await svc.MeasureResponsivenessAsync(
                        h, ct => GenerateLoadAsync(url, ct), s, cts.Token);
                    Console.Error.WriteLine();
                    if (j)
                    {
                        Print(new {
                            idle_latency_ms     = rr.IdleLatencyMs,
                            working_latency_ms  = rr.WorkingLatencyMs,
                            latency_increase_ms = rr.LatencyIncreaseMs,
                            rpm                 = rr.Rpm,
                            bufferbloat_grade   = rr.Grade.ToString()
                        });
                        return;
                    }
                    Console.WriteLine($"Idle RTT:        {rr.IdleLatencyMs} ms");
                    Console.WriteLine($"Working RTT:     {rr.WorkingLatencyMs} ms (+{rr.LatencyIncreaseMs} ms under load)");
                    Console.WriteLine($"Responsiveness:  {rr.RpmLabel}");
                    Console.WriteLine($"Bufferbloat:     {rr.GradeLabel}");
                    return;
                }

                Console.Error.Write($"Measuring quality to {h} ({s} pings)…");
                var r = await svc.MeasureAsync(h, s);
                Console.Error.WriteLine();
                if (j)
                {
                    Print(new {
                        grade        = r.GradeLabel,
                        latency_avg  = r.LatencyAvgMs,
                        latency_min  = r.LatencyMinMs,
                        latency_max  = r.LatencyMaxMs,
                        packet_loss  = r.PacketLossPct
                    });
                    return;
                }
                Console.WriteLine($"Grade:        {r.GradeLabel}");
                Console.WriteLine($"RTT (avg):    {r.LatencyLabel}");
                Console.WriteLine($"RTT min/max:  {r.LatencyMinMs} ms / {r.LatencyMaxMs} ms");
                Console.WriteLine($"Packet loss:  {r.LossLabel}");
            }
            catch (OperationCanceledException) { Console.Error.WriteLine("Measurement cancelled."); Environment.Exit(1); }
            catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); Environment.Exit(1); }
        }, host, samples, json, bloat, loadUrl);
        return cmd;
    }

    /// <summary>--bufferbloat 用の負荷生成: 指定 URL を並列ダウンロードし続け、ct で停止。</summary>
    private static async Task GenerateLoadAsync(string url, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var workers = Enumerable.Range(0, 4).Select(async _ =>
        {
            var buf = new byte[65536];
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var resp = await http.GetAsync(
                        url, HttpCompletionOption.ResponseHeadersRead, ct);
                    using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    while (!ct.IsCancellationRequested && await stream.ReadAsync(buf, ct) > 0) { }
                }
                catch (OperationCanceledException) { break; }
                catch { /* 一時的失敗はキャンセルまで再試行 */ }
            }
        });
        try { await Task.WhenAll(workers); }
        catch (OperationCanceledException) { /* 正常停止 */ }
    }

// ── history ──────────────────────────────────────────
    private static Command BuildHistory(ServiceProvider sp)
    {
        var limit = new Option<int>("--limit", () => 10, "Max entries");
        var json  = new Option<bool>("--json");
        var clear = new Option<bool>("--clear", "Clear all history");
        var cmd   = new Command("history", "Show connection history");
        cmd.AddOption(limit); cmd.AddOption(json); cmd.AddOption(clear);

        cmd.SetHandler((int lim, bool j, bool clr) =>
        {
            var svc = sp.GetRequiredService<NetworkHistoryService>();
            if (clr) { svc.ClearAll(); Console.WriteLine("cleared"); return; }

            var entries = svc.GetRecent(lim);
            if (j) { Print(entries); return; }

            Console.WriteLine($"{"SSID",-32} {"Success",7} {"Fail",5}  {"Last Connected"}");
            foreach (var e in entries)
                Console.WriteLine($"{Trunc(e.Ssid,32),-32} {e.ConnectCount,7} {e.FailCount,5}  {e.LastConnectedLabel}");
        }, limit, json, clear);
        return cmd;
    }

    // ── helpers ──────────────────────────────────────────
    private static async Task<WifiAdapter?> Resolve(IWifiService svc, string? filter)
    {
        var all = await svc.GetAdaptersAsync();
        if (string.IsNullOrEmpty(filter)) return all.FirstOrDefault();
        return all.FirstOrDefault(a =>
            a.Id.ToString().Equals(filter, StringComparison.OrdinalIgnoreCase) ||
            a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    // ユーティリティ → CliHelpers.cs を参照
}