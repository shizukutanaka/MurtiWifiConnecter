using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MWC.Core.Services;

/// <summary>
/// 接続中ネットワークの品質計測。
/// Apple iOS "Wi-Fi Recommendations" / macOS "Wi-Fi Diagnostics" に相当。
///
/// 計測項目:
///   - Gateway Ping レイテンシ (RTT ms)
///   - DNS 解決時間
///   - パケットロス率 (5回Ping)
///
/// 設計: 軽量 — プロセス不使用、System.Net.NetworkInformation.Ping 使用。
/// </summary>
public sealed class NetworkQualityService
{
    private static readonly string[] DnsTargets = { "8.8.8.8", "1.1.1.1" };
    private readonly ConcurrentDictionary<string, NetworkQualityResult> _cache = new();

    /// <summary>指定ホストへの品質計測(非同期)</summary>
    public async Task<NetworkQualityResult> MeasureAsync(
        string host = "8.8.8.8",
        int samples = 5,
        CancellationToken ct = default)
    {
        var hits = new System.Collections.Generic.List<int>(samples);
        int lost = 0;
        using var ping = new Ping();

        for (int i = 0; i < samples; i++)
        {
            // キャンセルは一貫して OperationCanceledException で伝播させる。
            // (旧実装は break で部分結果を返し、未計測分を「ロスト」に数えて
            //  パケットロス率を水増しした誤った計測値を正常結果として返していた。
            //  下の Task.Delay(ct) は OCE を投げるため、ループ先頭も throw に揃える。)
            ct.ThrowIfCancellationRequested();
            try
            {
                var r = await ping.SendPingAsync(host, 1500).ConfigureAwait(false);
                if (r.Status == IPStatus.Success)
                    hits.Add((int)r.RoundtripTime);
                else
                    lost++;
            }
            catch (OperationCanceledException) { throw; }
            catch { lost++; }
            if (i < samples - 1) await Task.Delay(200, ct).ConfigureAwait(false);
        }

        // 計測できなかった分を lost に加算
        lost += (samples - hits.Count - lost);

        int avg = hits.Count > 0 ? (int)Math.Round(hits.Average(x => (double)x)) : 999;
        int min = hits.Count > 0 ? hits.Min() : 999;
        int max = hits.Count > 0 ? hits.Max() : 999;
        double loss = (double)lost / samples * 100;

        var result = new NetworkQualityResult(
            LatencyAvgMs: avg,
            LatencyMinMs: min,
            LatencyMaxMs: max,
            PacketLossPct: loss,
            Grade: GradeFrom(avg, loss),
            MeasuredAt: DateTimeOffset.UtcNow);

        _cache[host] = result;
        return result;
    }

    public NetworkQualityResult? GetCached(string host = "8.8.8.8")
        => _cache.TryGetValue(host, out var r) ? r : null;

    /// <summary>
    /// 負荷時遅延(responsiveness / bufferbloat)を計測する。
    /// アイドル時 RTT と、<paramref name="loadGenerator"/> で輻輳を作った状態の RTT を比較し、
    /// IETF responsiveness の RPM(round-trips/分)と bufferbloat グレードを算出する。
    /// 負荷生成は呼び出し側が供給(例: 並列 HTTP ダウンロード)。null の場合は
    /// アイドルのみ計測しグレードは Unknown。
    /// 参考: IETF draft-ietf-ippm-responsiveness, Apple RPM。
    /// </summary>
    public async Task<ResponsivenessResult> MeasureResponsivenessAsync(
        string host = "8.8.8.8",
        Func<CancellationToken, Task>? loadGenerator = null,
        int samples = 5,
        CancellationToken ct = default)
    {
        // 1. アイドル時 RTT
        var idle = await MeasureAsync(host, samples, ct).ConfigureAwait(false);

        if (loadGenerator is null)
        {
            return new ResponsivenessResult(
                IdleLatencyMs:    idle.LatencyAvgMs,
                WorkingLatencyMs: idle.LatencyAvgMs,
                Rpm:              ComputeRpm(idle.LatencyAvgMs),
                Grade:            BufferbloatGrade.Unknown,
                MeasuredAt:       DateTimeOffset.UtcNow);
        }

        // 2. 負荷をかけながら RTT
        using var loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loadTask = Task.Run(() => loadGenerator(loadCts.Token), loadCts.Token);
        NetworkQualityResult working;
        try
        {
            await Task.Delay(300, ct).ConfigureAwait(false); // 負荷の立ち上がりを待つ
            working = await MeasureAsync(host, samples, ct).ConfigureAwait(false);
        }
        finally
        {
            loadCts.Cancel();
            try { await loadTask.ConfigureAwait(false); } catch { /* 負荷タスクのキャンセル/失敗は無視 */ }
        }

        return new ResponsivenessResult(
            IdleLatencyMs:    idle.LatencyAvgMs,
            WorkingLatencyMs: working.LatencyAvgMs,
            Rpm:              ComputeRpm(working.LatencyAvgMs),
            Grade:            GradeBufferbloat(idle.LatencyAvgMs, working.LatencyAvgMs),
            MeasuredAt:       DateTimeOffset.UtcNow);
    }

