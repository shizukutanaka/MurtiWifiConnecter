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
                // listallhardwareports は接続状態を示さない — 有効だが未接続として報告
                State       = AdapterState.Disconnected,
            });
        }

        if (adapters.Count == 0)
            adapters.Add(new WifiAdapter
            {
                Id          = GuidFromString("en0"),
                Name        = "en0",
                Description = "macOS Wi-Fi (en0)",
                State       = AdapterState.Disconnected,
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

    // SSID+アダプター → PSK のキャッシュ。ConnectAsync のシグネチャはパスフレーズを
    // 取らないため (NmcliWifiService と同じ分解)、RegisterProfileAsync が
    // profileXml から SSID/keyMaterial を抽出してここへ置き、ConnectAsync が参照する。
    // NOTE: パスフレーズは networksetup のプロセス引数になるため `ps` から見える。
    // 本番品質では CoreWLAN P/Invoke (CWInterface.associate) での引数回避が望ましい。
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid, string), string> _pskCache = new();

    public Task<bool> RegisterProfileAsync(
        Guid adapterId, string profileXml, bool overwrite, CancellationToken ct = default)
    {
        // Windows WLAN XML から SSID/keyMaterial を抽出しキャッシュする
        // (NmcliWifiService.RegisterProfileAsync と同じパターン。
        //  XML 実体参照はデコード必須 — '&' 等は '&amp;' で格納されている)。
        var ssidMatch = System.Text.RegularExpressions.Regex.Match(
            profileXml, @"<name>([^<]+)</name>");
        var keyMatch  = System.Text.RegularExpressions.Regex.Match(
            profileXml, @"<keyMaterial>([^<]+)</keyMaterial>");
        if (!ssidMatch.Success) return Task.FromResult(false);
        var ssid = System.Net.WebUtility.HtmlDecode(ssidMatch.Groups[1].Value);
        var pass = keyMatch.Success ? System.Net.WebUtility.HtmlDecode(keyMatch.Groups[1].Value) : "";
        _pskCache[(adapterId, ssid)] = pass;
        return Task.FromResult(true);
    }

    public async Task<ConnectionResult> ConnectAsync(
        Guid adapterId, string ssid, string profileName,
        TimeSpan timeout, CancellationToken ct = default)
    {
        // networksetup -setairportnetwork en0 <ssid> [password]
        // PSK は RegisterProfileAsync が XML から抽出したキャッシュ参照
        // (Open/OWE や未登録プロファイルでは引数なしで呼ぶ従来挙動)。
        var iface = await GetIfaceAsync(adapterId, ct).ConfigureAwait(false);
        var args = _pskCache.TryGetValue((adapterId, ssid), out var pass) && pass.Length > 0
            ? new[] { "-setairportnetwork", iface, ssid, pass }
            : new[] { "-setairportnetwork", iface, ssid };
        var (exit, _, stderr) = await RunFullAsync("networksetup", args, ct)
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
        // SSID 単位で集約する (IWifiService.ScanAsync の契約: 1 SSID = 1 WifiNetwork)。
        // airport は BSS 毎に 1 行出力するため、同一 SSID の複数バンド/AP は
        // BssEntries に束ね、最強 RSSI の行を代表値とする。隠し SSID は除外。
        var groups = new Dictionary<string, ScanGroup>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n').Skip(1))  // ヘッダー行スキップ
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            // airport 出力: SSID  BSSID  RSSI  CHANNEL  HT CC SECURITY
            var parts = System.Text.RegularExpressions.Regex.Split(line.Trim(), @"\s{2,}");
            if (parts.Length < 5) continue;

            var ssid    = parts[0].Trim();
            if (string.IsNullOrEmpty(ssid)) continue;   // 隠しネットワークは除外

            var bssid   = parts[1].Trim();
            var rssi    = int.TryParse(parts[2], out var r) ? r : -80;
            var chanStr = parts[3].Trim();
            var chan    = int.TryParse(chanStr.Split(',')[0], out var c) ? c : 0;
            var secStr  = parts.LastOrDefault() ?? "";
            var auth    = secStr.Contains("WPA3") ? AuthMethod.WPA3SAE
                        : secStr.Contains("WPA2") ? AuthMethod.WPA2PSK
                        : secStr.Contains("WPA")  ? AuthMethod.WPAPSK
                        : AuthMethod.Open;
            var band    = chan > 14 ? WifiBand.Band5GHz : WifiBand.Band2_4GHz;
            var phy     = band == WifiBand.Band5GHz ? PhyType.Dot11ac : PhyType.Dot11n;

            if (!groups.TryGetValue(ssid, out var g))
                groups[ssid] = g = new ScanGroup();

            g.Bss.Add(new BssInfo { Bssid = bssid, Rssi = rssi, Channel = chan, Phy = phy });
            // 最強 RSSI の行を代表にする (RSSI は負値なので大きいほど強い)。
            if (g.Representative is null || rssi > g.BestRssi)
            {
                g.BestRssi = rssi;
                g.Representative = new WifiNetwork
                {
                    Ssid          = ssid,
                    Auth          = auth,
                    Band          = band,
                    Channel       = chan,
                    SignalQuality = Math.Clamp(100 + rssi, 0, 100),
                    Phy           = phy,
                };
            }
        }

        var results = new List<WifiNetwork>(groups.Count);
        foreach (var g in groups.Values)
        {
            if (g.Representative is null) continue;
            results.Add(g.Representative with { BssEntries = g.Bss.ToArray() });
        }
        return results;
    }

    // SSID 単位のスキャン集約用の作業バッファ。
    private sealed class ScanGroup
    {
        public WifiNetwork? Representative;
        public int BestRssi = int.MinValue;
        public readonly List<BssInfo> Bss = new();
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
        try
        {
            // Drain stdout and stderr concurrently — sequential reads can deadlock if
            // the child fills the stderr pipe buffer (~64KB) before stdout reaches EOF.
            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync(ct);
            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return (proc.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            // Dispose() does not terminate a running child — kill it so a
            // cancelled call does not leave an orphaned process behind.
            try { if (!proc.HasExited) proc.Kill(); } catch { /* best effort */ }
            throw;
        }
    }

    // プロファイル削除時はキャッシュした PSK も捨てる (接続後の残存を残さない)。
    public async Task<bool> DeleteProfileAsync(
        Guid adapterId, string profileName, CancellationToken ct = default)
    {
        // 優先ネットワークから削除。profileName は本実装では SSID として使う
        // (RegisterProfileAsync が SSID キーでキャッシュするため)。
        _pskCache.TryRemove((adapterId, profileName), out _);
        var iface = await GetIfaceAsync(adapterId, ct).ConfigureAwait(false);
        var (exit, _, _) = await RunFullAsync(
            "networksetup", ["-removepreferredwirelessnetwork", iface, profileName], ct)
            .ConfigureAwait(false);
        return exit == 0;
    }

    public async Task<IReadOnlyList<string>> ListProfilesAsync(
        Guid adapterId, CancellationToken ct = default)
    {
        // networksetup -listpreferredwirelessnetworks <device>
        // 出力: 先頭行 "Preferred networks on en0:" + 続行に SSID が1行ずつ。
        var iface = await GetIfaceAsync(adapterId, ct).ConfigureAwait(false);
        var (exit, stdout, _) = await RunFullAsync(
            "networksetup", ["-listpreferredwirelessnetworks", iface], ct)
            .ConfigureAwait(false);
        if (exit != 0) return Array.Empty<string>();
        return stdout.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("Preferred networks", StringComparison.Ordinal))
            .ToList();
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
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(s));
        return new Guid(hash.AsSpan(0, 16));
    }
}
