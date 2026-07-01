using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// 802.1X (Enterprise) 認証の成功率を EAP タイプ別に集計するサービス。
///
/// ROADMAP.md 「802.1X 自動テスト(EAP 認証成功率を計測)」の計測基盤。
/// 本サービスは新規に接続を試みたり EAP を再テストしたりしない — 既存の接続実行
/// フロー(<see cref="ConnectionExecutor"/>)が Enterprise ネットワークへ接続を試みた
/// 際の成否を SSID × EapType 単位で集計するのみ。
///
/// 用途: 「このキャンパスの PEAP は 95% 成功するが、あの eduroam の EAP-TLS は
/// 60% しか成功しない」といった、EAP タイプ起因の接続不安定性を可視化する。
/// パスフレーズ等の機密情報は一切保持しない(SSID + EapType + 成否カウントのみ)。
///
/// 保存: %LocalAppData%/MWC/eap-stats.json
/// </summary>
public sealed class EapAuthStatsService
{
    private const int MaxEntries = 200; // SSID × EapType の組み合わせ上限

    private static readonly string StatsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MWC", "eap-stats.json");

    private readonly List<EapAuthStat> _entries;
    private readonly ILogger<EapAuthStatsService> _log;
    // _entries 保護用。RecordAttempt は ConnectionExecutor から、GetAll/GetStat は
    // UI/CLI から並行して呼ばれうるため NetworkHistoryService と同じロック方針を踏襲する。
    private readonly object _lock = new();
    // ファイル書き込み直列化用。_lock とは分離し、I/O 中に読み取りをブロックしない。
    private readonly object _saveLock = new();

    /// <summary>コンストラクタ。永続化ファイルがあれば読み込む。
    /// logger 省略時は NullLogger を使う(テスト容易性のため)。</summary>
    public EapAuthStatsService(ILogger<EapAuthStatsService>? log = null)
    {
        _log = log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EapAuthStatsService>.Instance;
        _entries = Load();
    }

    /// <summary>
    /// Enterprise 接続の試行結果を記録する。<paramref name="ssid"/> + <paramref name="eapType"/>
    /// の組み合わせ単位で成功/失敗カウントを積み上げる。
    /// </summary>
    public void RecordAttempt(string ssid, EapType eapType, bool success)
    {
        List<EapAuthStat> snapshot;
        lock (_lock)
        {
            var existing = _entries.FirstOrDefault(e => e.Ssid == ssid && e.EapType == eapType);
            if (existing is not null)
            {
                _entries.Remove(existing);
                _entries.Insert(0, existing with
                {
                    LastAttempt  = DateTimeOffset.UtcNow,
                    SuccessCount = existing.SuccessCount + (success ? 1 : 0),
                    FailCount    = existing.FailCount    + (success ? 0 : 1)
                });
            }
            else
            {
                _entries.Insert(0, new EapAuthStat(
                    Ssid:         ssid,
                    EapType:      eapType,
                    SuccessCount: success ? 1 : 0,
                    FailCount:    success ? 0 : 1,
                    LastAttempt:  DateTimeOffset.UtcNow));
            }

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);

            snapshot = new List<EapAuthStat>(_entries);
        }
        Save(snapshot);
    }

    /// <summary>全 SSID × EapType の統計一覧(直近更新順)。</summary>
    public IReadOnlyList<EapAuthStat> GetAll()
    {
        lock (_lock) { return _entries.ToList(); }
    }

    /// <summary>指定 SSID + EapType の統計。未記録なら null。</summary>
    public EapAuthStat? GetStat(string ssid, EapType eapType)
    {
        lock (_lock) { return _entries.FirstOrDefault(e => e.Ssid == ssid && e.EapType == eapType); }
    }

    /// <summary>全記録を消去する。</summary>
    public void ClearAll()
    {
        List<EapAuthStat> snapshot;
        lock (_lock) { _entries.Clear(); snapshot = new List<EapAuthStat>(_entries); }
        Save(snapshot);
    }

    private List<EapAuthStat> Load()
    {
        if (!File.Exists(StatsPath)) return new();
        try
        {
            var json = File.ReadAllText(StatsPath);
            return JsonSerializer.Deserialize<List<EapAuthStat>>(json) ?? new();
        }
        catch (JsonException ex)
        {
            // 破損ファイルは黙って上書きせず .corrupt へ退避(復旧/調査可能にする)。
            _log.LogWarning(ex, "EAP stats file corrupted, moved to {Path}.corrupt", StatsPath);
            try { File.Move(StatsPath, StatsPath + ".corrupt", overwrite: true); }
            catch (IOException moveEx)             { _log.LogDebug(moveEx, "Could not move corrupted EAP stats file to .corrupt"); }
            catch (UnauthorizedAccessException moveEx) { _log.LogDebug(moveEx, "Could not move corrupted EAP stats file to .corrupt"); }
            return new();
        }
        catch (IOException ex) { _log.LogWarning(ex, "Failed to read EAP stats file {Path}", StatsPath); return new(); }
        catch (UnauthorizedAccessException ex) { _log.LogWarning(ex, "Access denied reading EAP stats file {Path}", StatsPath); return new(); }
    }

    // スナップショットをディスクへ書き込む。_lock の外で呼び、I/O 中に
    // 読み取りをブロックしない。_saveLock で書き込み同士のみ直列化する。
    private void Save(List<EapAuthStat> snapshot)
    {
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatsPath)!);
                var tmp = StatsPath + ".tmp";
                File.WriteAllText(tmp,
                    JsonSerializer.Serialize(snapshot,
                        new JsonSerializerOptions { WriteIndented = false }));
                File.Move(tmp, StatsPath, overwrite: true);
            }
            catch (IOException ex) { _log.LogWarning(ex, "Failed to save EAP stats file {Path}", StatsPath); }
            catch (UnauthorizedAccessException ex) { _log.LogWarning(ex, "Access denied saving EAP stats file {Path}", StatsPath); }
        }
    }
}

/// <summary>SSID × EapType 単位の 802.1X 認証統計。</summary>
public sealed record EapAuthStat(
    string         Ssid,
    EapType        EapType,
    int            SuccessCount,
    int            FailCount,
    DateTimeOffset LastAttempt)
{
    /// <summary>成功率 (0.0-1.0)。記録がまだ無ければ 1.0(楽観値、UI 側で件数と併記すること)。</summary>
    public double SuccessRate =>
        SuccessCount + FailCount > 0
            ? (double)SuccessCount / (SuccessCount + FailCount)
            : 1.0;

    public int TotalAttempts => SuccessCount + FailCount;
}
