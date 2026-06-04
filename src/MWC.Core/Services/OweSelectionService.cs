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
/// </summary>
public sealed class OweSelectionService
{
    /// <summary>
    /// スキャン結果から Open/OWE ペアを検出し、OWE 優先に並べ替えた一覧を返す。
    /// OWE AP が存在する Open AP はリストから除外(透過的統合)。
    /// </summary>
    /// <summary>OWE AP が存在する Open AP を非表示にして OWE 優先リストを返す。</summary>
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

            // OWE AP の表示名に "Enhanced" バッジを付ける(UI 用メタデータ)
            if (net.Auth == AuthMethod.OWE)
            {
                result.Add(net with { });  // OWE をそのまま追加(フラグは VendorName に追記可)
            }
            else
            {
                result.Add(net);
            }
        }
        return result;
    }

    /// <summary>
    /// OWE 移行モードの AP かどうかを判定。
    /// 移行モード: Open SSID に対応する OWE SSID が異なる場合もある
    ///   (例: "FreeWifi" ↔ "FreeWifi_OWE" の組み合わせ)。
    /// </summary>
    /// <summary>Open AP と OWE AP が同一の BSS (OWE Transition Mode) かどうかを判定する。</summary>
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

    /// <summary>
    /// OWE 自動接続のプロファイル仕様を生成。
    /// Open AP への接続要求を OWE に自動昇格する。
    /// </summary>
    /// <summary>OWE 自動接続のプロファイル仕様を生成する。</summary>
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
