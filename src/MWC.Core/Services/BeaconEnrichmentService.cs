using System.Collections.Generic;
using MWC.Core.Abstractions;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// 生ビーコン IE (<see cref="RawBeaconData"/>) を使って <see cref="WifiNetwork"/> を
/// 詳細情報で強化する。プラットフォーム非依存 — 生 IE の取得元 (P/Invoke 等) は
/// 呼び出し側が <see cref="IBeaconIeProvider"/> 経由で供給する。
///
/// 強化内容:
///   - BeaconIeParser で IE を解析 (Country/TPC/BSS Load/RNR/MDID/WMM)
///   - BeaconIeApplier で FastTransition / NeighborReport / BssLoad / MDID をモデルへ反映
///
/// TSF タイムスタンプ (稼働時間) や Country/TPC は BeaconIeSummary 側に残るため、
/// 表示が必要なら呼び出し側で <see cref="BeaconIeParser.Parse"/> を直接使う。
/// IE が無い BSS は元のまま (劣化なし)。
/// </summary>
public sealed class BeaconEnrichmentService
{
    /// <summary>
    /// ネットワーク群を生ビーコン辞書で強化する。
    /// BSSID は各ネットワークの先頭 BSS エントリで照合する。
    /// </summary>
    public IReadOnlyList<WifiNetwork> Enrich(
        IReadOnlyList<WifiNetwork> networks,
        IReadOnlyDictionary<string, RawBeaconData> rawBeacons)
    {
        if (rawBeacons.Count == 0) return networks;

        var result = new List<WifiNetwork>(networks.Count);
        foreach (var net in networks)
            result.Add(EnrichOne(net, rawBeacons));
        return result;
    }

    /// <summary>単一ネットワークを強化する。一致する生ビーコンが無ければ原型を返す。</summary>
    public WifiNetwork EnrichOne(
        WifiNetwork net, IReadOnlyDictionary<string, RawBeaconData> rawBeacons)
    {
        if (net.BssEntries.Count == 0) return net;

        var bssid = Normalize(net.BssEntries[0].Bssid);
        if (!rawBeacons.TryGetValue(bssid, out var raw)) return net;

        var summary = BeaconIeParser.Parse(raw.InformationElements);
        return net.WithBeaconIe(summary);
    }

    /// <summary>BSSID を比較用に正規化する (小文字)。</summary>
    public static string Normalize(string bssid) => bssid.ToLowerInvariant();
}
