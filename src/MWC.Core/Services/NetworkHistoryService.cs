using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MWC.Core.Services;

/// <summary>
/// ネットワーク接続履歴を管理する。
/// Apple iOS の "最近接続したネットワーク" に相当。
///
/// 保存: %LocalAppData%/MWC/history.json
/// 最大: 500件 (90日分を収容)
/// 用途: JumpList、最近使ったネットワーク表示、接続優先度
/// </summary>
public sealed class NetworkHistoryService
{
    private const int MaxEntries = 500;       // 90日分を収容
    private const int RetentionDays = 90;
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MWC", "history.json");

    private readonly List<ConnectionHistoryEntry> _entries;
    // _entries 保護用。net9.0 / netstandard2.0 双方でビルドできるよう object lock を使用
    // (System.Threading.Lock は net9.0 専用のため netstandard2.0 でビルド不能)。
    private readonly object _lock = new();
    // ファイル書き込みの直列化用。_lock とは分離し、ディスク I/O 中に
    // 読み取り(_lock)をブロックしないようにする。
    private readonly object _saveLock = new();

    /// <summary>コンストラクタ。永続化ファイルがあれば読み込む。</summary>
    public NetworkHistoryService()
    {
        _entries = Load();
    }

    /// <summary>接続試行を履歴に記録する。成功 / 失敗どちらも保存。</summary>
    public void RecordConnection(string ssid, bool success)
    {
        List<ConnectionHistoryEntry> snapshot;
        lock (_lock)
        {
            var existing = _entries.FirstOrDefault(e => e.Ssid == ssid);
            if (existing is not null)
            {
                _entries.Remove(existing);
                _entries.Insert(0, existing with
                {
                    LastConnected = DateTimeOffset.UtcNow,
                    ConnectCount  = existing.ConnectCount + (success ? 1 : 0),
                    FailCount     = existing.FailCount    + (success ? 0 : 1)
                });
            }
            else
            {
                _entries.Insert(0, new ConnectionHistoryEntry(
                    Ssid:          ssid,
                    LastConnected: DateTimeOffset.UtcNow,
                    ConnectCount:  success ? 1 : 0,
                    FailCount:     success ? 0 : 1));
            }

            // 90日超のエントリを自動削除
            var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
            _entries.RemoveAll(e => e.LastConnected < cutoff);

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);

            snapshot = new List<ConnectionHistoryEntry>(_entries);
        }
        Save(snapshot);
    }

    /// <summary>直近 n 件の接続履歴を返す。</summary>
    public IReadOnlyList<ConnectionHistoryEntry> GetRecent(int n = 10)
    {
        lock (_lock)
        {
            return _entries.Take(n).ToList();
        }
    }

    /// <summary>直近 n 件の SSID 一覧を返す (JumpList 等に利用)。</summary>
    public IReadOnlyList<string> GetRecentSsids(int n = 10)
    {
        lock (_lock) { return _entries.Take(n).Select(e => e.Ssid).ToList(); }
    }

    /// <summary>指定 SSID の履歴エントリを取得する。存在しなければ null。</summary>
    public ConnectionHistoryEntry? GetEntry(string ssid)
    {
        lock (_lock) { return _entries.FirstOrDefault(e => e.Ssid == ssid); }
    }


    /// <summary>指定日数分の接続統計を返す</summary>
    public NetworkStatsSummary GetStats(int days = 30)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        List<ConnectionHistoryEntry> recent;
        lock (_lock) { recent = _entries.Where(e => e.LastConnected >= since).ToList(); }
        return new NetworkStatsSummary(
            Period:        TimeSpan.FromDays(days),
            TotalConnects: recent.Sum(e => e.ConnectCount),
            TotalFails:    recent.Sum(e => e.FailCount),
            UniqueNetworks: recent.Count,
            TopSsid:       recent.OrderByDescending(e => e.ConnectCount).FirstOrDefault()?.Ssid);
    }

    /// <summary>最も頻繁に接続するSSID上位N件</summary>
    public IReadOnlyList<string> GetFrequentSsids(int n = 5)
    {
        lock (_lock)
        {
            return _entries
                .OrderByDescending(e => e.ConnectCount)
                .Take(n)
                .Select(e => e.Ssid)
                .ToList();
        }
    }

    /// <summary>履歴全件(フィルタなし)</summary>
    public IReadOnlyList<ConnectionHistoryEntry> GetAll()
    {
        lock (_lock) { return _entries.ToList().AsReadOnly(); }
    }

    /// <summary>保存済みエントリ数</summary>
    public int Count { get { lock (_lock) { return _entries.Count; } } }

    public void Forget(string ssid)
    {
        List<ConnectionHistoryEntry> snapshot;
        lock (_lock) { _entries.RemoveAll(e => e.Ssid == ssid); snapshot = new List<ConnectionHistoryEntry>(_entries); }
        Save(snapshot);
    }

    public void ClearAll()
    {
        List<ConnectionHistoryEntry> snapshot;
        lock (_lock) { _entries.Clear(); snapshot = new List<ConnectionHistoryEntry>(_entries); }
        Save(snapshot);
    }

    private List<ConnectionHistoryEntry> Load()
    {
        if (!File.Exists(HistoryPath)) return new();
        try
        {
            var json = File.ReadAllText(HistoryPath);
            return JsonSerializer.Deserialize<List<ConnectionHistoryEntry>>(json) ?? new();
        }
        catch (JsonException)
        {
            // 破損ファイルは黙って上書きせず .corrupt に退避(復旧/調査可能にする)。
            try { File.Move(HistoryPath, HistoryPath + ".corrupt", overwrite: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return new();
        }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }

    // スナップショットをディスクへ書き込む。_lock の外で呼び、I/O 中に
    // 読み取りをブロックしない。_saveLock で書き込み同士のみ直列化する。
    private void Save(List<ConnectionHistoryEntry> snapshot)
    {
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
                // 一時ファイルへ書いてから置換し、書き込み中クラッシュでの破損を防ぐ。
                var tmp = HistoryPath + ".tmp";
                File.WriteAllText(tmp,
                    JsonSerializer.Serialize(snapshot,
                        new JsonSerializerOptions { WriteIndented = false }));
                File.Move(tmp, HistoryPath, overwrite: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

public sealed record ConnectionHistoryEntry(
    string         Ssid,
    DateTimeOffset LastConnected,
    int            ConnectCount,
    int            FailCount)
{
    public string LastConnectedLabel
    {
        get
        {
            var diff = DateTimeOffset.UtcNow - LastConnected;
            if (diff.TotalMinutes < 1)  return "たった今";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}分前";
            if (diff.TotalHours   < 24) return $"{(int)diff.TotalHours}時間前";
            if (diff.TotalDays    < 7)  return $"{(int)diff.TotalDays}日前";
            return LastConnected.LocalDateTime.ToString("M/d");
        }
    }
    public bool HasFailures => FailCount > 0;
}

/// <summary>ネットワーク接続統計サマリ</summary>
public sealed record NetworkStatsSummary(
    TimeSpan Period,
    int      TotalConnects,
    int      TotalFails,
    int      UniqueNetworks,
    string?  TopSsid)
{
    public double SuccessRate =>
        TotalConnects + TotalFails > 0
            ? (double)TotalConnects / (TotalConnects + TotalFails)
            : 1.0;
}
