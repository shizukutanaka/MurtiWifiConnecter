using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MWC.Core.Services;

/// <summary>
/// 子機(無線アダプター)ごとの接続好み設定。
/// 同じPCに複数子機がある場合の「使い分け」をサポートする。
///
/// ユースケース:
///   - 子機A(内蔵): 自宅WiFi に自動接続
///   - 子機B(USB): モバイルルーター専用
///   - 子機C(USB): 5GHz帯のみ使用
///
/// 保存先: %LocalAppData%/MWC/adapters.json
/// </summary>
public sealed class AdapterPreferencesService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MWC", "adapters.json");

    private readonly Dictionary<Guid, AdapterPreferences> _store;
    // _store 保護用。AutoReconnectService(バックグラウンド)が Get/PickBestSsid で読み取る一方、
    // UI スレッドが Save するため、ロック無しでは Dictionary の並行読み書きで
    // InvalidOperationException("collection was modified") やデータ破損が起きうる。
    private readonly object _lock = new();
    // ディスク書き込み直列化用。_lock とは分離し、I/O 中に読み取りをブロックしない。
    private readonly object _saveLock = new();

    /// <summary>コンストラクタ。永続化ファイルから設定を読み込む。</summary>
    public AdapterPreferencesService()
    {
        _store = Load();
    }

    public AdapterPreferences Get(Guid adapterId)
    {
        lock (_lock)
        {
            return _store.TryGetValue(adapterId, out var p) ? p
                : new AdapterPreferences { AdapterId = adapterId };
        }
    }

    public void Save(AdapterPreferences prefs)
    {
        List<AdapterPreferences> snapshot;
        lock (_lock)
        {
            _store[prefs.AdapterId] = prefs;
            snapshot = _store.Values.ToList();
        }
        Persist(snapshot);
    }

    public IReadOnlyList<AdapterPreferences> All()
    {
        lock (_lock) { return _store.Values.ToList(); }
    }

    /// <summary>子機Aで使ったSSIDを子機Bに自動共有(任意)</summary>
    public void PinSsid(Guid adapterId, string ssid)
    {
        var p   = Get(adapterId);
        var pin = new List<string>(p.PinnedSsids);
        if (!pin.Contains(ssid))
        {
            pin.Insert(0, ssid);
            if (pin.Count > 20) pin.RemoveRange(20, pin.Count - 20);
            Save(p with { PinnedSsids = pin });
        }
    }

    public void UnpinSsid(Guid adapterId, string ssid)
    {
        var p = Get(adapterId);
        if (p.PinnedSsids.Contains(ssid))
            Save(p with { PinnedSsids = p.PinnedSsids.Where(s => s != ssid).ToList() });
    }

    public void SetAutoConnectPriority(Guid adapterId, IReadOnlyList<string> orderedSsids)
        => Save(Get(adapterId) with { AutoConnectPriority = orderedSsids.ToList() });

    public void SetBandFilter(Guid adapterId, BandPreference band)
        => Save(Get(adapterId) with { PreferredBand = band });

    public void SetEnabled(Guid adapterId, bool enabled)
        => Save(Get(adapterId) with { IsEnabled = enabled });

    public void SetLabel(Guid adapterId, string? label)
        => Save(Get(adapterId) with { CustomLabel = label });


    /// <summary>この子機で自動再接続が有効か(IsEnabled かつ PinnedSsids > 0)</summary>
    public bool IsAutoReconnectEnabled(Guid adapterId)
    {
        var p = Get(adapterId);
        return p.IsEnabled && p.PinnedSsids.Count > 0;
    }

    /// <summary>
    /// ピン留めSSID + 優先順位から、圏内の最適SSIDを選択。
    /// 候補なしなら null。
    /// </summary>
    public string? PickBestSsid(Guid adapterId, IEnumerable<string> availableSsids)
    {
        var p = Get(adapterId);
        if (!p.IsEnabled) return null;

        var available = new HashSet<string>(availableSsids);

        // 1. AutoConnectPriority (明示的に順位設定済み)
        foreach (var ssid in p.AutoConnectPriority)
            if (available.Contains(ssid)) return ssid;

        // 2. PinnedSsids (ピン留め順)
        foreach (var ssid in p.PinnedSsids)
            if (available.Contains(ssid)) return ssid;

        return null;
    }


    /// <summary>優先ネットワークリストに追加</summary>
    public void AddPreferred(Guid adapterId, string ssid)
    {
        var p = Get(adapterId);
        if (p.AutoConnectPriority.Contains(ssid)) return;
        var list = new List<string>(p.AutoConnectPriority) { ssid };
        Save(p with { AutoConnectPriority = list });
    }

    /// <summary>優先ネットワークリストから削除</summary>
    public void RemovePreferred(Guid adapterId, string ssid)
    {
        var p = Get(adapterId);
        Save(p with { AutoConnectPriority = p.AutoConnectPriority.Where(s => s != ssid).ToList() });
    }

    /// <summary>優先ネットワークの順位を上げる</summary>
    public void MoveUp(Guid adapterId, string ssid)
    {
        var p = Get(adapterId);
        var list = new List<string>(p.AutoConnectPriority);
        var idx = list.IndexOf(ssid);
        if (idx <= 0) return;
        (list[idx - 1], list[idx]) = (list[idx], list[idx - 1]);
        Save(p with { AutoConnectPriority = list });
    }

    /// <summary>優先ネットワーク一覧を取得</summary>
    public IReadOnlyList<string> GetPreferredNetworks(Guid adapterId)
        => Get(adapterId).AutoConnectPriority;

    /// <summary>自動再接続の有効/無効を設定</summary>
    public void SetAutoReconnect(Guid adapterId, bool enabled)
    {
        var p = Get(adapterId);
        // IsEnabled は子機全体の有効化、ここでは PinnedSsids の有無で判定
        // 明示的な AutoReconnect フラグが欲しい場合はレコードに追加が必要
        // 暫定: enabled=false なら PinnedSsids をクリア、true なら何もしない
        if (!enabled)
            Save(p with { AutoConnectPriority = Array.Empty<string>() });
    }

    private Dictionary<Guid, AdapterPreferences> Load()
    {
        if (!File.Exists(ConfigPath)) return new();
        try
        {
            var json = File.ReadAllText(ConfigPath);
            var list = JsonSerializer.Deserialize<List<AdapterPreferences>>(json);
            return list?.ToDictionary(p => p.AdapterId) ?? new();
        }
        catch (JsonException)
        {
            // 破損ファイルは黙って上書きせず .corrupt に退避。
            try { File.Move(ConfigPath, ConfigPath + ".corrupt", overwrite: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return new();
        }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }

    // スナップショットをディスクへ書き込む。_lock の外で呼び、I/O 中に
    // 読み取りをブロックしない。_saveLock で書き込み同士のみ直列化する。
    private void Persist(List<AdapterPreferences> snapshot)
    {
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                // 一時ファイル経由で原子的に置換し、書き込み中クラッシュでの破損を防ぐ。
                var tmp = ConfigPath + ".tmp";
                File.WriteAllText(tmp,
                    JsonSerializer.Serialize(snapshot,
                        new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, ConfigPath, overwrite: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

public sealed record AdapterPreferences
{
    public Guid              AdapterId { get; init; }
    /// <summary>ユーザー定義の表示名(例: "自宅用 USB ドングル")</summary>
    public string?           CustomLabel { get; init; }
    /// <summary>このアダプターを使用する</summary>
    public bool              IsEnabled { get; init; } = true;
    /// <summary>接続自動再接続のSSID優先順位</summary>
    public IReadOnlyList<string> AutoConnectPriority { get; init; } = Array.Empty<string>();
    /// <summary>ピン留めSSID(リスト先頭表示)</summary>
    public IReadOnlyList<string> PinnedSsids { get; init; } = Array.Empty<string>();
    /// <summary>このアダプターで使うバンド(例: 5GHz専用ドングル)</summary>
    public BandPreference    PreferredBand { get; init; } = BandPreference.Any;
}

public enum BandPreference
{
    Any,        // 制限なし
    Only2_4GHz, // 2.4GHzのみ
    Only5GHz,   // 5GHzのみ
    Only6GHz    // 6GHzのみ (Wi-Fi 6E/7)
}
