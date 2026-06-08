using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// <see cref="BeaconIeSummary"/> を <see cref="WifiNetwork"/> の能力フラグへ反映する橋渡し。
///
/// プラットフォーム層は生 IE blob を <see cref="BeaconIeParser.Parse"/> で解析し、
/// 得た要約をこの拡張で適用する。これにより「IE バイト → モデルのフラグ」変換が
/// 1 箇所に集約され、各プラットフォーム実装での重複・取りこぼしを防ぐ。
///
/// 既存値が true の場合は維持する (別経路で検出済みの能力を打ち消さない)。
/// </summary>
public static class BeaconIeApplier
{
    /// <summary>
    /// IE 要約から導けるローミング / QoS / 混雑フラグを反映した
    /// <see cref="WifiNetwork"/> のコピーを返す。元のインスタンスは変更しない。
    /// </summary>
    public static WifiNetwork WithBeaconIe(this WifiNetwork network, BeaconIeSummary summary)
    {
        return network with
        {
            // ローミング能力 (既存 true は維持)
            FastTransition = network.FastTransition || summary.SupportsFastTransition,
            NeighborReport = network.NeighborReport || summary.HasNeighborReport,

            // 先頭 BSS へ BssLoad / MDID を補完 (各々未設定の場合のみ)
            BssEntries = BackfillFirstBss(network.BssEntries, summary.BssLoad, summary.MobilityDomain?.Mdid),
        };
    }

    /// <summary>
    /// IE 要約から WMM 対応有無を判定する補助 (モデルに WMM フィールドがないため別取得用)。
    /// </summary>
    public static bool SupportsWmm(this BeaconIeSummary summary) => summary.SupportsWmm;

    private static System.Collections.Generic.IReadOnlyList<BssInfo> BackfillFirstBss(
        System.Collections.Generic.IReadOnlyList<BssInfo> entries, BssLoad? bssLoad, ushort? mdid)
    {
        if (entries.Count == 0) return entries;

        var first = entries[0];
        // 既存値は上書きしない。補完すべき値がなければそのまま返す。
        BssLoad? newLoad = first.BssLoad ?? bssLoad;
        ushort?  newMdid = first.MobilityDomainId ?? mdid;
        if (ReferenceEquals(newLoad, first.BssLoad) && newMdid == first.MobilityDomainId)
            return entries;

        var updated = new BssInfo[entries.Count];
        updated[0] = first with { BssLoad = newLoad, MobilityDomainId = newMdid };
        for (int i = 1; i < entries.Count; i++)
            updated[i] = entries[i];
        return updated;
    }
}
