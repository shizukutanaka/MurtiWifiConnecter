using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  802.11u Interworking 要素 (Element ID 107) の検出と適用。
//
//  背景: `WifiNetwork` は Passpoint / Hotspot 2.0 判定のために
//  `BssInfo.HasInterworkingElement` を読んでいたが、**どの層もこの値を設定していなかった**
//  ため常に false だった (docs/FEATURE-AUDIT.md §1d と同じ「配線済みだがデータ源が空」パターン)。
//  これが `Hotspot20Service` を配線できない理由として記録されていた。
//
//  ここで埋めるのは Core 側 — IE バイト列から Interworking の有無を判定し、
//  `BssInfo` に載せるところまで。**プラットフォーム層に残るのは生 IE バイトの供給だけ**で、
//  それは `IBeaconIeProvider` の実装として Windows セッションが行う。
//  解析ロジックが Core にありテスト可能であることが重要 —
//  実機でしか触れない部分を最小化するため。
// ══════════════════════════════════════════════════════════════
public class InterworkingIeTests
{
    /// <summary>[Element ID][Length][Body] 形式の IE を 1 つ組み立てる。</summary>
    private static byte[] Ie(byte id, params byte[] body)
    {
        var el = new List<byte> { id, (byte)body.Length };
        el.AddRange(body);
        return el.ToArray();
    }

    // 802.11u Interworking 要素の本文は Access Network Options (1 バイト) が必須で、
    // 任意で Venue Info (2) と HESSID (6) が続く。ここでは最小形を使う。
    private static byte[] InterworkingIe(byte accessNetworkOptions = 0x0E)
        => Ie(BeaconIeSummary.InterworkingElementId, accessNetworkOptions);

    private static byte[] SsidIe(string s)
    {
        var b = System.Text.Encoding.ASCII.GetBytes(s);
        return Ie(0, b);
    }

    [Fact]
    public void InterworkingElement_IsDetected()
    {
        var data = SsidIe("Passpoint-AP").Concat(InterworkingIe()).ToArray();

        BeaconIeParser.Parse(data).HasInterworking.Should().BeTrue();
    }

    [Fact]
    public void WithoutInterworkingElement_IsNotDetected()
    {
        // 誤検知しないこと。普通の AP を Passpoint と誤認すると
        // Hotspot20Service が的外れな案内を出す。
        BeaconIeParser.Parse(SsidIe("PlainAP")).HasInterworking.Should().BeFalse();
    }

    [Fact]
    public void ElementIdIs107_PerIeee80211u()
    {
        // 規格で定められた値。ここがずれると全く別の要素を Passpoint と誤認する。
        BeaconIeSummary.InterworkingElementId.Should().Be(107);
    }

    [Fact]
    public void InterworkingAmongOtherElements_IsStillFound()
    {
        // 実際のビーコンでは多数の IE が並ぶ。途中に埋もれていても検出できること。
        var data = SsidIe("Campus")
            .Concat(Ie(3, 6))                    // DS Parameter Set (channel 6)
            .Concat(InterworkingIe())
            .Concat(Ie(127, 0, 0, 0, 0, 0, 0, 0, 0x40))  // Extended Capabilities
            .ToArray();

        BeaconIeParser.Parse(data).HasInterworking.Should().BeTrue();
    }

    [Fact]
    public void TruncatedIe_DoesNotFalselyReportInterworking()
    {
        // 長さが本文を超える壊れた IE。パーサーは打ち切るので、
        // その先にある要素は「存在した」ことにしてはならない。
        var data = new byte[] { 0, 200 };  // SSID を名乗るが本文が無い

        BeaconIeParser.Parse(data).HasInterworking.Should().BeFalse();
    }

    // ── BssInfo への適用 ─────────────────────────────────────────

    private static WifiNetwork NetWithOneBss()
        => new()
        {
            Ssid = "Passpoint-AP",
            Auth = AuthMethod.WPA2Enterprise,
            BssEntries = new List<BssInfo> { new() { Bssid = "AA:BB:CC:11:22:33" } },
        };

    [Fact]
    public void AppliedSummary_SetsHasInterworkingElementOnBss()
    {
        // これが「読む側はあるが埋める側が無い」状態を解消する配線そのもの。
        var summary = BeaconIeParser.Parse(
            SsidIe("Passpoint-AP").Concat(InterworkingIe()).ToArray());

        var net = NetWithOneBss().WithBeaconIe(summary);

        net.BssEntries[0].HasInterworkingElement.Should().BeTrue();
    }

