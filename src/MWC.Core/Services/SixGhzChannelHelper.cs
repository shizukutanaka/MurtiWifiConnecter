using System.Collections.Generic;

namespace MWC.Core.Services;

/// <summary>
/// 6GHz 帯チャネルのユーティリティ。
///
/// IEEE 802.11ax-2021 / Wi-Fi 6E (6GHz Band) で導入された
/// Preferred Scanning Channels (PSC) を提供する。
///
/// PSC とは:
///   6GHz 帯の 59 チャネル (ch 1–233, 20MHz 単位) のうち、AP が
///   プローブ応答を確実に返すよう義務付けられた 15 チャネル。
///   クライアントは PSC のみをスキャンすることで大幅に時間短縮できる。
///   規格上 PSC は ch 5 から 32 おき: 5, 37, 69, ... (ch = 5 + 32n, n=0..14)
///
/// 参考: IEEE 802.11ax-2021 §26.17.2.3.3 (Preferred Scanning Channels)
/// </summary>
public static class SixGhzChannelHelper
{
    /// <summary>6GHz PSC チャネル番号一覧 (ch 5 から 32 おき、最大 8 チャネル ≤ 233)。</summary>
    public static IReadOnlyList<int> PreferredScanningChannels { get; } = BuildPsc();

    /// <summary>6GHz 全チャネル (ch 1, 5, 9, ... 233 — 20MHz 単位、59 チャネル)。</summary>
    public static IReadOnlyList<int> AllChannels { get; } = BuildAll();

    private static readonly HashSet<int> _pscSet = new(BuildPsc());

    /// <summary>指定チャネルが PSC かどうかを返す。O(1)。</summary>
    public static bool IsPreferredScanningChannel(int channel)
        => _pscSet.Contains(channel);

    /// <summary>チャネル番号を周波数 (MHz) に変換する (6GHz: 5950 + ch×5)。</summary>
    public static int ChannelToFreqMhz(int channel)
        => 5950 + channel * 5;

    /// <summary>周波数 (MHz) をチャネル番号に変換する。範囲外は -1。</summary>
    public static int FreqMhzToChannel(int freqMhz)
    {
        int ch = (freqMhz - 5950) / 5;
        return (freqMhz - 5950) % 5 == 0 && ch >= 1 && ch <= 233 ? ch : -1;
    }

    private static List<int> BuildPsc()
    {
        // PSC = ch 5 + 32n (n = 0, 1, 2, ...) で ch ≤ 233 のもの
        var list = new List<int>();
        for (int n = 0; ; n++)
        {
            int ch = 5 + n * 32;
            if (ch > 233) break;
            list.Add(ch);
        }
        return list;
    }

    private static List<int> BuildAll()
    {
        // 6GHz 帯 20MHz チャネル = ch 1, 5, 9, 13 ... 229, 233 (4 刻み、59 チャネル)
        var list = new List<int>();
        for (int ch = 1; ch <= 233; ch += 4)
            list.Add(ch);
        return list;
    }
}
