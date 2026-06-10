using System.Collections.Generic;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// DFS (Dynamic Frequency Selection / 動的周波数選択) チャンネル判定ユーティリティ。
///
/// 規制背景:
///   FCC/ETSI/MIC 規則により 5GHz 帯の一部チャンネルは DFS 対象。
///   AP はレーダー信号を検出した場合 10 秒以内にそのチャンネルを停止し、
///   60 秒以内に別チャンネルへ切り替えなければならない (Channel Availability Check)。
///   クライアントから見ると突然の切断が発生しうる。
///
/// DFS 対象チャンネル (5GHz / 802.11a 規則帯域):
///   UNII-2 (U-NII-2A):     52, 56, 60, 64        (5260–5320 MHz)
///   UNII-2 Extended:      100, 104, 108, 112,
///                         116, 120, 124, 128,
///                         132, 136, 140, 144    (5500–5720 MHz)
///
/// 参考: FCC 47 CFR §15.407; ETSI EN 301 893; IEEE 802.11-2020 §11.9
/// </summary>
public static class DfsChannelHelper
{
    private static readonly HashSet<int> DfsChannels5Ghz = new()
    {
        // UNII-2A
        52, 56, 60, 64,
        // UNII-2 Extended
        100, 104, 108, 112, 116, 120, 124, 128, 132, 136, 140, 144
    };

    /// <summary>
    /// 指定バンド/チャンネルが DFS 対象かどうかを返す。
    /// DFS 対象の AP に接続するとレーダー検出時に突然切断されることがある。
    /// </summary>
    public static bool IsDfsChannel(WifiBand band, int channel)
        => band == WifiBand.Band5GHz && DfsChannels5Ghz.Contains(channel);

    /// <summary>IsDfsChannel(WifiNetwork) オーバーロード。</summary>
    public static bool IsDfsChannel(WifiNetwork network)
        => IsDfsChannel(network.Band, network.Channel);
}
