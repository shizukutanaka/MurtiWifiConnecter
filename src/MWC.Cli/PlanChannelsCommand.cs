using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.Cli;

/// <summary>
/// mwc plan-channels — AP 運用者向け「自分の AP をどのチャネルに設定すべきか」推奨。
/// ADR-0025 の CLI 露出。純 Core の ChannelPlannerService にスキャン結果を渡すだけ。
/// </summary>
public static partial class Program
{
    private static Command BuildPlanChannels(ServiceProvider sp)
    {
        var adapterOpt = new Option<string?>("--adapter", "Adapter GUID or name (default: first)");
        var bandOpt    = new Option<string?>("--band",    "Target band: 2.4 | 5 | 6 (default: all)");
        bandOpt.AddCompletions("2.4", "5", "6");
        var dfsOpt    = new Option<bool>("--dfs",    "Include DFS channels in 5 GHz candidates");
        var rankedOpt = new Option<bool>("--ranked", "Show full candidate ranking table per band");
        var jsonOpt   = new Option<bool>("--json",   "Output JSON");

        var cmd = new Command("plan-channels",
            "Recommend the best channel to configure on your own AP (AP operator tool)");
        cmd.AddOption(adapterOpt);
        cmd.AddOption(bandOpt);
        cmd.AddOption(dfsOpt);
        cmd.AddOption(rankedOpt);
        cmd.AddOption(jsonOpt);

        cmd.SetHandler(
            async (string? af, string? bandStr, bool dfsFlag, bool showRanked, bool j) =>
            {
              try
              {
                // Validate --band before touching the adapter.
                WifiBand? targetBand = null;
                if (!string.IsNullOrEmpty(bandStr))
                {
                    targetBand = bandStr.Trim().ToLowerInvariant() switch
                    {
                        "2.4" or "24" or "2.4ghz" => WifiBand.Band2_4GHz,
                        "5"   or "5ghz"            => WifiBand.Band5GHz,
                        "6"   or "6ghz"            => WifiBand.Band6GHz,
                        _                          => (WifiBand?)null
                    };
                    if (targetBand is null)
                    {
                        Err($"unknown band '{bandStr}' — use 2.4, 5, or 6");
                        Environment.Exit(ExitCode.InvalidInput);
                        return;
                    }
                }

                var svc = sp.GetRequiredService<IWifiService>();
                var ad  = await Resolve(svc, af);
                if (ad is null)
                {
                    Err("adapter not found");
                    Environment.Exit(ExitCode.InvalidInput);
                    return;
                }

                Console.Error.Write($"Scanning {ad.Name}…");
                var visible = await svc.ScanAsync(ad.Id);
                Console.Error.WriteLine($" {visible.Count} networks");

                var planner = new ChannelPlannerService();

                IReadOnlyList<ChannelRecommendation> recs;
                if (targetBand is not null)
                {
                    var r = planner.Recommend(targetBand.Value, visible, dfsFlag);
                    recs = r is null ? Array.Empty<ChannelRecommendation>() : new[] { r };
                }
                else
                {
                    recs = planner.RecommendAllBands(visible, dfsFlag);
                }

                if (recs.Count == 0)
                {
                    Err("no channel candidates for the requested band");
                    Environment.Exit(ExitCode.InvalidInput);
                    return;
                }

                if (j) { Print(recs); return; }

                // ── summary table ───────────────────────────────────────
                Console.WriteLine(
                    $"{"Band",4}  {"Rec Ch",6}  {"Score",5}  {"Neighbors",9}  {"DFS",3}  Reason");
                Console.WriteLine(new string('-', 102));
                foreach (var r in recs)
                {
                    Console.WriteLine(
                        $"{BandLabel(r.Band),4}  {r.RecommendedChannel,6}  {r.Score,5}" +
                        $"  {r.CompetingApCount,9}  {(r.IsDfs ? "Yes" : "No"),3}" +
                        $"  {Trunc(r.Reason, 60)}");
                }

                if (!showRanked) return;

                // ── per-band full ranking ────────────────────────────────
                foreach (var r in recs)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  {BandLabel(r.Band)} — full candidate ranking:");
                    Console.WriteLine($"  {"",1}{"Ch",4}  {"Score",5}  {"Neighbors",9}  DFS");
                    Console.WriteLine($"  {new string('-', 30)}");
                    foreach (var s in r.Ranked)
                    {
                        var marker = s.Channel == r.RecommendedChannel ? "▶" : " ";
                        Console.WriteLine(
                            $"  {marker}{s.Channel,4}  {s.Score,5}  {s.CompetingApCount,9}" +
                            $"  {(s.IsDfs ? "Yes" : "No")}");
                    }
                }
              }
              catch (Exception ex) { Err(ex.Message); Environment.Exit(ExitCode.GeneralError); }
            },
            adapterOpt, bandOpt, dfsOpt, rankedOpt, jsonOpt);

        return cmd;
    }
}