    [Fact]
    public void WithoutInterworking_BssFlagStaysFalse()
    {
        var summary = BeaconIeParser.Parse(SsidIe("PlainAP"));
        var net = NetWithOneBss().WithBeaconIe(summary);

        net.BssEntries[0].HasInterworkingElement.Should().BeFalse();
    }

    // ── 802.11be Multi-Link (拡張要素) ───────────────────────────
    // Interworking と Multi-Link はどちらも 107 だが**名前空間が違う** —
    // 前者は通常の Element ID、後者は拡張要素 (ID 255) の Element ID Extension。
    // 取り違えると通常の AP を Wi-Fi 7 と誤認する (またはその逆)。

    /// <summary>拡張要素: [255][len][ExtId][body...]</summary>
    private static byte[] ExtIe(byte extId, params byte[] body)
    {
        var el = new List<byte> { 255, (byte)(body.Length + 1), extId };
        el.AddRange(body);
        return el.ToArray();
    }

    private static byte[] MultiLinkIe() => ExtIe(BeaconIeSummary.MultiLinkExtensionId, 0x00, 0x00);

    [Fact]
    public void MultiLinkElement_IsDetected()
    {
        var data = SsidIe("WiFi7-AP").Concat(MultiLinkIe()).ToArray();

        BeaconIeParser.Parse(data).HasMultiLink.Should().BeTrue();
    }

    [Fact]
    public void InterworkingAlone_IsNotMistakenForMultiLink()
    {
        // 同じ 107 でも通常要素なので MLO ではない。
        var summary = BeaconIeParser.Parse(SsidIe("Passpoint").Concat(InterworkingIe()).ToArray());

        summary.HasInterworking.Should().BeTrue();
        summary.HasMultiLink.Should().BeFalse();
    }

    [Fact]
    public void MultiLinkAlone_IsNotMistakenForInterworking()
    {
        var summary = BeaconIeParser.Parse(SsidIe("WiFi7").Concat(MultiLinkIe()).ToArray());

        summary.HasMultiLink.Should().BeTrue();
        summary.HasInterworking.Should().BeFalse();
    }

    [Fact]
    public void ExtendedElementWithEmptyBody_IsIgnored_NotMisread()
    {
        // [255][0] — 拡張要素を名乗るが Ext ID が無い壊れた形。
        // 範囲外参照せず、何も検出しないこと。
        var data = SsidIe("X").Concat(new byte[] { 255, 0 }).ToArray();

        var act = () => BeaconIeParser.Parse(data);
        act.Should().NotThrow();
        BeaconIeParser.Parse(data).HasMultiLink.Should().BeFalse();
    }

    [Fact]
    public void MultiLinkSummary_SetsIsMloOnTheNetwork()
    {
        var summary = BeaconIeParser.Parse(SsidIe("WiFi7-AP").Concat(MultiLinkIe()).ToArray());

        NetWithOneBss().WithBeaconIe(summary).IsMlo.Should().BeTrue();
    }

    [Fact]
    public void WithoutMultiLink_IsMloStaysFalse()
    {
        var summary = BeaconIeParser.Parse(SsidIe("PlainAP"));

        NetWithOneBss().WithBeaconIe(summary).IsMlo.Should().BeFalse();
    }

    [Fact]
    public void AlreadySetFlag_IsNeverClearedByASummaryWithoutIt()
    {
        // 他の値と同じく「立ったら降ろさない」。あるスキャンで Interworking が
        // 取れなかっただけで Passpoint 判定を失うのは望ましくない。
        var net = new WifiNetwork
        {
            Ssid = "Passpoint-AP",
            Auth = AuthMethod.WPA2Enterprise,
            BssEntries = new List<BssInfo>
            {
                new() { Bssid = "AA:BB:CC:11:22:33", HasInterworkingElement = true },
            },
        };

        var summary = BeaconIeParser.Parse(SsidIe("Passpoint-AP"));  // Interworking 無し
        net.WithBeaconIe(summary).BssEntries[0].HasInterworkingElement.Should().BeTrue();
    }
}
