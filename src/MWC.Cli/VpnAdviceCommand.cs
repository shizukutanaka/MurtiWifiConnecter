using System;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MWC.Core.Abstractions;
using MWC.Core.Services;

namespace MWC.Cli;

/// <summary>
/// mwc vpn-advice — ROADMAP.md 「VPN 自動切替(信頼済み AP では VPN オフ)」の CLI 露出。
/// 純 Core の VpnAdvisoryService にスキャン結果+接続履歴を渡すだけ。
/// 助言表示のみ行い、VPN の実状態は一切変更しない。
/// </summary>
public static partial class Program
{
    private static Command BuildVpnAdvice(ServiceProvider sp)
    {
        var adapterOpt = new Option<string?>("--adapter", "Adapter GUID or name (default: first)");
        var jsonOpt    = new Option<bool>("--json", "Output JSON");

        var cmd = new Command("vpn-advice",
            "Recommend whether to use a VPN per network (advisory only — never changes VPN state)");
        cmd.AddOption(adapterOpt);
        cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (string? af, bool j) =>
        {
            var svc  = sp.GetRequiredService<IWifiService>();
            var ad   = await Resolve(svc, af);
            if (ad is null) { Err("adapter not found"); Environment.Exit(ExitCode.InvalidInput); return; }

            Console.Error.Write("Scanning…");
            var nets = await svc.ScanAsync(ad.Id);
            Console.Error.WriteLine($" {nets.Count} networks");

            var history = sp.GetRequiredService<NetworkHistoryService>();
            var vpnSvc  = new VpnAdvisoryService();

            // "既知信頼済み" は MWC 経由での過去の成功接続実績で近似する。
            var results = nets.Select(n =>
            {
                var known  = history.GetEntry(n.Ssid) is { ConnectCount: > 0 };
                var advice = vpnSvc.Analyze(n, known);
                return (Network: n, Known: known, Advice: advice);
            }).ToList();

            if (j)
            {
                Print(results.Select(r => new
                {
                    ssid           = r.Network.Ssid,
                    known_trusted  = r.Known,
                    recommendation = r.Advice.Recommendation.ToString(),
                    reason         = r.Advice.Reason
                }));
                return;
            }

            if (results.Count == 0)
            {
                Console.WriteLine("No networks in range.");
                return;
            }

            Console.WriteLine("VPN advice (informational only — does not change VPN state):");
            Console.WriteLine($"{"SSID",-32} {"Known",6} {"Recommendation",-20} Reason");
            foreach (var r in results)
                Console.WriteLine(
                    $"{Trunc(r.Network.Ssid,32),-32} {(r.Known ? "yes" : "no"),6} " +
                    $"{r.Advice.Recommendation,-20} {Trunc(r.Advice.Reason, 60)}");
        }, adapterOpt, jsonOpt);

        return cmd;
    }
}
