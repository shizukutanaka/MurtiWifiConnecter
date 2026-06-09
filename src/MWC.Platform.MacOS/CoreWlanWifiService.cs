using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MWC.Core.Abstractions;
using MWC.Core.Models;

namespace MWC.Platform.MacOS;

/// <summary>
/// macOS CoreWLAN フレームワーク経由の IWifiService 実装。
///
/// .NET/C# から CoreWLAN を使う方法:
///   Option A: ObjCRuntime (Xamarin.Mac / .NET for macOS) — 推奨
///   Option B: airport コマンド経由 (フォールバック)
///   Option C: CoreWLAN P/Invoke — 複雑だが依存ゼロ
///
/// 本実装は airport CLI を使ったシンプル版(テスト・プロトタイプ用)。
/// 本番では ObjCRuntime 版に置き換えること。
///
/// 必要な entitlements:
///   - com.apple.developer.networking.wifi-info (iOS 13+/macOS 12+)
///   - com.apple.security.network.client
/// </summary>
public sealed class CoreWlanWifiService : IWifiService
{
    private const string AirportPath =
        "/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport";

    public async Task<IReadOnlyList<WifiAdapter>> GetAdaptersAsync(CancellationToken ct = default)
    {
        // networksetup -listnetworkserviceorder でWi-Fiサービス一覧を取得
        var output = await RunAsync("networksetup", ["-listallhardwareports"], ct)
            .ConfigureAwait(false);

        var adapters = new List<WifiAdapter>();
        var lines    = output.Split('\n');

        for (int i = 0; i < lines.Length - 1; i++)
        {
            if (!lines[i].Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)) continue;
            var deviceLine = lines.ElementAtOrDefault(i + 1) ?? "";
            var device     = deviceLine.Replace("Device:", "").Trim();

            adapters.Add(new WifiAdapter
            {
                Id          = GuidFromString(device),
                Name        = device,
                Description = "macOS Wi-Fi (" + device + ")",
                IsEnabled   = true,
            });
        }

        if (adapters.Count == 0)
            adapters.Add(new WifiAdapter
            {
                Id          = GuidFromString("en0"),
                Name        = "en0",
                Description = "macOS Wi-Fi (en0)",
                IsEnabled   = true,
            });

        return adapters;
    }

    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(
        Guid adapterId, CancellationToken ct = default)
    {
        // airport --scan でスキャン(要 sudo または Location Services 許可)
        var output = await RunAsync(AirportPath, ["--scan"], ct).ConfigureAwait(false);
        return ParseAirportScan(output);
    }

    public Task<bool> RegisterProfileAsync(
        Guid adapterId, string profileXml, bool overwrite, CancellationToken ct = default)
    {
        // macOS では /Library/Preferences/SystemConfiguration/com.apple.wifi.plist
        // または networksetup -addpreferredwirelessnetworkatindex で管理
        // 未実装スタブ — 登録は行われないため false。
        return Task.FromResult(false);
    }

    public async Task<ConnectionResult> ConnectAsync(
        Guid adapterId, string ssid, string profileName,
        TimeSpan timeout, CancellationToken ct = default)
    {
        // networksetup -setairportnetwork en0 <ssid> [password]
        var iface = await GetIfaceAsync(adapterId, ct).ConfigureAwait(false);
        var (exit, _, stderr) = await RunFullAsync(
            "networksetup", ["-setairportnetwork", iface, ssid], ct)
            .ConfigureAwait(false);

        if (exit == 0)
        {
            var internet = await CheckConnectivityAsync(ct).ConfigureAwait(false);
            return ConnectionResult.Ok(ssid, internet, false);
        }
        return ConnectionResult.Fail(
            stderr.Contains("password") ? ConnectionFailure.BadCredentials : ConnectionFailure.Unknown);
    }

    public async Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default)
    {
        var iface = await GetIfaceAsync(adapterId, ct).ConfigureAwait(false);
        var (exit, _, _) = await RunFullAsync(
            "networksetup", ["-setairportpower", iface, "off"], ct).ConfigureAwait(false);
        // 再度ONにして切断のみ実施
        await RunFullAsync("networksetup", ["-setairportpower", iface, "on"], ct).ConfigureAwait(false);
        return exit == 0;
    }

    // ── Private ──────────────────────────────────────────────────────

    private static IReadOnlyList<WifiNetwork> ParseAirportScan(string output)
    {
        var results = new List<WifiNetwork>();
        foreach (var line in output.Split('\n').Skip(1))  // ヘッダー行スキップ
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            // airport 出力: SSID  BSSID  RSSI  CHANNEL  HT CC SECURITY
            var parts = System.Text.RegularExpressions.Regex.Split(line.Trim(), @"\s{2,}");
            if (parts.Length < 5) continue;

            var ssid    = parts[0].Trim();
            var rssi    = int.TryParse(parts[2], out var r) ? r : -80;
            var chanStr = parts[3].Trim();
            var chan    = int.TryParse(chanStr.Split(',')[0], out var c) ? c : 0;
            var secStr  = parts.LastOrDefault() ?? "";
            var auth    = secStr.Contains("WPA3") ? AuthMethod.WPA3SAE
                        : secStr.Contains("WPA2") ? AuthMethod.WPA2PSK
                        : secStr.Contains("WPA")  ? AuthMethod.WPAPSK
                        : AuthMethod.Open;
            var band    = chan > 14 ? WifiBand.Band5GHz : WifiBand.Band2_4GHz;

            results.Add(new WifiNetwork
            {
                Ssid          = ssid,
                Auth          = auth,
                Band          = band,
                Channel       = chan,
                SignalQuality = Math.Clamp(100 + rssi, 0, 100),
                Phy           = band == WifiBand.Band5GHz ? PhyType.Dot11ac : PhyType.Dot11n,
            });
        }
        return results;
    }

    private static async Task<string> GetIfaceAsync(Guid id, CancellationToken ct)
    {
        // 簡易: en0 固定(実際は GetAdaptersAsync で解決)
        return await Task.FromResult("en0").ConfigureAwait(false);
    }

    private static async Task<bool> CheckConnectivityAsync(CancellationToken ct)
    {
        var (exit, _, _) = await RunFullAsync(
            "curl", ["-s", "--max-time", "3",
                     "https://connectivitycheck.gstatic.com/generate_204"], ct)
            .ConfigureAwait(false);
        return exit == 0;
    }

    private static async Task<string> RunAsync(string cmd, string[] args, CancellationToken ct)
    {
        var (_, stdout, _) = await RunFullAsync(cmd, args, ct).ConfigureAwait(false);
        return stdout;
    }

    private static async Task<(int exit, string stdout, string stderr)> RunFullAsync(
        string cmd, string[] args, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName               = cmd,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (var a in args)
            proc.StartInfo.ArgumentList.Add(a);
        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proc.ExitCode, stdout, stderr);
    }

    public Task<bool> DeleteProfileAsync(
        Guid adapterId, string profileName, CancellationToken ct = default)
    {
        // macOS: networksetup -removepreferredwirelessnetwork <device> <ssid>
        // Stubbed — full implementation requires entitlement validation
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<string>> ListProfilesAsync(
        Guid adapterId, CancellationToken ct = default)
    {
        // macOS: networksetup -listpreferredwirelessnetworks <device>
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    public async IAsyncEnumerable<WifiEvent> SubscribeEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // macOS CoreWLAN does not expose .NET-friendly event streams without ObjCRuntime.
        // Stub: yields nothing.
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private static Guid GuidFromString(string s)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s)));
    }
}
