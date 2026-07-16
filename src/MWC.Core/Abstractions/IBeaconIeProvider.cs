using System;
using System.Collections.Generic;

namespace MWC.Core.Abstractions;

/// <summary>
/// BSSID ごとの生 802.11 情報要素 (IE) バイト列と TSF タイムスタンプを供給する。
///
/// <see cref="IWifiService"/> の標準スキャン (ManagedNativeWifi) は IE 生バイトを
/// 公開しないため、Country/TPC/BSS Load/RNR/Mobility Domain 等の詳細解析には
/// 別経路 (Windows: WlanGetNetworkBssList P/Invoke) が要る。本抽象でその経路を分離し、
/// Core 側のパーサ (BeaconIeParser) に供給する。
///
/// 実装が無い/取得失敗時は空を返す (例外は投げない)。プラットフォーム差を吸収し、
/// IE が得られない環境でも基本スキャンは劣化なく動作する。
/// </summary>
public interface IBeaconIeProvider
{
    /// <summary>
    /// 指定アダプターで可視な各 BSS の生 IE / TSF を取得する。
    /// 取得不能なら空辞書。
    /// </summary>
    /// <returns>BSSID(小文字 "aa:bb:cc:dd:ee:ff") → 生 IE 情報</returns>
    IReadOnlyDictionary<string, RawBeaconData> GetRawBeacons(Guid adapterId);
}

/// <summary>1 つの BSS の生ビーコン情報。</summary>
public sealed record RawBeaconData(
    byte[] InformationElements,
    ulong  TsfTimestamp,
    ushort BeaconIntervalTu);

/// <summary>IE 供給源が無い場合の既定実装 (常に空)。</summary>
public sealed class NullBeaconIeProvider : IBeaconIeProvider
{
    public static readonly NullBeaconIeProvider Instance = new();

    private static readonly IReadOnlyDictionary<string, RawBeaconData> Empty =
        new Dictionary<string, RawBeaconData>();

    public IReadOnlyDictionary<string, RawBeaconData> GetRawBeacons(Guid adapterId) => Empty;
}
