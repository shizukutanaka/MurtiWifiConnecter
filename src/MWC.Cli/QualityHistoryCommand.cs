using System.CommandLine;
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
        var cmd     = new Command("quality", "Measure network quality (latency + packet loss)");
        cmd.AddOption(host); cmd.AddOption(samples); cmd.AddOption(json);

        cmd.SetHandler(async (string h, int s, bool j) =>
        {
            var svc = sp.GetRequiredService<NetworkQualityService>();
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
        }, host, samples, json);
        return cmd;
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