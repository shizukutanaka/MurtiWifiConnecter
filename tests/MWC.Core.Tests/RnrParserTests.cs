using System.Collections.Generic;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  RnrParser — Reduced Neighbor Report (Element ID 201)
//  802.11ax 6GHz 近隣 AP 発見 — バイトレベルのゴールデンテスト
// ══════════════════════════════════════════════════════════════
public class RnrParserTests
{
    // 最小 RNR 要素: 1 TBTT エントリ (802.11-2020 §9.4.2.170 / Wireshark 突合済み)
    // Neighbor AP Info field = TBTT Header(2B) + Operating Class(1B) + Channel(1B)
    //   + TBTT エントリ群。Length ∈ bits 8-15、Count-1 ∈ bits 4-7。
    // TBTT エントリ = TBTT Offset(1B) + Length 依存サブフィールド。
    //   Length=1: offset のみ / Length=7: offset+BSSID / Length>=16: +MLD Parameters
    private static byte[] RnrElement(byte opClass, byte channel, byte[]? bssid6 = null,
        byte? mldId = null, byte? linkId = null)
    {
        bool hasBssid  = bssid6 is { Length: 6 };
        bool hasMld    = mldId.HasValue || linkId.HasValue;
        byte tbttLen   = (byte)(hasMld ? 16 : hasBssid ? 7 : 1);
        ushort info    = (ushort)((tbttLen << 8) | (0 << 4));   // 1 entry
        var entry = new List<byte> { 0x00 };                    // TBTT Offset
        if (hasBssid)  entry.AddRange(bssid6!);
        if (hasMld)
        {
            // Short-SSID(4B) + BSS Params(1B) + 20MHz PSD(1B) + MLD Params(3B)
            entry.AddRange(new byte[] { 0, 0, 0, 0, 0, 0 });
            int mld = (mldId ?? 0) | ((linkId ?? 0) << 8);
            entry.AddRange(new byte[] { (byte)(mld & 0xFF), (byte)((mld >> 8) & 0xFF), (byte)((mld >> 16) & 0xFF) });
        }
        var body = new List<byte>
        {
            (byte)(info & 0xFF), (byte)(info >> 8),  // TBTT Information Header
            opClass, channel                         // Operating Class + Channel (フィールド単位で一度)
        };
        body.AddRange(entry);

        return new byte[] { 201, (byte)body.Count }.Concat(body).ToArray();
    }

    [Fact]
    public void ParsesSingleEntry_NoOptionalBssid()
    {
        var bytes = RnrElement(opClass: 131, channel: 7);
        var r = RnrParser.Parse(bytes);

        r.Should().ContainSingle();
        r[0].OperatingClass.Should().Be(131);
        r[0].Channel.Should().Be(7);
        r[0].Bssid.Should().BeNull();
        r[0].Is6GHz.Should().BeTrue();
    }

    [Fact]
    public void ParsesSingleEntry_WithBssid()
    {
        var bssid = new byte[] { 0xAA, 0xBB, 0xCC, 0x11, 0x22, 0x33 };
        var r = RnrParser.Parse(RnrElement(opClass: 131, channel: 7, bssid6: bssid));

        r.Should().ContainSingle();
        r[0].Bssid.Should().Be("aa:bb:cc:11:22:33");
    }

    [Fact]
    public void Is6GHz_Boundary()
    {
        RnrParser.Parse(RnrElement(130, 1))[0].Is6GHz.Should().BeFalse();
        RnrParser.Parse(RnrElement(131, 1))[0].Is6GHz.Should().BeTrue();
        RnrParser.Parse(RnrElement(135, 1))[0].Is6GHz.Should().BeTrue();
        RnrParser.Parse(RnrElement(136, 1))[0].Is6GHz.Should().BeFalse();
    }

    [Fact]
    public void SkipsNon201Elements_ThenFindsRnr()
    {
        var stream = new List<byte>();
        stream.AddRange(new byte[] { 0, 3, 0x41, 0x42, 0x43 }); // SSID element
        stream.AddRange(RnrElement(131, 9));
        var r = RnrParser.Parse(stream.ToArray());

        r.Should().ContainSingle();
        r[0].Channel.Should().Be(9);
    }

    [Fact]
    public void EmptySpan_ReturnsEmpty()
    {
        RnrParser.Parse(System.Array.Empty<byte>()).Should().BeEmpty();
    }

