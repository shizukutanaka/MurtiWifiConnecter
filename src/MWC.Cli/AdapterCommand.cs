using System;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MWC.Core.Abstractions;
using MWC.Core.Services;

namespace MWC.Cli;

/// <summary>
/// mwc adapter 子機ごとの設定操作。
///
///   mwc adapter list                           # 設定済み子機一覧
///   mwc adapter rename "Wi-Fi" "自宅用"        # カスタム名設定
///   mwc adapter band "USB Adapter" 5           # バンド固定
///   mwc adapter pin "Wi-Fi" "HomeWiFi"         # SSIDピン留め
///   mwc adapter unpin "Wi-Fi" "OldNetwork"     # ピン解除
///   mwc adapter enable "USB Adapter"           # 有効化
///   mwc adapter disable "USB Adapter"          # 無効化
/// </summary>
internal static class AdapterCommand
{
    internal static Command Build(ServiceProvider sp)
    {
        var cmd = new Command("adapter", "Per-adapter preferences");
        cmd.AddCommand(BuildList(sp));
        cmd.AddCommand(BuildRename(sp));
        cmd.AddCommand(BuildBand(sp));
        cmd.AddCommand(BuildPin(sp));
        cmd.AddCommand(BuildUnpin(sp));
        cmd.AddCommand(BuildEnable(sp));
        cmd.AddCommand(BuildDisable(sp));
        return cmd;
    }

    private static Command BuildList(ServiceProvider sp)
    {
        var c = new Command("list", "List all adapters with their preferences");
        c.SetHandler(async () =>
        {
            var wifi  = sp.GetRequiredService<IWifiService>();
            var prefs = sp.GetRequiredService<AdapterPreferencesService>();
            var ads   = await wifi.GetAdaptersAsync();
            Console.WriteLine($"{"NAME",-30} {"BAND",-12} {"ENABLED",-9} {"PINNED",-7} LABEL");
            Console.WriteLine(new string('─', 78));
            foreach (var a in ads)
            {
                var p = prefs.Get(a.Id);
                Console.WriteLine(
                    $"{Trunc(a.Name, 30),-30} {p.PreferredBand,-12} " +
                    $"{(p.IsEnabled ? "yes" : "no"),-9} {p.PinnedSsids.Count,-7} " +
                    $"{p.CustomLabel ?? "-"}");
            }
        });
        return c;
    }

    private static Command BuildRename(ServiceProvider sp)
    {
        var n = new Argument<string>("adapter", "Adapter name or GUID");
        var l = new Argument<string>("label",   "New custom label");
        var c = new Command("rename", "Set custom label for an adapter");
        c.AddArgument(n); c.AddArgument(l);
        c.SetHandler(async (string name, string label) =>
        {
            var (id, ok) = await ResolveAdapter(sp, name);
            if (!ok) { Console.Error.WriteLine($"Not found: {name}"); Environment.Exit(2); return; }
            sp.GetRequiredService<AdapterPreferencesService>().SetLabel(id, label);
            Console.WriteLine($"✓ Renamed: {label}");
        }, n, l);
        return c;
    }

    private static Command BuildBand(ServiceProvider sp)
    {
        var n = new Argument<string>("adapter", "Adapter name or GUID");
        var b = new Argument<string>("band",    "any | 2.4 | 5 | 6");
        b.AddCompletions("any", "2.4", "5", "6");
        var c = new Command("band", "Set preferred band for an adapter");
        c.AddArgument(n); c.AddArgument(b);
        c.SetHandler(async (string name, string band) =>
        {
            var (id, ok) = await ResolveAdapter(sp, name);
            if (!ok) { Console.Error.WriteLine($"Not found: {name}"); Environment.Exit(2); return; }
            var pref = band.ToLowerInvariant() switch
            {
                "2.4" => BandPreference.Only2_4GHz,
                "5"   => BandPreference.Only5GHz,
                "6"   => BandPreference.Only6GHz,
                _     => BandPreference.Any
            };
            sp.GetRequiredService<AdapterPreferencesService>().SetBandFilter(id, pref);
            Console.WriteLine($"✓ Band: {pref}");
        }, n, b);
        return c;
    }

    private static Command BuildPin(ServiceProvider sp)
    {
        var n = new Argument<string>("adapter", "Adapter name or GUID");
        var s = new Argument<string>("ssid",    "SSID to pin");
        var c = new Command("pin", "Pin an SSID for this adapter");
        c.AddArgument(n); c.AddArgument(s);
        c.SetHandler(async (string name, string ssid) =>
        {
            var (id, ok) = await ResolveAdapter(sp, name);
            if (!ok) { Console.Error.WriteLine($"Not found: {name}"); Environment.Exit(2); return; }
            sp.GetRequiredService<AdapterPreferencesService>().PinSsid(id, ssid);
            Console.WriteLine($"★ Pinned: {ssid}");
        }, n, s);
        return c;
    }

    private static Command BuildUnpin(ServiceProvider sp)
    {
        var n = new Argument<string>("adapter", "Adapter name or GUID");
        var s = new Argument<string>("ssid",    "SSID to unpin");
        var c = new Command("unpin", "Remove an SSID from this adapter's pin list");
        c.AddArgument(n); c.AddArgument(s);
        c.SetHandler(async (string name, string ssid) =>
        {
            var (id, ok) = await ResolveAdapter(sp, name);
            if (!ok) { Console.Error.WriteLine($"Not found: {name}"); Environment.Exit(2); return; }
            sp.GetRequiredService<AdapterPreferencesService>().UnpinSsid(id, ssid);
            Console.WriteLine($"☆ Unpinned: {ssid}");
        }, n, s);
        return c;
    }

    private static Command BuildEnable(ServiceProvider sp)
        => BuildToggle(sp, "enable",  true,  "Enable an adapter in MWC");
    private static Command BuildDisable(ServiceProvider sp)
        => BuildToggle(sp, "disable", false, "Disable an adapter (hide from list)");

    private static Command BuildToggle(ServiceProvider sp, string verb, bool on, string desc)
    {
        var n = new Argument<string>("adapter", "Adapter name or GUID");
        var c = new Command(verb, desc);
        c.AddArgument(n);
        c.SetHandler(async (string name) =>
        {
            var (id, ok) = await ResolveAdapter(sp, name);
            if (!ok) { Console.Error.WriteLine($"Not found: {name}"); Environment.Exit(2); return; }
            sp.GetRequiredService<AdapterPreferencesService>().SetEnabled(id, on);
            Console.WriteLine($"✓ {(on ? "Enabled" : "Disabled")}: {name}");
        }, n);
        return c;
    }

    private static async Task<(Guid id, bool ok)> ResolveAdapter(ServiceProvider sp, string n)
    {
        var ads = await sp.GetRequiredService<IWifiService>().GetAdaptersAsync();
        var ad = ads.FirstOrDefault(a =>
            a.Id.ToString().Equals(n, StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
        return ad is null ? (Guid.Empty, false) : (ad.Id, true);
    }

    private static string Trunc(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";
}
