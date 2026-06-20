using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// Wi-Fi Direct (P2P) 接続サービス。
///
/// Wi-Fi Direct は IEEE 802.11 P2P 拡張仕様。AP なしでデバイス間直接接続。
/// Windows では Windows.Devices.WiFiDirect API (UWP) を使用する。
///
/// アーキテクチャ:
///   IWifiDirectService  ←── このインターフェースを Platform が実装
///     ↓
///   WifiDirectService   ← 高レベルロジック(ペアリング・グループ管理)
///     ↓
///   WindowsWifiDirectAdapter (MWC.Platform.Windows で実装)
///
/// 典型的な使用フロー:
///   1. StartDiscoveryAsync() でデバイス探索
///   2. ConnectAsync(device) でペアリング+接続
///   3. GetConnectedDevices() で接続中デバイス管理
///   4. StopDiscoveryAsync() / DisconnectAsync() でクリーンアップ
/// </summary>
public sealed class WifiDirectService
{
    private readonly IWifiDirectAdapter        _adapter;
    private readonly List<WifiDirectDevice>    _discovered = new();
    private readonly List<WifiDirectDevice>    _connected  = new();
    // 単一ロックで _discovered / _connected / _discovering を保護する。
    // OnDeviceDiscovered は adapter のバックグラウンドスレッドから呼ばれる可能性がある。
    private readonly object                    _stateLock  = new();
    private volatile bool                      _discovering;

    /// <summary>コンストラクタ。プラットフォーム実装アダプターを注入する。</summary>
    public WifiDirectService(IWifiDirectAdapter adapter)
        => _adapter = adapter;

    // ── Discovery ──────────────────────────────────────────────────

    public event Action<WifiDirectDevice>? DeviceDiscovered;
    public event Action<WifiDirectDevice>? DeviceLost;
    public event Action<WifiDirectDevice>? ConnectionStateChanged;

    public bool IsDiscovering => _discovering;

    /// <summary>
    /// P2P デバイス探索を開始する。
    /// 発見したデバイスは DeviceDiscovered イベントで通知。
    /// </summary>
    public async Task StartDiscoveryAsync(
        WifiDirectDiscoveryOptions? options = null,
        CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            if (_discovering) return;
            _discovering = true;
            _discovered.Clear();
        }

        try
        {
            await _adapter.StartDiscoveryAsync(
                OnDeviceDiscovered,
                options ?? WifiDirectDiscoveryOptions.Default,
                ct).ConfigureAwait(false);
        }
        catch
        {
            _discovering = false;
            throw;
        }
    }

    /// <summary>アダプターからの発見通知を受けて重複排除のうえ公開イベントへ転送する。</summary>
    private void OnDeviceDiscovered(WifiDirectDevice device)
    {
        bool added;
        lock (_stateLock)
        {
            if (_discovered.Exists(d => d.DeviceId == device.DeviceId)) return;
            _discovered.Add(device);
            added = true;
        }
        if (added) DeviceDiscovered?.Invoke(device);
    }

    /// <summary>探索を停止する。</summary>
    public async Task StopDiscoveryAsync()
    {
        if (!_discovering) return;
        await _adapter.StopDiscoveryAsync().ConfigureAwait(false);
        _discovering = false;
    }

    /// <summary>発見済みデバイス一覧(スナップショット)</summary>
    public IReadOnlyList<WifiDirectDevice> DiscoveredDevices
    {
        get { lock (_stateLock) { return _discovered.ToList(); } }
    }

    // ── Connection ────────────────────────────────────────────────

    /// <summary>指定デバイスと Wi-Fi Direct P2P 接続する。GO ネゴシエーションを自動実行。</summary>
    public async Task<WifiDirectConnectionResult> ConnectAsync(
        WifiDirectDevice device,
        WifiDirectConnectionOptions? options = null,
        CancellationToken ct = default)
    {
        var opts = options ?? WifiDirectConnectionOptions.Default;
        var result = await _adapter.ConnectAsync(device, opts, ct).ConfigureAwait(false);

        if (result.Success)
        {
            lock (_stateLock) { _connected.Add(device with { State = WifiDirectDeviceState.Connected }); }
            ConnectionStateChanged?.Invoke(device);
        }
        return result;
    }

    /// <summary>デバイスとの接続を切断する。</summary>
    public async Task DisconnectAsync(WifiDirectDevice device, CancellationToken ct = default)
    {
        await _adapter.DisconnectAsync(device, ct).ConfigureAwait(false);
        lock (_stateLock) { _connected.RemoveAll(d => d.DeviceId == device.DeviceId); }
        ConnectionStateChanged?.Invoke(device with { State = WifiDirectDeviceState.Disconnected });
    }

    /// <summary>現在接続中のデバイス一覧(スナップショット)</summary>
    public IReadOnlyList<WifiDirectDevice> ConnectedDevices
    {
        get { lock (_stateLock) { return _connected.ToList(); } }
    }

    // ── Group Owner Mode (ソフト AP) ─────────────────────────────

    /// <summary>本デバイスを Group Owner として動作させ、他デバイスの接続を受け付ける。スマートフォン等との直接ファイル共有に使用。</summary>
    public async Task<WifiDirectGroupOwnerResult> StartGroupOwnerAsync(
        string? groupSsid = null,
        string? passphrase = null,
        CancellationToken ct = default)
    {
        var ssid = groupSsid ?? $"DIRECT-{Guid.NewGuid():N}"[..15];
        return await _adapter.StartGroupOwnerAsync(ssid, passphrase, ct).ConfigureAwait(false);
    }

    /// <summary>Group Owner モードを終了する。</summary>
    public async Task StopGroupOwnerAsync()
        => await _adapter.StopGroupOwnerAsync().ConfigureAwait(false);
}

