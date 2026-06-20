using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ManagedNativeWifi;
using Microsoft.Extensions.Logging;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.Platform.Windows;

/// <summary>
/// ManagedNativeWifi 3.x を使った IWifiService 実装。
/// netsh.exe / WMI を一切使わず WlanAPI 直叩き。
/// API バージョン差異は try/catch で吸収。
/// </summary>
public sealed class WindowsWifiService : IWifiService
{
    private readonly ILogger<WindowsWifiService> _log;
    private readonly IConnectivityChecker        _connectivity;
    private readonly IBeaconIeProvider           _ieProvider;
    private readonly BeaconEnrichmentService     _enrichment = new();

    public WindowsWifiService(
        ILogger<WindowsWifiService> log,
        IConnectivityChecker connectivity,
        IBeaconIeProvider? ieProvider = null)
    {
        _log          = log;
        _connectivity = connectivity;
        // 生 IE 供給源が無ければ Null オブジェクト (基本スキャンのみ、劣化なし)
        _ieProvider   = ieProvider ?? NullBeaconIeProvider.Instance;
    }

    // ── Adapters ────────────────────────────────────────────────────
    public Task<IReadOnlyList<WifiAdapter>> GetAdaptersAsync(CancellationToken ct = default)
    {
        try
        {
            var list = NativeWifi.EnumerateInterfaces()
                .Select(i => new WifiAdapter
                {
                    Id            = i.Id,
                    Name          = i.Description ?? i.Id.ToString(),
                    Description   = i.Description ?? "",
                    State         = MapState(i.State),
                    // ConnectedSsid must be set here; AdapterFailoverService reads it
                    // to detect link-loss transitions. Without this, currentSsid is
                    // always null and the failover trigger never fires.
                    ConnectedSsid = GetConnectedSsid(i.Id)
                })
                .ToList();
            return Task.FromResult<IReadOnlyList<WifiAdapter>>(list);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "EnumerateInterfaces failed");
            return Task.FromResult<IReadOnlyList<WifiAdapter>>(Array.Empty<WifiAdapter>());
        }
    }

    // ── Scan ─────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(
        Guid adapterId, CancellationToken ct = default)
    {
        try
        {
            await NativeWifi.ScanNetworksAsync(adapterId, TimeSpan.FromSeconds(8), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _log.LogWarning(ex, "ScanNetworks warning"); }

        try
        {
            // BSS 情報(BSSID/RSSI/Channel)を取得
            var bssMap = BuildBssMap(adapterId);

            var networks = NativeWifi.EnumerateAvailableNetworks()
                .Where(n => n.Interface.Id == adapterId)
                .GroupBy(n => n.Ssid.ToString())
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .Select(g =>
                {
                    var first = g.First();
                    var ssid  = g.Key;
                    var bssList = bssMap.TryGetValue(ssid, out var bl) ? bl
                                : Array.Empty<BssInfo>();
                    int ch  = bssList.Length > 0 ? bssList[0].Channel  : 0;
                    int rssi = bssList.Length > 0 ? bssList[0].Rssi    : 0;
                    int freq = bssList.Length > 0 ? bssList[0].FrequencyMhz : 0;
                    var phy  = bssList.Length > 0 ? bssList[0].Phy     : PhyType.Unknown;

                    return new WifiNetwork
                    {
                        Ssid         = ssid,
                        BssEntries   = bssList,
                        SignalQuality = (int)first.SignalQuality,
                        Rssi         = rssi != 0 ? rssi : null,
                        Auth         = MapAuth(first.AuthAlgorithm),
                        Cipher       = MapCipher(first.CipherAlgorithm),
                        Band         = FreqToBand(freq > 0 ? freq : ChannelToFreq(ch)),
                        Channel      = ch,
                        Phy          = phy,
                        FrequencyMhz = freq > 0 ? freq : null,
                        HasProfile   = !string.IsNullOrEmpty(first.ProfileName),
                        ProfileName  = string.IsNullOrEmpty(first.ProfileName)
                                       ? null : first.ProfileName,
                    };
                })
                .OrderByDescending(n => n.IsConnected)
                .ThenByDescending(n => n.SignalQuality)
                .ToList();

            // 接続中 SSID をマーク
            string? conn = GetConnectedSsid(adapterId);
            var marked = networks
                .Select(n => n with { IsConnected = n.Ssid == conn })
                .ToList();

            // 生 IE が供給される環境では詳細解析で強化 (Country/TPC/BSS Load/FT/MDID 等)。
            // 供給源が無い (Null プロバイダ) 場合は marked をそのまま返す。
            try
            {
                var rawBeacons = _ieProvider.GetRawBeacons(adapterId);
                return _enrichment.Enrich(marked, rawBeacons);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Beacon IE enrichment skipped");
                return marked;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "EnumerateAvailableNetworks failed");
            return Array.Empty<WifiNetwork>();
        }
    }

    private Dictionary<string, BssInfo[]> BuildBssMap(Guid adapterId)
    {
        var map = new Dictionary<string, List<BssInfo>>(StringComparer.Ordinal);
        try
        {
            foreach (var bss in NativeWifi.EnumerateBssNetworks()
                         .Where(b => b.Interface.Id == adapterId))
            {
                var ssid = bss.Ssid.ToString();
                if (string.IsNullOrWhiteSpace(ssid)) continue;

                if (!map.TryGetValue(ssid, out var list))
                    map[ssid] = list = new();

                int freqMhz = bss.Band.HasValue
                    ? (int)(bss.Band.Value / 1000)   // kHz → MHz
                    : ChannelToFreq(bss.Channel);

                list.Add(new BssInfo
                {
                    Bssid        = bss.Bssid.ToString(),
                    Rssi         = bss.Rssi,
                    Channel      = bss.Channel,
                    FrequencyMhz = freqMhz,
                    Phy          = MapPhy(bss.PhyType),
                    ChannelWidth = MapWidth(bss.Bandwidth),
                });
            }
        }
        catch (Exception ex) { _log.LogDebug(ex, "EnumerateBssNetworks skipped"); }

        return map.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    // ── Profile ──────────────────────────────────────────────────────
    public Task<bool> RegisterProfileAsync(Guid adapterId, string profileXml,
        bool overwrite, CancellationToken ct = default)
    {
        try
        {
            return Task.FromResult(
                NativeWifi.SetProfile(adapterId, ProfileType.AllUser,
                    profileXml, null, overwrite));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SetProfile failed");
            return Task.FromResult(false);
        }
    }

    // ── Connect ──────────────────────────────────────────────────────
    public async Task<ConnectionResult> ConnectAsync(
        Guid adapterId, string profileName, string ssid,
        TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            using var waiter = new ConnectionWaiter(adapterId, _log);
            bool req = NativeWifi.ConnectNetwork(adapterId, profileName, BssType.Any);
            if (!req) return ConnectionResult.Fail(ConnectionFailure.ProfileRejected);

            var outcome = await waiter.WaitAsync(timeout, ct);
            if (outcome != ConnectionOutcome.Connected)
                return ConnectionResult.Fail(outcome switch
                {
                    ConnectionOutcome.BadCredentials => ConnectionFailure.BadCredentials,
                    ConnectionOutcome.Timeout        => ConnectionFailure.Timeout,
                    ConnectionOutcome.Cancelled      => ConnectionFailure.Cancelled,
                    _ => ConnectionFailure.Unknown
                });

            var conn = await _connectivity.CheckAsync(ct);
            return ConnectionResult.Ok(ssid, conn.HasInternet, conn.CaptivePortalDetected);
        }
        catch (OperationCanceledException)
            { return ConnectionResult.Fail(ConnectionFailure.Cancelled); }
        catch (UnauthorizedAccessException)
            { return ConnectionResult.Fail(ConnectionFailure.InsufficientPrivilege); }
        catch (Exception ex)
        {
            _log.LogError(ex, "ConnectAsync failed");
            return ConnectionResult.Fail(ConnectionFailure.OsError);
        }
    }

    // ── Misc ─────────────────────────────────────────────────────────
    public Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default)
        => Task.FromResult(NativeWifi.DisconnectNetwork(adapterId));

    public Task<bool> DeleteProfileAsync(Guid adapterId, string profileName,
        CancellationToken ct = default)
        => Task.FromResult(NativeWifi.DeleteProfile(adapterId, profileName));

    public Task<IReadOnlyList<string>> ListProfilesAsync(Guid adapterId,
        CancellationToken ct = default)
    {
        var list = NativeWifi.EnumerateProfiles()
            .Where(p => p.Interface.Id == adapterId)
            .Select(p => p.Name).ToList();
        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    // ── Events ───────────────────────────────────────────────────────
    public async IAsyncEnumerable<WifiEvent> SubscribeEventsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var ch = Channel.CreateUnbounded<WifiEvent>();

        void OnChanged(object? s, NetworkStateChangedEventArgs e)
        {
            ch.Writer.TryWrite(new WifiEvent(
                e.InterfaceId,
                MapEventType(e.State),
                e.Ssid,
                DateTimeOffset.UtcNow));
        }

        // ManagedNativeWifi が NetworkStateChanged を公開している場合のみ購読
        // (バージョンによりイベント名が異なるため型で安全に判定)
        NetworkStateChangedEventHandlerBridge.Subscribe(OnChanged, _log);
        try
        {
            await foreach (var ev in ch.Reader.ReadAllAsync(ct))
                yield return ev;
        }
        finally
        {
            NetworkStateChangedEventHandlerBridge.Unsubscribe(OnChanged);
            ch.Writer.TryComplete();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────
    private string? GetConnectedSsid(Guid adapterId)
    {
        try
        {
            return NativeWifi.EnumerateConnectedNetworks()
                .FirstOrDefault(n => n.Interface.Id == adapterId)
                ?.Ssid.ToString();
        }
        catch (Exception ex) { _log.LogDebug(ex, "GetConnectedSsid failed for adapter {Id}", adapterId); return null; }
    }

    private static AdapterState MapState(InterfaceState s) => s switch
    {
        InterfaceState.Connected     => AdapterState.Connected,
        InterfaceState.Disconnecting => AdapterState.Disconnecting,
        InterfaceState.Disconnected  => AdapterState.Disconnected,
        InterfaceState.Associating   => AdapterState.Associating,
        InterfaceState.Discovering   => AdapterState.Discovering,
        InterfaceState.Authenticating=> AdapterState.Authenticating,
        _                            => AdapterState.NotReady,
    };

    private static AuthMethod MapAuth(AuthAlgorithm a) => a switch
    {
        AuthAlgorithm.Open       => AuthMethod.Open,
        AuthAlgorithm.RsnaPsk    => AuthMethod.WPA2PSK,
        AuthAlgorithm.WpaPsk     => AuthMethod.WPAPSK,
        AuthAlgorithm.Rsna       => AuthMethod.WPA2Enterprise,
        AuthAlgorithm.Wpa        => AuthMethod.WPA2Enterprise,
        AuthAlgorithm.Wpa3Sae    => AuthMethod.WPA3SAE,
        AuthAlgorithm.Owe        => AuthMethod.OWE,
        AuthAlgorithm.Wpa3Enterprise192 => AuthMethod.WPA3Enterprise192,
        _ => AuthMethod.Open,
    };

    private static CipherType MapCipher(CipherAlgorithm c) => c switch
    {
        CipherAlgorithm.Ccmp    => CipherType.AES,
        CipherAlgorithm.Tkip    => CipherType.TKIP,
        CipherAlgorithm.Wep     => CipherType.WEP,
        CipherAlgorithm.Gcmp256 => CipherType.GCMP256,
        CipherAlgorithm.None    => CipherType.None,
        _ => CipherType.AES,
    };

    private static PhyType MapPhy(PhyType_ p) => p switch
    {
        PhyType_.B     => PhyType.Dot11b,
        PhyType_.A     => PhyType.Dot11a,
        PhyType_.G     => PhyType.Dot11g,
        PhyType_.N     => PhyType.Dot11n,
        PhyType_.Ac    => PhyType.Dot11ac,
        PhyType_.Ax    => PhyType.Dot11ax,
        PhyType_.Be    => PhyType.Dot11be,
        _ => PhyType.Unknown,
    };

    private static int MapWidth(ChannelBandwidth? bw) => bw switch
    {
        ChannelBandwidth.Width20    => 20,
        ChannelBandwidth.Width40    => 40,
        ChannelBandwidth.Width80    => 80,
        ChannelBandwidth.Width80p80 => 80,
        ChannelBandwidth.Width160   => 160,
        ChannelBandwidth.Width320   => 320,
        _ => 0,
    };

    private static WifiBand FreqToBand(int mhz) =>
        mhz >= 5925 ? WifiBand.Band6GHz :
        mhz >= 5000 ? WifiBand.Band5GHz :
        mhz >= 2400 ? WifiBand.Band2_4GHz :
        WifiBand.Unknown;

    /// <summary>
    /// チャンネル番号から中心周波数(MHz)を推定する **フォールバック**。
    /// ドライバーが実周波数を報告した場合は常にそちらを優先する(呼び出し側参照)。
    /// 注意: 6GHz(802.11ax 6E)はチャンネル番号 1〜233 を再利用するため、
    /// 2.4GHz(1〜14)/5GHz(32〜177)と数値が重複し、チャンネル番号のみでは
    /// バンドを一意に判別できない。ここでは普及度の高い 2.4GHz / 5GHz を優先し、
    /// どの帯域とも重複しない 178〜233 のみ 6GHz と確定する。
    /// 15〜31 のような無効チャンネルは 0(不明)を返す。
    /// </summary>
    private static int ChannelToFreq(int ch)
    {
        if (ch == 14)               return 2484;                 // 2.4GHz ch14 (日本)
        if (ch >= 1   && ch <= 13)  return 2412 + (ch - 1) * 5;  // 2.4GHz
        if (ch >= 32  && ch <= 177) return 5000 + ch * 5;        // 5GHz UNII
        if (ch >= 178 && ch <= 233) return 5950 + ch * 5;        // 6GHz 6E (非重複域のみ)
        return 0;                                                // 不明 / 無効チャンネル
    }

    private static WifiEventType MapEventType(string? s) => (s ?? "").ToLowerInvariant() switch
    {
        "connected"    => WifiEventType.Connected,
        "disconnected" => WifiEventType.Disconnected,
        "associating"  => WifiEventType.Connecting,
        "discovering"  => WifiEventType.Connecting,
        _              => WifiEventType.ScanComplete,
    };
}
