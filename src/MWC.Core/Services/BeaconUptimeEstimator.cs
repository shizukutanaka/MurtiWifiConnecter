using System;

namespace MWC.Core.Services;

/// <summary>
/// ビーコン TSF (Timing Synchronization Function) タイムスタンプから
/// AP の概算稼働時間を推定する (lswifi の uptime 推定相当)。
///
/// ビーコンフレームの先頭 8 バイトは 64bit TSF タイマー (マイクロ秒単位、
/// AP 起動時に 0 から開始しリトルエンディアンで増加)。これを稼働時間とみなす。
///
/// 注意:
///   - TSF は約 58 万年で一巡するため実運用ではラップしない。
///   - AP によっては TSF をリセットしない/非標準実装があり、過大値はクランプする。
///   - 短い稼働時間は再起動直後 = ファーム更新/不安定の兆候として有用。
/// </summary>
public static class BeaconUptimeEstimator
{
    /// <summary>非現実的な値を弾く上限 (10 年)。</summary>
    private static readonly TimeSpan MaxPlausibleUptime = TimeSpan.FromDays(365 * 10);

    /// <summary>64bit TSF 値 (マイクロ秒) から稼働時間を算出する。</summary>
    public static TimeSpan? FromTsf(ulong tsfMicroseconds)
    {
        if (tsfMicroseconds == 0) return null;
        // マイクロ秒 → TimeSpan (100ns ticks = µs × 10)
        double micros = tsfMicroseconds;
        if (micros / 1_000_000.0 > MaxPlausibleUptime.TotalSeconds) return null;
        return TimeSpan.FromMilliseconds(micros / 1000.0);
    }

    /// <summary>
    /// ビーコンフレーム本体先頭の 8 バイト (リトルエンディアン TSF) から推定する。
    /// 8 バイト未満なら null。
    /// </summary>
    public static TimeSpan? FromBeaconTimestamp(ReadOnlySpan<byte> beaconTimestamp8)
    {
        if (beaconTimestamp8.Length < 8) return null;
        ulong tsf = 0;
        for (int i = 0; i < 8; i++)
            tsf |= (ulong)beaconTimestamp8[i] << (8 * i);
        return FromTsf(tsf);
    }

    /// <summary>稼働時間を人間語のラベルにする (例: "3日 4時間")。</summary>
    public static string ToLabel(TimeSpan? uptime)
    {
        if (uptime is not { } t) return "Unknown";
        if (t.TotalDays >= 1)   return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1)  return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m";
        return $"{(int)t.TotalSeconds}s";
    }

    /// <summary>再起動直後 (5 分未満) かどうか — 不安定/更新直後の兆候。</summary>
    public static bool IsRecentlyRebooted(TimeSpan? uptime)
        => uptime is { } t && t < TimeSpan.FromMinutes(5);
}
