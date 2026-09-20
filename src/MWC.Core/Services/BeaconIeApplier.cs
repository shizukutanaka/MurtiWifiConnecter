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
            FastTransition   = network.FastTransition   || summary.SupportsFastTransition,
            NeighborReport   = network.NeighborReport   || summary.HasNeighborReport,
            BssTransitionMgmt = network.BssTransitionMgmt || summary.BssTransitionMgmt,

            // Wi-Fi 7 MLO 対応 (802.11be Multi-Link 要素を広告しているか)。
            IsMlo = network.IsMlo || summary.HasMultiLink,

            // MLO リンク詳細: RNR の MLD Parameters を持ち、かつ MLD ID が
            // この AP の Multi-Link 要素が広告する AP MLD ID と一致する近隣 AP
            // だけが、本 AP MLD の別リンクと確定できる (無条件に RNR エントリを
            // リンク扱いすると別 MLD/無関係な AP を誤表示する)。
            // LinkId/Band/Channel/Frequency は広告値、Rssi は未測定のため null。
            MloLinks = DeriveMloLinks(network.MloLinks, summary),

            // 先頭 BSS へ BssLoad / MDID を補完 (各々未設定の場合のみ)
            BssEntries = BackfillFirstBss(network.BssEntries, summary.BssLoad, summary.MobilityDomain?.Mdid, summary.HasInterworking),
        };
    }

    /// <summary>
    /// IE 要約から WMM 対応有無を判定する補助 (モデルに WMM フィールドがないため別取得用)。
    /// </summary>
    public static bool SupportsWmm(this BeaconIeSummary summary) => summary.SupportsWmm;

    /// <summary>
    /// RNR の MLD Parameters と AP 自身の MLD ID を照合し、確実に本 AP MLD の
    /// リンクと分かるエントリのみ <see cref="MloLink"/> に変換する。
    /// 既存の MloLinks があれば維持 (ランタイム API 供給の実測値を上書きしない)。
    /// </summary>
    private static System.Collections.Generic.IReadOnlyList<MloLink> DeriveMloLinks(
        System.Collections.Generic.IReadOnlyList<MloLink> existing, BeaconIeSummary summary)
    {
        if (existing.Count > 0) return existing;
        if (summary.OwnApMldId is not byte own) return existing;

        var links = new System.Collections.Generic.List<MloLink>();
        foreach (var n in summary.RnrNeighbors)
        {
            if (n.MldId != own) continue;
            links.Add(new MloLink
            {
                LinkId       = n.MldLinkId ?? 0,
                Band         = BandFromOperatingClass(n.OperatingClass),
                Channel      = n.Channel,
                FrequencyMhz = FreqMhz(n.OperatingClass, n.Channel),
                Rssi         = null,                    // 広告には無い実測値
                ChannelWidth = ChannelWidthMhz(n.OperatingClass),
            });
        }
        return links.Count > 0 ? links : existing;
    }

    // グローバル Operating Class → バンド (IEEE 802.11 Annex E)。
    // 81-84: 2.4GHz / 115-130: 5GHz / 131-135: 6GHz。
    private static WifiBand BandFromOperatingClass(byte opClass) => opClass switch
    {
        >= 131 and <= 135 => WifiBand.Band6GHz,
        >= 115 and <= 130 => WifiBand.Band5GHz,
        >= 81  and <= 84  => WifiBand.Band2_4GHz,
        _                 => WifiBand.Unknown
    };

    // Operating Class の公称チャネル幅 (MHz)。80+80 は 160 として扱う。
    private static int ChannelWidthMhz(byte opClass) => opClass switch
    {
        134 or 135                              => 160,
        133 or 128 or 129 or 130                => 80,
        132 or 116 or 117 or 119 or 120 or 122 or 123 or 125 or 126 => 40,
        131 or 115 or 118 or 121 or 124 or 81 or 82 or 83 or 84     => 20,
        _                                       => 0
    };

    private static int FreqMhz(byte opClass, int channel) => BandFromOperatingClass(opClass) switch
    {
        WifiBand.Band6GHz   => SixGhzChannelHelper.ChannelToFreqMhz(channel),
        WifiBand.Band5GHz   => 5000 + channel * 5,
        WifiBand.Band2_4GHz => channel == 14 ? 2484 : 2407 + channel * 5,
        _                   => 0
    };

    private static System.Collections.Generic.IReadOnlyList<BssInfo> BackfillFirstBss(
        System.Collections.Generic.IReadOnlyList<BssInfo> entries, BssLoad? bssLoad, ushort? mdid,
        bool hasInterworking = false)
    {
        if (entries.Count == 0) return entries;

        var first = entries[0];
        // 既存値は上書きしない。補完すべき値がなければそのまま返す。
        BssLoad? newLoad = first.BssLoad ?? bssLoad;
        ushort?  newMdid = first.MobilityDomainId ?? mdid;
        // 802.11u Interworking は「立ったら降ろさない」— 他の値と同じく既存を尊重する。
        bool newInterworking = first.HasInterworkingElement || hasInterworking;
        if (ReferenceEquals(newLoad, first.BssLoad) && newMdid == first.MobilityDomainId
            && newInterworking == first.HasInterworkingElement)
            return entries;

        var updated = new BssInfo[entries.Count];
        updated[0] = first with
        {
            BssLoad = newLoad, MobilityDomainId = newMdid,
            HasInterworkingElement = newInterworking,
        };
        for (int i = 1; i < entries.Count; i++)
            updated[i] = entries[i];
        return updated;
    }
}
