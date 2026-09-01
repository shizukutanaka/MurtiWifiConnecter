using System;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.Cli;

/// <summary>
/// mwc privacy — MAC アドレス追跡に関するプライバシー勧告を表示する
/// (純 Core の <see cref="PrivacyAdvisoryService"/> の CLI 露出)。
///
/// 本サービスの唯一のプラットフォーム依存は「現在の MAC ランダム化モードの検出」だけで、
/// 勧告ロジック自体は Core のテスト可能な純関数である。そこで `mwc import-cat` と同じ分解を使う:
/// **プラットフォームが供給できない値は、ユーザーが供給する**。
/// CAT ファイルが持たない資格情報を `--username/-p` で受け取ったのと同様、
/// ここでは MAC モードを `--mac-mode` で受け取り、自動検出を待たずに勧告を出す。
/// (Windows の実 MAC モードを読む実装が入れば `--mac-mode` は既定値の供給元に置き換えられる。)
///
/// 助言表示のみ。MAC 設定は一切変更しない(他の *AdvisoryService と同じ「助言のみ」方針)。
/// </summary>
public static partial class Program
{
    private static Command BuildPrivacy(ServiceProvider sp)
    {
        var macModeOpt = new Option<string?>("--mac-mode",
            "Your current MAC setting: hardware | random-per-network | random-daily " +
            "(check Windows Wi-Fi settings). Omit if unknown.");
        macModeOpt.AddCompletions("hardware", "random-per-network", "random-daily");
        var adapterOpt = new Option<string?>("--adapter", "Adapter GUID or name (default: first)");
        var ssidOpt    = new Option<string?>("--ssid",
            "Network to advise about (default: the currently connected one)");
        var macOpt     = new Option<string?>("--mac",
            "This adapter's current MAC (e.g. from `ipconfig /all`). Randomisation is inferred " +
            "from the address itself, which is more reliable than --mac-mode.");
        var jsonOpt    = new Option<bool>("--json", "Output JSON");

        var cmd = new Command("privacy",
            "Show MAC-tracking privacy advisories (advisory only — never changes MAC settings)");
        cmd.AddOption(macModeOpt); cmd.AddOption(macOpt);
        cmd.AddOption(adapterOpt); cmd.AddOption(ssidOpt); cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (string? macModeStr, string? macStr, string? af, string? ssid, bool j) =>
        {
            try
            {
                var mode = ParseMacMode(macModeStr);
                if (mode is null)
                {
                    Err($"unknown --mac-mode '{macModeStr}'. Use: hardware | random-per-network | random-daily");
                    Environment.Exit(ExitCode.InvalidInput); return;
                }

                // --mac が渡されたらアドレスから判定し、--mac-mode より優先する。
                // 「明示指定が強い」ではなく「実測が強い」— ユーザーの自己申告より
                // アドレスのビットの方が確かなため。
                MacModeEvidence? evidence = null;
                if (macStr is not null)
                {
                    if (!MacAddressModeInference.TryParse(macStr, out var macBytes))
                    {
                        Err($"unparsable --mac '{macStr}'. Expected 6 hex octets, e.g. AA:BB:CC:DD:EE:FF");
                        Environment.Exit(ExitCode.InvalidInput); return;
                    }
                    var inferred = MacAddressModeInference.FromAddress(
                        macBytes, sp.GetService<OuiLookupService>());
                    evidence = inferred.Evidence;
                    mode = inferred.Mode;
                }

                var svc = sp.GetRequiredService<IWifiService>();
                var ad  = await Resolve(svc, af);
                if (ad is null) { Err("adapter not found"); Environment.Exit(ExitCode.InvalidInput); return; }

                var nets = await svc.ScanAsync(ad.Id);

                // 対象ネットワークの解決: --ssid → 接続中 → 中立(セキュア扱い)の順。
                // 中立を「セキュア」にするのは、対象不明のときに公共ネットワーク特有の
                // 警告 (#1) を誤って出さず、モード依存の一般助言 (#2〜#5) だけを出すため。
                var target =
                    (!string.IsNullOrWhiteSpace(ssid) ? nets.FirstOrDefault(n => n.Ssid == ssid) : null)
                    ?? nets.FirstOrDefault(n => n.IsConnected)
                    ?? new WifiNetwork { Ssid = ssid ?? "(no specific network)", Auth = AuthMethod.WPA2PSK };

                var advisories = new PrivacyAdvisoryService().Analyze(mode.Value, target);

                if (j)
                {
                    Print(new
                    {
                        macMode = mode.Value.ToString(),
                        macModeEvidence = evidence?.ToString(),
                        network = target.Ssid,
                        networkAuth = target.Auth.ToString(),
                        advisories = advisories.Select(a => new
                        {
                            severity  = a.Severity.ToString(),
                            code      = a.Code,
                            title     = a.Title,
                            detail    = a.Detail,
                            reference = a.Reference,
                        }),
                    });
                    return;
                }

                Console.WriteLine($"MAC privacy — mode: {mode.Value}, network: {target.Ssid} ({target.Auth})");
                if (evidence is not null)
                    Console.WriteLine($"(inferred from the address: {Describe(evidence.Value)})");
                Console.WriteLine("(informational only — does not change your MAC settings)");
                Console.WriteLine();

                if (advisories.Count == 0)
                {
                    // Unknown は「助言できるだけの情報が無い」であって「問題なし」ではない。
                    // 両者を同じ "No advisories." で表すと、設定を伝えていないユーザーに
                    // 「あなたのプライバシーは良好」と誤読させる — 本製品が避けるべき主張。
                    if (mode.Value == MacAddressMode.Unknown)
                    {
                        Console.WriteLine("Cannot advise: this run has no MAC information to reason about.");
                        Console.WriteLine(
                            "Pass --mac with the adapter's current address and it will be determined from " +
                            "the address itself:");
                        Console.WriteLine("    ipconfig /all        # find the Wi-Fi adapter's Physical Address");
                        Console.WriteLine("    mwc privacy --mac AA:BB:CC:DD:EE:FF");
                        Console.WriteLine(
                            "(--mac-mode hardware | random-per-network | random-daily still works if you " +
                            "already know your Windows setting.)");
                        return;
                    }

                    Console.WriteLine("No advisories for this combination.");
                    return;
                }

                foreach (var a in advisories)
                {
                    Console.WriteLine($"[{a.Severity}] {a.Code}  {a.Title}");
                    Console.WriteLine($"    {a.Detail}");
                    Console.WriteLine($"    ref: {a.Reference}");
                    Console.WriteLine();
                }

                if (macModeStr is null && macStr is null)
                    Console.WriteLine(
                        "Tip: pass --mac with the adapter's address; randomisation is then determined " +
                        "from the address rather than taken on trust.");
            }
            catch (Exception ex) { Err($"privacy failed: {ex.Message}"); Environment.Exit(ExitCode.GeneralError); }
        }, macModeOpt, macOpt, adapterOpt, ssidOpt, jsonOpt);

