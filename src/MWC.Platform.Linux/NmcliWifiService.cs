using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Profile;

namespace MWC.Platform.Linux;

/// <summary>
/// Linux NetworkManager (nmcli) 経由の IWifiService 実装。
///
/// 前提条件:
///   - NetworkManager 1.38+ (nmcli --version)
///   - ユーザーが `netdev` グループに所属しているか sudo 不要設定
///
/// 主なコマンドマッピング:
///   GetAdaptersAsync()      → nmcli -t -f DEVICE,TYPE,STATE device
///   ScanAsync()             → nmcli -t -f SSID,BSSID,MODE,CHAN,FREQ,RATE,SIGNAL,SECURITY dev wifi list --rescan yes
///   ConnectAsync()          → nmcli device wifi connect <ssid> password <pass>
///   DisconnectAsync()       → nmcli device disconnect <iface>
///   RegisterProfileAsync()  → nmcli connection add / modify
/// </summary>
public sealed class NmcliWifiService : IWifiService
{
    private readonly string _iface;  // デフォルトインターフェース名(例: wlan0, wlp2s0)

    public NmcliWifiService(string interfaceName = "")
        => _iface = interfaceName;

    // ── IWifiService 実装 ────────────────────────────────────────────

    public async Task<IReadOnlyList<WifiAdapter>> GetAdaptersAsync(CancellationToken ct = default)
    {
        // nmcli -t -f DEVICE,TYPE,STATE,CONNECTION device
        var output = await RunNmcliAsync(
            "-t -f DEVICE,TYPE,STATE,CONNECTION device", ct).ConfigureAwait(false);

        var adapters = new List<WifiAdapter>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = line.Split(':');
            if (cols.Length < 3) continue;
            if (!cols[1].Contains("wifi", StringComparison.OrdinalIgnoreCase)) continue;

            var device = cols[0].Trim();
            var state  = cols[2].Trim();
            var conn   = cols.Length > 3 ? cols[3].Trim() : null;

            adapters.Add(new WifiAdapter
            {
                Id            = GuidFromString(device),
                Name          = device,
                Description   = $"Linux Wi-Fi ({device})",
                IsEnabled     = state != "unavailable" && state != "unmanaged",
                ConnectedSsid = state == "connected" ? conn : null,
            });
        }
        return adapters;
    }

    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(
        Guid adapterId, CancellationToken ct = default)
    {
        var iface = await ResolveIface(adapterId, ct).ConfigureAwait(false);

        // --rescan yes で強制再スキャン(3秒程度かかる)
        var output = await RunNmcliAsync(
            $"-t -f SSID,BSSID,MODE,CHAN,FREQ,RATE,SIGNAL,SECURITY,IN-USE " +
            $"dev wifi list ifname {iface} --rescan yes", ct).ConfigureAwait(false);

        var networks = new Dictionary<string, WifiNetwork>(StringComparer.Ordinal);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = line.Split(':');
            if (cols.Length < 8) continue;

            var ssid     = cols[0].Trim();
            var chan     = int.TryParse(cols[3], out var ch) ? ch : 0;
            var freq     = int.TryParse(Regex.Replace(cols[4], @"[^\d]", ""), out var f) ? f : 0;
            var signal   = int.TryParse(cols[6], out var s) ? s : 0;
            var security = cols[7].Trim();
            var inUse    = cols.Length > 8 && cols[8].Contains("*");
            var auth     = ParseSecurity(security);
            var band     = freq >= 5000 ? (freq >= 5950 ? WifiBand.Band6GHz : WifiBand.Band5GHz)
                                        : WifiBand.Band2_4GHz;
            var phy      = band == WifiBand.Band6GHz ? PhyType.Dot11ax
                         : chan > 14                 ? PhyType.Dot11ac
                                                     : PhyType.Dot11n;

            if (string.IsNullOrEmpty(ssid)) ssid = "<hidden>";

            networks[ssid + cols[1]] = new WifiNetwork
            {
                Ssid          = ssid,
                Auth          = auth,
                Band          = band,
                Channel       = chan,
                FrequencyMhz  = freq,
                SignalQuality = signal,
                Phy           = phy,
                IsConnected   = inUse,
                BssEntries    = new[]
                {
                    new BssInfo { Bssid = cols[1].Trim(), Rssi = (signal - 100) }
                }
            };
        }
        return networks.Values.ToList();
    }

    public async Task<bool> RegisterProfileAsync(
        Guid adapterId, string profileXml, bool overwrite, CancellationToken ct = default)
    {
        // Windows WLAN XML からキー情報を抽出して nmcli connection として登録
        // XML: <SSID><name>…</name></SSID>, <keyMaterial>…</keyMaterial>
        var ssidMatch = System.Text.RegularExpressions.Regex.Match(
            profileXml, @"<name>([^<]+)</name>");
        var keyMatch  = System.Text.RegularExpressions.Regex.Match(
            profileXml, @"<keyMaterial>([^<]+)</keyMaterial>");

        if (!ssidMatch.Success) return false;
        var ssid = ssidMatch.Groups[1].Value;
        var pass = keyMatch.Success ? keyMatch.Groups[1].Value : "";

        // nmcli connection add で登録(既存があれば modify)
        var op = overwrite ? "modify" : "add";
        if (overwrite)
        {
            // 既存の接続設定を更新
            var (exitMod, _, _) = await RunNmcliFullAsync(
                $"connection modify "{EscapeShell(ssid)}" wifi-sec.psk "{EscapeShell(pass)}"", ct)
                .ConfigureAwait(false);
            if (exitMod == 0) return true;
        }

        // 新規追加
        var args = string.IsNullOrEmpty(pass)
            ? $"connection add type wifi ssid "{EscapeShell(ssid)}""
            : $"connection add type wifi ssid "{EscapeShell(ssid)}" wifi-sec.key-mgmt wpa-psk wifi-sec.psk "{EscapeShell(pass)}"";
        var (exitAdd, _, _) = await RunNmcliFullAsync(args, ct).ConfigureAwait(false);
        return exitAdd == 0;
    }

    public async Task<ConnectionResult> ConnectAsync(
        Guid adapterId, string ssid, string profileName,
        TimeSpan timeout, CancellationToken ct = default)
    {
        var iface = await ResolveIface(adapterId, ct).ConfigureAwait(false);

        // nmcli device wifi connect <ssid> ifname <iface>
        var (exit, stdout, stderr) = await RunNmcliFullAsync(
            $"device wifi connect \"{EscapeShell(ssid)}\" ifname {iface}",
            ct).ConfigureAwait(false);

        if (exit == 0)
        {
            var hasInternet = await CheckInternetAsync(ct).ConfigureAwait(false);
            return ConnectionResult.Ok(ssid, hasInternet, false);
        }

        var failure = stderr.Contains("No network with SSID") ? ConnectionFailure.NotInRange
                    : stderr.Contains("Secrets were required") ? ConnectionFailure.BadCredentials
                    : ConnectionFailure.Unknown;
        return ConnectionResult.Fail(failure);
    }

    public async Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default)
    {
        var iface = await ResolveIface(adapterId, ct).ConfigureAwait(false);
        var (exit, _, _) = await RunNmcliFullAsync(
            $"device disconnect {iface}", ct).ConfigureAwait(false);
        return exit == 0;
    }

    // ── Private helpers ──────────────────────────────────────────────

    private async Task<string> ResolveIface(Guid adapterId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_iface)) return _iface;
        var adapters = await GetAdaptersAsync(ct).ConfigureAwait(false);
        return adapters.FirstOrDefault(a => a.Id == adapterId)?.Name ?? "wlan0";
    }

    private static async Task<string> RunNmcliAsync(string args, CancellationToken ct)
    {
        var (_, stdout, _) = await RunNmcliFullAsync(args, ct).ConfigureAwait(false);
        return stdout;
    }

    private static async Task<(int exit, string stdout, string stderr)> RunNmcliFullAsync(
        string args, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName               = "nmcli",
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proc.ExitCode, stdout, stderr);
    }

    private static async Task<bool> CheckInternetAsync(CancellationToken ct)
    {
        try
        {
            var (exit, _, _) = await RunNmcliFullAsync(
                "networking connectivity check", ct).ConfigureAwait(false);
            return exit == 0;
        }
        catch { return false; }
    }

    private static AuthMethod ParseSecurity(string security)
    {
        if (security.Contains("WPA3"))   return AuthMethod.WPA3SAE;
        if (security.Contains("WPA2"))   return AuthMethod.WPA2PSK;
        if (security.Contains("WPA"))    return AuthMethod.WPAPSK;
        if (security.Contains("WEP"))    return AuthMethod.WEP;
        if (security.Contains("OWE"))    return AuthMethod.OWE;
        if (security == "--")            return AuthMethod.Open;
        return AuthMethod.Open;
    }

    private static Guid GuidFromString(string s)
    {
        // 決定論的 Guid: デバイス名から生成
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
        return new Guid(hash);
    }

    private static string EscapeShell(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
