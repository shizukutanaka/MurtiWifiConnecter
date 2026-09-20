using System;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MWC.Core.Abstractions;
using MWC.Core.Services;

namespace MWC.Cli;

/// <summary>
/// mwc passpoint — 周囲の Passpoint (Hotspot 2.0) 対応 AP を一覧し、
/// 主要キャリアのプリセットを表示する。
///
/// Passpoint は「事前に資格情報を登録しておけば、対応 AP へ自動でローミングできる」仕組みで、
/// OpenRoaming の普及に伴い空港・駅・カフェで広がりつつある。
///
/// 判定は <see cref="MWC.Core.Models.WifiNetwork.IsPasspoint"/> —
/// Enterprise 認証であること、かつ 802.11u Interworking 要素 (Element ID 107) を
/// 広告していること。後者は 2026-07 に `BeaconIeParser` へ実装するまで
/// **どの層も設定しておらず常に false** だったため、この機能は配線できなかった
/// (docs/FEATURE-AUDIT.md §1a)。Windows では `WlanBssIeProvider` が生ビーコンを供給し、
/// `BeaconEnrichmentService` が解析結果を適用する。
///
/// 注意: Interworking の有無は Passpoint の**第一段のふるい分け**であり、
/// 完全な Hotspot 2.0 判定には Vendor Specific 要素 (WFA OUI) の確認も要る。
/// そのため本コマンドは「候補」として提示し、断定的な表現を避けている。
/// </summary>
public static partial class Program
{
    private static Command BuildPasspoint(ServiceProvider sp)
    {
        var adapterOpt  = new Option<string?>("--adapter", "Adapter GUID or name (default: first)");
        var jsonOpt     = new Option<bool>("--json", "Output JSON");
        var carriersOpt = new Option<bool>("--carriers",
            "List the built-in carrier presets instead of scanning");

        var cmd = new Command("passpoint",
            "List nearby Passpoint (Hotspot 2.0) capable access points");
        cmd.AddOption(adapterOpt); cmd.AddOption(jsonOpt); cmd.AddOption(carriersOpt);

        cmd.SetHandler(async (string? af, bool j, bool carriers) =>
        {
            try
            {
                var hs = new Hotspot20Service();

                if (carriers)
                {
                    var presets = Hotspot20Service.KnownCarriers;
                    if (j) { Print(presets); return; }

                    Console.WriteLine($"{"Carrier",-24}  {"SSID",-24}  {"EAP",-16}  Domain");
                    Console.WriteLine(new string('-', 78));
                    foreach (var p in presets)
                        Console.WriteLine($"{Trunc(p.CarrierName, 24),-24}  {Trunc(p.Ssid, 24),-24}  {p.EapType,-16}  {p.Domain}");
                    return;
                }

                var svc = sp.GetRequiredService<IWifiService>();
                var ad  = await Resolve(svc, af);
                if (ad is null) { Err("adapter not found"); Environment.Exit(ExitCode.InvalidInput); return; }

                var nets  = await svc.ScanAsync(ad.Id);
                var found = hs.FilterPasspointNetworks(nets);

                if (j)
                {
                    Print(found.Select(n => new
                    {
                        ssid    = n.Ssid,
                        auth    = n.Auth.ToString(),
                        signal  = n.SignalQuality,
                        band    = n.Band.ToString(),
                        channel = n.Channel,
                    }));
                    return;
                }

                if (found.Count == 0)
                {
                    Console.WriteLine("No Passpoint-capable access points found.");
                    Console.WriteLine();
                    Console.WriteLine("This means no nearby network advertised an 802.11u Interworking element");
                    Console.WriteLine("together with Enterprise security. Run 'mwc passpoint --carriers'");
                    Console.WriteLine("to see the carrier presets this build knows about.");
                    return;
                }

                Console.WriteLine($"{"SSID",-30}  {"Security",-20}  {"Signal",6}  {"Band",-8}  Ch");
                Console.WriteLine(new string('-', 82));
                foreach (var n in found.OrderByDescending(n => n.SignalQuality))
                    Console.WriteLine(
                        $"{Trunc(n.Ssid, 30),-30}  {n.Auth,-20}  {n.SignalQuality,5}%  {n.Band,-8}  {n.Channel}");

                Console.WriteLine();
                Console.WriteLine($"{found.Count} of {nets.Count} networks advertise Passpoint support.");
            }
            catch (Exception ex) { Err($"passpoint failed: {ex.Message}"); Environment.Exit(ExitCode.GeneralError); }
        }, adapterOpt, jsonOpt, carriersOpt);

        return cmd;
    }
}
