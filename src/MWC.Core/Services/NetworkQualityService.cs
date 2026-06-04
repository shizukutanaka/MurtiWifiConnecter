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
            if (ct.IsCancellationRequested) break;
            try
            {
                var r = await ping.SendPingAsync(host, 1500).ConfigureAwait(false);
                if (r.Status == IPStatus.Success)
                    hits.Add((int)r.RoundtripTime);
                else
                    lost++;
            }
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
        QualityGrade.Excellent => "優良",
        QualityGrade.Good      => "良好",
        QualityGrade.Fair      => "普通",
        QualityGrade.Poor      => "不良",
        _ => "計測中…"
    };

    public string LatencyLabel => LatencyAvgMs >= 999
        ? "タイムアウト"
        : $"{LatencyAvgMs} ms";

    public string LossLabel => $"{PacketLossPct:F0}%";
}

public enum QualityGrade { Unknown, Excellent, Good, Fair, Poor }


