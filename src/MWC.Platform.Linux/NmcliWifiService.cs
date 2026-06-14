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
            ["-t", "-f", "DEVICE,TYPE,STATE,CONNECTION", "device"], ct).ConfigureAwait(false);

        var adapters = new List<WifiAdapter>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = SplitTerse(line);
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
            ["-t", "-f", "SSID,BSSID,MODE,CHAN,FREQ,RATE,SIGNAL,SECURITY,IN-USE",
             "dev", "wifi", "list", "ifname", iface, "--rescan", "yes"], ct).ConfigureAwait(false);

        var networks = new Dictionary<string, WifiNetwork>(StringComparer.Ordinal);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = SplitTerse(line);
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
                    // 品質(0-100%) を概算 RSSI(-100..-30 dBm) に変換。
                    // signal-100 だと 0 dBm(非現実的に強い)になるため 0.7 係数で圧縮
                    // (RssiDistanceEstimator.QualityToRssi と一致)。
                    new BssInfo { Bssid = cols[1].Trim(),
                                  Rssi = (int)Math.Round(-100 + Math.Clamp(signal, 0, 100) * 0.7) }
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
        if (overwrite)
        {
            // 既存の接続設定を更新
            var modArgs = string.IsNullOrEmpty(pass)
                ? new[] { "connection", "modify", ssid }
                : new[] { "connection", "modify", ssid, "wifi-sec.psk", pass };
            var (exitMod, _, _) = await RunNmcliFullAsync(modArgs, ct).ConfigureAwait(false);
            if (exitMod == 0) return true;
        }

        // 新規追加
        var args = string.IsNullOrEmpty(pass)
            ? new[] { "connection", "add", "type", "wifi", "ssid", ssid }
            : new[] { "connection", "add", "type", "wifi", "ssid", ssid,
                      "wifi-sec.key-mgmt", "wpa-psk", "wifi-sec.psk", pass };
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
            ["device", "wifi", "connect", ssid, "ifname", iface],
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
            ["device", "disconnect", iface], ct).ConfigureAwait(false);
        return exit == 0;
    }

    // ── Private helpers ──────────────────────────────────────────────

    // nmcli terse(-t)モードはフィールド内の ':' を '\:'、'\' を '\\' にエスケープする。
    // 単純な Split(':') では BSSID(AA:BB:...) が列にまたがり位置がズレる。
    // 旧実装は Regex (?<!\\): を使っていたが、これは「値が '\' で終わる」場合に破綻する:
    //   SSID "foo\" → エンコード "foo\\" → 区切りは "foo\\:..." となり、'\:' の lookbehind が
    //   区切りコロンを「エスケープ済み」と誤認して分割せず、SSID と BSSID が結合する。
    //   (lookbehind ではバックスラッシュの偶奇を数えられないため原理的に不可能。)
    // バックスラッシュ状態を追う逐次スキャナに置き換え、'\X' を X として取り込み、
    // 非エスケープの ':' のみを区切りとする。アンエスケープも同時に行う。
    private static string[] SplitTerse(string line)
    {
        var cols = new List<string>();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                sb.Append(line[i + 1]);   // エスケープされた文字をそのまま取り込む
                i++;
            }
            else if (c == ':')
            {
                cols.Add(sb.ToString());  // 非エスケープのコロン = フィールド区切り
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        cols.Add(sb.ToString());
        return cols.ToArray();
    }

    private async Task<string> ResolveIface(Guid adapterId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_iface)) return _iface;
        var adapters = await GetAdaptersAsync(ct).ConfigureAwait(false);
        return adapters.FirstOrDefault(a => a.Id == adapterId)?.Name ?? "wlan0";
    }

    private static async Task<string> RunNmcliAsync(string[] args, CancellationToken ct)
    {
        var (_, stdout, _) = await RunNmcliFullAsync(args, ct).ConfigureAwait(false);
        return stdout;
    }

    private static async Task<(int exit, string stdout, string stderr)> RunNmcliFullAsync(
        string[] args, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName               = "nmcli",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };
        foreach (var a in args)
            proc.StartInfo.ArgumentList.Add(a);
        proc.Start();
        // Drain stdout and stderr concurrently. Reading them sequentially
        // (stdout to EOF, then stderr) can deadlock: if nmcli writes more to
        // stderr than the OS pipe buffer (~64KB) before closing stdout, it
        // blocks on the stderr write while we await stdout that never ends.
        // With ct often defaulted (no timeout), that hang would be permanent.
        Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync(ct);
        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proc.ExitCode, stdout, stderr);
    }

    private static async Task<bool> CheckInternetAsync(CancellationToken ct)
    {
        try
        {
            var (exit, _, _) = await RunNmcliFullAsync(
                ["networking", "connectivity", "check"], ct).ConfigureAwait(false);
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
        // 決定論的 Guid: SHA-256 先頭 16 バイト。MD5 は FIPS 強制環境で例外を投げるため不使用。
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(s));
        return new Guid(hash.AsSpan(0, 16));
    }

    public async Task<bool> DeleteProfileAsync(
        Guid adapterId, string profileName, CancellationToken ct = default)
    {
        var (exit, _, _) = await RunNmcliFullAsync(
            ["connection", "delete", profileName], ct).ConfigureAwait(false);
        return exit == 0;
    }

    public async Task<IReadOnlyList<string>> ListProfilesAsync(
        Guid adapterId, CancellationToken ct = default)
    {
        var output = await RunNmcliAsync(
            ["-t", "-f", "NAME,TYPE", "connection", "show"], ct).ConfigureAwait(false);
        var profiles = new List<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = SplitTerse(line);
            if (cols.Length >= 2 && cols[1].Contains("wifi", StringComparison.OrdinalIgnoreCase))
                profiles.Add(cols[0].Trim());
        }
        return profiles;
    }

    public async IAsyncEnumerable<WifiEvent> SubscribeEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // nmcli monitor はイベント発生時に行を出力する。
        // 例: "wlan0: connected to \"HomeWifi\""  / "wlan0: disconnected"
        // プロセスが予期せず死んだ場合は 3 秒後に再起動する。
        var adapterCache = await GetAdaptersAsync(ct).ConfigureAwait(false);
        var ifaceToId    = adapterCache.ToDictionary(a => a.Name, a => a.Id);

        while (!ct.IsCancellationRequested)
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName               = "nmcli",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            proc.StartInfo.ArgumentList.Add("monitor");

            Process? started = null;
            try
            {
                proc.Start();
                started = proc;
                // stdout を非同期で行単位に読み込む
                while (!ct.IsCancellationRequested)
                {
                    var readTask = proc.StandardOutput.ReadLineAsync(ct).AsTask();
                    var line = await readTask.ConfigureAwait(false);
                    if (line is null) break;  // プロセス終了

                    // 形式: "<iface>: connected to \"<ssid>\""  or  "<iface>: disconnected"
                    var colonIdx = line.IndexOf(':', StringComparison.Ordinal);
                    if (colonIdx <= 0) continue;

                    var iface = line[..colonIdx].Trim();
                    var rest  = line[(colonIdx + 1)..].Trim();

                    if (!ifaceToId.TryGetValue(iface, out var adapterId)) continue;

                    if (rest.StartsWith("connected to", StringComparison.OrdinalIgnoreCase))
                    {
                        // connected to "SSID" — SSID は引用符で囲まれていることがある
                        var ssid = rest["connected to".Length..].Trim().Trim('"');
                        yield return new WifiEvent(adapterId, WifiEventType.Connected,
                            string.IsNullOrEmpty(ssid) ? null : ssid, DateTimeOffset.UtcNow);
                    }
                    else if (rest.StartsWith("disconnected", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return new WifiEvent(adapterId, WifiEventType.Disconnected,
                            null, DateTimeOffset.UtcNow);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* nmcli が見つからない等は再試行 */ }
            finally
            {
                if (started is not null && !started.HasExited)
                    try { started.Kill(); } catch { }
            }

            // プロセス死亡後は 3 秒待って再起動
            if (!ct.IsCancellationRequested)
                await Task.Delay(3000, ct).ConfigureAwait(false);
        }
    }
}