    /// <summary>working RTT(ms)から RPM(round-trips/分)を算出。</summary>
    public static int ComputeRpm(int workingLatencyMs)
        => workingLatencyMs <= 0 ? 0 : (int)Math.Round(60000.0 / workingLatencyMs);

    /// <summary>アイドル時と負荷時 RTT の増分から bufferbloat グレードを算出。</summary>
    public static BufferbloatGrade GradeBufferbloat(int idleLatencyMs, int workingLatencyMs)
    {
        if (workingLatencyMs >= 999 || workingLatencyMs <= 0) return BufferbloatGrade.Unknown;
        int increase = Math.Max(0, workingLatencyMs - idleLatencyMs);
        return increase switch
        {
            < 30  => BufferbloatGrade.A,
            < 60  => BufferbloatGrade.B,
            < 100 => BufferbloatGrade.C,
            < 200 => BufferbloatGrade.D,
            _     => BufferbloatGrade.F
        };
    }

    private static QualityGrade GradeFrom(int latencyMs, double lossPct)
    {
        if (lossPct >= 20 || latencyMs >= 999) return QualityGrade.Poor;
        if (latencyMs <= 20  && lossPct == 0) return QualityGrade.Excellent;
        if (latencyMs <= 50  && lossPct <  2) return QualityGrade.Good;
        if (latencyMs <= 100 && lossPct <  5) return QualityGrade.Fair;
        return QualityGrade.Poor;
    }
}

public readonly record struct NetworkQualityResult(
    int            LatencyAvgMs,
    int            LatencyMinMs,
    int            LatencyMaxMs,
    double         PacketLossPct,
    QualityGrade   Grade,
    DateTimeOffset MeasuredAt)
{
    public string GradeLabel => Grade switch
    {
        QualityGrade.Excellent => "Excellent",
        QualityGrade.Good      => "Good",
        QualityGrade.Fair      => "Fair",
        QualityGrade.Poor      => "Poor",
        _ => "Unknown"
    };

    public string LatencyLabel => LatencyAvgMs >= 999
        ? "Timeout"
        : $"{LatencyAvgMs} ms";

    public string LossLabel => $"{PacketLossPct:F0}%";
}

public enum QualityGrade { Unknown, Excellent, Good, Fair, Poor }

/// <summary>負荷時遅延(responsiveness / bufferbloat)計測結果。</summary>
public readonly record struct ResponsivenessResult(
    int              IdleLatencyMs,
    int              WorkingLatencyMs,
    int              Rpm,
    BufferbloatGrade Grade,
    DateTimeOffset   MeasuredAt)
{
    /// <summary>負荷時のレイテンシ増分(= bufferbloat)。</summary>
    public int LatencyIncreaseMs => Math.Max(0, WorkingLatencyMs - IdleLatencyMs);

    public string RpmLabel => Rpm <= 0 ? "—" : $"{Rpm} RPM";

    public string GradeLabel => Grade switch
    {
        BufferbloatGrade.A => "A (Excellent)",
        BufferbloatGrade.B => "B (Good)",
        BufferbloatGrade.C => "C (Fair)",
        BufferbloatGrade.D => "D (Needs improvement)",
        BufferbloatGrade.F => "F (Critical)",
        _                  => "—"
    };
}

/// <summary>bufferbloat(負荷時遅延)グレード。増分が小さいほど良い。</summary>
public enum BufferbloatGrade { Unknown, A, B, C, D, F }