    [Fact]
    public void TruncatedBody_ParsedSafely()
    {
        // Declare length 10 but provide only 4 bytes of body → truncated at element boundary
        RnrParser.Parse(new byte[] { 201, 10, 0x00, 0x00, 0x01, 0x09 })
                 .Should().BeEmpty();
    }

    [Fact]
    public void ParsesTwoNeighborInfoSets_BothContributed()
    {
        // Two back-to-back Neighbor AP Info sets inside one RNR element body.
        // Set 1: opClass=131 (6 GHz), channel=5.
        // Set 2: opClass=115 (5 GHz), channel=36.
        // Regression: ParseRnrBody must not exit (return) after the first set.
        //
        // Neighbor AP Info (LE 16-bit): bits 4-7 = tbttCount-1, bits 8-15 = tbttInfoLen.
        // tbttInfoLen=1 (offset only), tbttCount=1 → info = 1<<8 = 0x0100 → [0x00, 0x01].
        // フィールド構造: [Header 2B][OpClass][Channel][TBTT エントリ(offset のみ)]
        const byte InfoLo = 0x00;
        const byte InfoHi = 0x01;

        var body = new List<byte>
        {
            InfoLo, InfoHi, 131, 5,   0x00,   // Set 1: OpClass=131, Channel=5, entry=offsetのみ
            InfoLo, InfoHi, 115, 36,  0x00    // Set 2: OpClass=115, Channel=36, entry=offsetのみ
        };
        var element = new byte[] { 201, (byte)body.Count }.AppendRange(body);

        var result = RnrParser.Parse(element);

        result.Should().HaveCount(2);
        result.Should().Contain(ap => ap.OperatingClass == 131 && ap.Channel == 5);
        result.Should().Contain(ap => ap.OperatingClass == 115 && ap.Channel == 36);
    }

    [Fact]
    public void MalformedEntry_TbttInfoLenZero_DoesNotProduceSpuriousEntries()
    {
        // Before fix: tbttInfoLen=0 caused the inner for-loop (pos += 0) to not advance pos.
        // The outer while then re-read bytes at the same offset as a new Neighbor AP Info header.
        // The crafted body below is designed so that without the fix, the re-parsed header
        // (0x00,0x06 → info=0x0600, tbttInfoLen=3, tbttCount=1) produces a spurious
        // Is6GHz RnrNeighborAp entry (opClass=131, channel=7).
        // After fix: break on tbttInfoLen==0 → result is empty, no exception.
        var body = new byte[]
        {
            0x00, 0x00,        // TBTT Header: tbttInfoLen=0 (bits 8-15) → 不正
            0x83, 0x07,        // 以降は読まれてはいけない (opClass=131, channel=7 に見える)
            0x00, 0x06,
        };
        var element = new byte[] { 201, (byte)body.Length }
            .AppendRange(body);

        var result = RnrParser.Parse(element);

        result.Should().BeEmpty(
            "tbttInfoLen=0 is invalid; subsequent bytes must not be mis-parsed as TBTT entries");
    }

    [Fact]
    public void ParsesMldParameters_LinkIdAndMldId()
    {
        // Length>=16 のエントリは MLD Parameters を持つ (802.11be)。
        var bssid = new byte[] { 0xAA, 0xBB, 0xCC, 0x11, 0x22, 0x33 };
        var r = RnrParser.Parse(RnrElement(131, 7, bssid6: bssid, mldId: 0x42, linkId: 3));

        r.Should().ContainSingle();
        r[0].MldId.Should().Be(0x42);
        r[0].MldLinkId.Should().Be(3);
        r[0].IsMloAffiliated.Should().BeTrue();
    }

    [Fact]
    public void NoMldParameters_NotAffiliated()
    {
        var r = RnrParser.Parse(RnrElement(131, 7));
        r[0].IsMloAffiliated.Should().BeFalse();
        r[0].MldId.Should().BeNull();
    }
}

// ── helper ──────────────────────────────────────────────────────────────
file static class ByteArrayExt
{
    public static byte[] AppendRange(this byte[] prefix, IEnumerable<byte> more)
    {
        var list = new List<byte>(prefix);
        list.AddRange(more);
        return list.ToArray();
    }
}
