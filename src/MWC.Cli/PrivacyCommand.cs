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
        var jsonOpt    = new Option<bool>("--json", "Output JSON");

        var cmd = new Command("privacy",
            "Show MAC-tracking privacy advisories (advisory only — never changes MAC settings)");
        cmd.AddOption(macModeOpt); cmd.AddOption(adapterOpt); cmd.AddOption(ssidOpt); cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (string? macModeStr, string? af, string? ssid, bool j) =>
        {
            try
            {
                var mode = ParseMacMode(macModeStr);
                if (mode is null)
                {
                    Err($"unknown --mac-mode '{macModeStr}'. Use: hardware | random-per-network | random-daily");
                    Environment.Exit(ExitCode.InvalidInput); return;
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
                Console.WriteLine("(informational only — does not change your MAC settings)");
                Console.WriteLine();

                if (advisories.Count == 0)
                {
                    // Unknown は「助言できるだけの情報が無い」であって「問題なし」ではない。
                    // 両者を同じ "No advisories." で表すと、設定を伝えていないユーザーに
                    // 「あなたのプライバシーは良好」と誤読させる — 本製品が避けるべき主張。
                    if (mode.Value == MacAddressMode.Unknown)
                    {
                        Console.WriteLine(
                            "Cannot advise: your MAC setting is unknown (this build cannot detect it).");
                        Console.WriteLine(
                            "Check Windows → Settings → Network & internet → Wi-Fi → Random hardware addresses,");
                        Console.WriteLine(
                            "then re-run with --mac-mode hardware | random-per-network | random-daily.");
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

                if (macModeStr is null)
                    Console.WriteLine(
                        "Tip: pass --mac-mode with your actual Windows setting for accurate advice.");
            }
            catch (Exception ex) { Err($"privacy failed: {ex.Message}"); Environment.Exit(ExitCode.GeneralError); }
        }, macModeOpt, adapterOpt, ssidOpt, jsonOpt);

        return cmd;
    }

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
            "randompernetwork" or "pernetwork" or "random" => MacAddressMode.RandomPerNetwork,
            "randomdaily" or "daily"        => MacAddressMode.RandomDaily,
            "unknown"                        => MacAddressMode.Unknown,
            _                                => null,
        };
    }
}
