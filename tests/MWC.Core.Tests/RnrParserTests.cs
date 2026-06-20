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
    // 最小 RNR 要素: 1 TBTT エントリ、BSSID なし (tbttInfoLen=3)
    private static byte[] RnrElement(byte opClass, byte channel, byte[]? bssid6 = null)
    {
        bool hasBssid  = bssid6 is { Length: 6 };
        byte tbttLen   = (byte)(hasBssid ? 9 : 3);
        // Neighbor AP Info: tbttCount-1=0 (1 entry), tbttInfoLen=tbttLen in bits 9-15
        ushort info = (ushort)((tbttLen << 9) | 0);
        var body = new List<byte>
        {
            (byte)(info & 0xFF), (byte)(info >> 8),  // Neighbor AP Info
            0x00,                                     // TBTT Offset
            opClass, channel
        };
        if (hasBssid) body.AddRange(bssid6!);

        return new byte[] { 201, (byte)body.Count }.AppendRange(body);
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
            0x00, 0x00,        // Neighbor AP Info: tbttInfoLen=0 (bits 9-15), tbttCount=1
            0x00, 0x06,        // without fix: re-read as info=0x0600 → tbttInfoLen=3, tbttCount=1
            0x00, 0x83, 0x07,  // without fix: TBTT entry → opClass=131 (6GHz!), channel=7
        };
        var element = new byte[] { 201, (byte)body.Length }
            .AppendRange(body);

        var result = RnrParser.Parse(element);

        result.Should().BeEmpty(
            "tbttInfoLen=0 is invalid; subsequent bytes must not be mis-parsed as TBTT entries");
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