        return cmd;
    }

    /// <summary>判定根拠を 1 行の説明に写す。Core は文言を持たないので写像はここで行う。</summary>
    private static string Describe(MacModeEvidence e) => e switch
    {
        MacModeEvidence.LocallyAdministeredBitSet =>
            "locally-administered bit is set, so this address was generated, not burned in",
        MacModeEvidence.UniversallyAdministered =>
            "universally-administered address (IEEE-assigned OUI), i.e. the hardware address",
        MacModeEvidence.UniversallyAdministeredWithKnownVendor =>
            "universally-administered address and the OUI resolves to a known vendor",
        MacModeEvidence.AddressChangedWithinSameSsidAcrossDays =>
            "the address changed for the same SSID on different days",
        MacModeEvidence.AddressDiffersPerSsid =>
            "a different address is used per SSID",
        MacModeEvidence.NotAUnicastAddress =>
            "the group bit is set, so this is not a station address",
        MacModeEvidence.NoObservations => "no observations were supplied",
        _ => "the address could not be parsed",
    };

    /// <summary>
    /// --mac-mode の文字列を <see cref="MacAddressMode"/> にマップする。
    /// null/空 は Unknown(自動検出が未実装のため既定はこれ)。ハイフン/アンダースコアと
    /// 大文字小文字を無視し、enum 名そのものも受け付ける。未知の文字列は null(= 入力エラー)。
    /// </summary>
    private static MacAddressMode? ParseMacMode(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return MacAddressMode.Unknown;
        var key = s.Trim().Replace("-", "").Replace("_", "").ToLowerInvariant();
        return key switch
        {
            "hardware" or "fixed"           => MacAddressMode.Hardware,
            "randompernetwork" or "pernetwork" => MacAddressMode.RandomPerNetwork,
            // 種類を言っていないのだから種類を決めつけない。
            "random" or "randomized" => MacAddressMode.Randomized,
            "randomdaily" or "daily"        => MacAddressMode.RandomDaily,
            "unknown"                        => MacAddressMode.Unknown,
            _                                => null,
        };
    }
}