// ══ インターフェース ═══════════════════════════════════════════════════

/// <summary>プラットフォーム実装が提供すべきアダプター</summary>
public interface IWifiDirectAdapter
{
    Task StartDiscoveryAsync(Action<WifiDirectDevice> onDiscovered,
        WifiDirectDiscoveryOptions options, CancellationToken ct);
    Task StopDiscoveryAsync();
    Task<WifiDirectConnectionResult> ConnectAsync(
        WifiDirectDevice device, WifiDirectConnectionOptions options, CancellationToken ct);
    Task DisconnectAsync(WifiDirectDevice device, CancellationToken ct);
    Task<WifiDirectGroupOwnerResult> StartGroupOwnerAsync(
        string ssid, string? passphrase, CancellationToken ct);
    Task StopGroupOwnerAsync();
}

// ══ データ型 ════════════════════════════════════════════════════════════

/// <summary>Wi-Fi Direct デバイス情報</summary>
public sealed record WifiDirectDevice(
    string                  DeviceId,
    string                  DeviceName,
    WifiDirectDeviceType    Type,
    int                     Rssi,
    WifiDirectDeviceState   State = WifiDirectDeviceState.Available,
    string?                 IpAddress = null);

public enum WifiDirectDeviceType   { Phone, PC, Printer, Camera, TV, Unknown }
public enum WifiDirectDeviceState  { Available, Pairing, Connected, Disconnected }

/// <summary>探索オプション</summary>
public sealed record WifiDirectDiscoveryOptions(
    TimeSpan Timeout,
    bool     ScanAll)
{
    public static WifiDirectDiscoveryOptions Default =>
        new(TimeSpan.FromSeconds(30), ScanAll: false);
}

/// <summary>接続オプション</summary>
public sealed record WifiDirectConnectionOptions(
    WifiDirectPairingMethod Method,
    string?                 Pin)
{
    public static WifiDirectConnectionOptions Default =>
        new(WifiDirectPairingMethod.PushButton, null);
}

public enum WifiDirectPairingMethod { PushButton, Pin, None }

/// <summary>接続結果</summary>
public sealed record WifiDirectConnectionResult(
    bool    Success,
    string? ErrorMessage,
    string? LocalIp,
    string? RemoteIp);

/// <summary>Group Owner 起動結果</summary>
public sealed record WifiDirectGroupOwnerResult(
    bool   Success,
    string Ssid,
    string Passphrase,
    string LocalIp,
    int    Port = 0);
