using System;
using System.Collections.Generic;

namespace MWC.Core.Services;

/// <summary>
/// Country 要素 (Element ID 7, 802.11d) のパーサ。
///
/// AP がビーコンで広告する規制ドメイン情報を解析する:
///   bytes 0-1: Country String (ISO 3166-1 alpha-2、例 "US")
///   byte  2  : 環境 (' '=any, 'I'=indoor, 'O'=outdoor) または 0x04
///   以降      : 1 つ以上の三つ組 (3 バイト):
///              First Channel Number / Number of Channels / Max Transmit Power (dBm)
///              ※ 先頭バイトが ≥201 の場合は Operating Triplet (規制拡張) で別解釈
///
/// 本パーサは Country String と Regulatory Triplet を抽出する。
/// 切り詰め・不正入力でも例外を投げない (防衛的設計)。
/// </summary>
public static class CountryInfoParser
{
    public const byte CountryElementId = 7;
    public const int  MinBodyLength    = 3;   // Country String(2) + Environment(1)

    public static CountryInfo? Parse(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i + 2 <= data.Length)
        {
            byte id  = data[i];
            byte len = data[i + 1];
            int bodyStart = i + 2;
            if (bodyStart + len > data.Length) break;

            if (id == CountryElementId && len >= MinBodyLength)
                return DecodeCountry(data.Slice(bodyStart, len));

            i = bodyStart + len;
        }
        return null;
    }

    private static CountryInfo DecodeCountry(ReadOnlySpan<byte> b)
    {
        // Country String: 2 文字 (3 文字目は環境)
        string code = $"{(char)b[0]}{(char)b[1]}";
        char env = (char)b[2];

        var triplets = new List<RegulatoryTriplet>();
        // 三つ組は byte 3 から (環境バイトの後)
        for (int p = 3; p + 3 <= b.Length; p += 3)
        {
            byte first = b[p];
            // 先頭 ≥201 は Operating Triplet (規制拡張) — ここでは Regulatory Triplet のみ対象
            if (first >= 201) continue;
            triplets.Add(new RegulatoryTriplet(
                FirstChannel:   first,
                ChannelCount:   b[p + 1],
                MaxTxPowerDbm:  unchecked((sbyte)b[p + 2])));
        }

        return new CountryInfo(
            CountryCode: code,
            Environment: env switch
            {
                'I' => RegulatoryEnvironment.Indoor,
                'O' => RegulatoryEnvironment.Outdoor,
                _   => RegulatoryEnvironment.Any
            },
            Triplets: triplets);
    }
}

/// <summary>Country 要素から得た規制ドメイン情報。</summary>
public sealed record CountryInfo(
    string                          CountryCode,
    RegulatoryEnvironment           Environment,
    IReadOnlyList<RegulatoryTriplet> Triplets)
{
    /// <summary>全三つ組での最大送信出力 (dBm)。三つ組がなければ null。</summary>
    public int? MaxTxPowerDbm
    {
        get
        {
            int? max = null;
            foreach (var t in Triplets)
                if (max is null || t.MaxTxPowerDbm > max) max = t.MaxTxPowerDbm;
            return max;
        }
    }
}

/// <summary>規制三つ組 (チャネル範囲 + 最大送信出力)。</summary>
public sealed record RegulatoryTriplet(
    byte  FirstChannel,
    byte  ChannelCount,
    sbyte MaxTxPowerDbm);

/// <summary>802.11d 規制環境。</summary>
public enum RegulatoryEnvironment { Any, Indoor, Outdoor }
