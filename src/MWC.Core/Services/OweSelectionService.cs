using System;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// WPA3-OWE (Opportunistic Wireless Encryption / Enhanced Open) 自動選択サービス。
///
/// 背景:
///   OWE は Open AP に対応する「隠れペア AP」として動作する。
///   例: "CafeWifi" (Open) と "CafeWifi" (OWE) が共存する場合、
///       OWE 対応クライアントは OWE AP を優先すべき。
///   Windows は WLAN API で自動的に処理するが、
///   スキャン結果表示ではユーザーに透過的に提示する必要がある。
///
/// 動作:
///   1. スキャン結果から Open + OWE の SSID ペアを検出
///   2. OWE 対応デバイスでは OWE を優先 → SSID を統合表示
///   3. 接続時に OWE プロファイルを自動生成
///
/// 既知の制限(GUI/CLI 配線時に検討・許容した事項、2026-07):
///   <see cref="ApplyOwePreference"/> は Open AP が <c>IsConnected</c> かどうかに関わらず
///   OWE 側が存在すれば無条件で除外する。OWE 非対応の端末が過去に Windows ネイティブ設定
///   (MWC 経由でない)で Open 側へ接続済みだった場合、理論上は「実際は接続中なのに UI 上は
///   未接続に見える」表示上の不整合が起こりうる。実際の OS レベルの接続状態には影響しない
///   (表示のみ)。発生条件が狭い(OWE 非対応端末 + 既存 Open プロファイル)ため、
///   呼び出し側で追加のガードは設けていない — 再発時は Open 側の <c>IsConnected</c> を
///   常に残す変更を検討すること。
/// </summary>
public sealed class OweSelectionService
{
    /// <summary>OWE AP が存在する Open AP を非表示にして OWE 優先リストを返す。同名 Open AP はリストから除外し透過的統合を実現する。</summary>
    public IReadOnlyList<WifiNetwork> ApplyOwePreference(
        IReadOnlyList<WifiNetwork> networks)
    {
        // OWE として認識できる SSID セットを構築
        var oweSsids = networks
            .Where(n => n.Auth == AuthMethod.OWE)
            .Select(n => n.Ssid)
            .ToHashSet(StringComparer.Ordinal);

        var result = new List<WifiNetwork>();
        foreach (var net in networks)
        {
            // Open AP で同名 OWE AP が存在 → OWE を優先するため Open を除外
            if (net.Auth == AuthMethod.Open && oweSsids.Contains(net.Ssid))
                continue;

            result.Add(net);
        }
        return result;
    }

    /// <summary>Open AP と OWE AP が同一の BSS (OWE Transition Mode) かどうかを判定する。移行モードでは Open SSID に対応する OWE SSID が異なる場合もある ("FreeWifi" ↔ "FreeWifi_OWE")。</summary>
    public bool IsOweTransitionPair(WifiNetwork open, WifiNetwork owe)
    {
        if (open.Auth != AuthMethod.Open) return false;
        if (owe.Auth != AuthMethod.OWE)  return false;
        // 同一 SSID または OWE Transition AP (BSSID が隣接)
        if (string.Equals(open.Ssid, owe.Ssid, StringComparison.Ordinal))
            return true;
        // BSSID が一致する BSS エントリを持つかどうかで判断
        var openBssids = open.BssEntries.Select(b => b.Bssid).ToHashSet();
        var oweBssids  = owe.BssEntries.Select(b => b.Bssid).ToHashSet();
        return openBssids.Overlaps(oweBssids);
    }

    /// <summary>OWE 自動接続のプロファイル仕様を生成する。Open AP への接続要求を OWE に自動昇格する。</summary>
    public WifiProfileSpec BuildOweSpec(string ssid)
        => new() { Ssid = ssid, Auth = AuthMethod.OWE };

    /// <summary>
    /// 指定ネットワークに対して推奨認証方式を返す(OWE 優先)。
    /// </summary>
    public AuthMethod RecommendAuth(WifiNetwork network,
        IReadOnlyList<WifiNetwork> allNetworks)
    {
        if (network.Auth == AuthMethod.Open)
        {
            // 同 SSID に OWE AP があれば OWE を推奨
            var hasOwe = allNetworks.Any(n =>
                n.Auth == AuthMethod.OWE &&
                string.Equals(n.Ssid, network.Ssid, StringComparison.Ordinal));
            if (hasOwe) return AuthMethod.OWE;
        }
        return network.Auth;
    }
}
