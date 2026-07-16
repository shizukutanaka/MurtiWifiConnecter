using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  WmmParser — WMM/WME Parameter IE (Vendor Specific 221, OUI 00:50:F2, Type 2)
//  バイトレベルのゴールデンテスト
// ══════════════════════════════════════════════════════════════
public class WmmParserTests
{
    // WMM Parameter 要素を組み立てる (4 AC パラメータ付き)
    private static byte[] WmmParamElement(
        byte qosInfo = 0x02,
        // BE: AIFSN=3, ECWmin=4, ECWmax=10, TXOP=0
        byte beAifsn = 0x03, byte beEcw = 0xA4, ushort beTxop = 0,
        // BK: AIFSN=7, ECWmin=4, ECWmax=10, TXOP=0
        byte bkAifsn = 0x27, byte bkEcw = 0xA4, ushort bkTxop = 0,
        // VI: AIFSN=2, ACM=0, ECWmin=3, ECWmax=4, TXOP=188 (6016 μs)
        byte viAifsn = 0x42, byte viEcw = 0x43, ushort viTxop = 188,
        // VO: AIFSN=2, ACM=0, ECWmin=2, ECWmax=3, TXOP=102 (3264 μs)
        byte voAifsn = 0x62, byte voEcw = 0x32, ushort voTxop = 102)
    {
        return new byte[]
        {
            221, 24,                                     // Element ID, Length
            0x00, 0x50, 0xF2,                            // OUI (Microsoft/WMM)
            0x02,                                        // OUI Type = WMM
            0x01,                                        // Subtype = Parameter
            0x01,                                        // Version
            qosInfo, 0x00,                               // QoS Info, Reserved
            // AC_BE (ACI=00)
            beAifsn, beEcw, (byte)(beTxop & 0xFF), (byte)(beTxop >> 8),
            // AC_BK (ACI=01)
            bkAifsn, bkEcw, (byte)(bkTxop & 0xFF), (byte)(bkTxop >> 8),
            // AC_VI (ACI=10)
            viAifsn, viEcw, (byte)(viTxop & 0xFF), (byte)(viTxop >> 8),
            // AC_VO (ACI=11)
            voAifsn, voEcw, (byte)(voTxop & 0xFF), (byte)(voTxop >> 8),
        };
    }

    [Fact]
    public void ParsesAllFourCategories()
    {
        var r = WmmParser.ParseParameters(WmmParamElement());

        r.Should().NotBeNull();
        r!.AcParams.Should().HaveCount(4);
        r.GetAc(WmmAccessCategory.BestEffort).Should().NotBeNull();
        r.GetAc(WmmAccessCategory.Background).Should().NotBeNull();
        r.GetAc(WmmAccessCategory.Video).Should().NotBeNull();
        r.GetAc(WmmAccessCategory.Voice).Should().NotBeNull();
    }

    [Fact]
    public void ParameterSetCount_FromQosInfoLowNibble()
    {
        var r = WmmParser.ParseParameters(WmmParamElement(qosInfo: 0x05));
        r!.ParameterSetCount.Should().Be(5);
    }

    [Fact]
    public void UapsdEnabled_HighBitOfQosInfo()
    {
        WmmParser.ParseParameters(WmmParamElement(qosInfo: 0x82))!
                 .UapsdEnabled.Should().BeTrue();
        WmmParser.ParseParameters(WmmParamElement(qosInfo: 0x02))!
                 .UapsdEnabled.Should().BeFalse();
    }

    [Fact]
    public void VideoAc_DecodesAifsn_And_Txop()
    {
        var r = WmmParser.ParseParameters(WmmParamElement());
        var vi = r!.GetAc(WmmAccessCategory.Video)!;

        // viAifsn = 0x42 → ACI=10 (VI), ACM=0, AIFSN=2
        vi.Aifsn.Should().Be(2);
        vi.AdmissionControlMandatory.Should().BeFalse();
        // viTxop = 188 → 188×32 = 6016 μs
        vi.TxopLimitMicroseconds.Should().Be(6016);
    }

    [Fact]
    public void BestEffortAc_CwMinCwMax()
    {
        var r = WmmParser.ParseParameters(WmmParamElement());
        var be = r!.GetAc(WmmAccessCategory.BestEffort)!;
        // beEcw = 0xA4 → ECWmax=10, ECWmin=4 → CWmax=1023, CWmin=15
        be.EcwMin.Should().Be(4);
        be.EcwMax.Should().Be(10);
        be.CwMin.Should().Be(15);   // 2^4 - 1
        be.CwMax.Should().Be(1023); // 2^10 - 1
    }

    [Fact]
    public void AdmissionControlMandatory_Decoded()
    {
        // ACM bit = bit4 of ACI/AIFSN byte
        // viAifsn with ACM=1: 0x42 | 0x10 = 0x52
        var r = WmmParser.ParseParameters(WmmParamElement(viAifsn: 0x52));
        r!.GetAc(WmmAccessCategory.Video)!.AdmissionControlMandatory.Should().BeTrue();
    }

    [Fact]
    public void NonWmmVendorSpecific_ReturnsNull()
    {
        // OUI 00:0C:E7 (not WMM)
        var element = new byte[] { 221, 7, 0x00, 0x0C, 0xE7, 0x02, 0x01, 0x01, 0x00 };
        WmmParser.ParseParameters(element).Should().BeNull();
    }

    [Fact]
    public void EmptySpan_ReturnsNull()
    {
        WmmParser.ParseParameters(System.Array.Empty<byte>()).Should().BeNull();
    }

    [Fact]
    public void ParseQosInfo_FindsFromInfoElement()
    {
        // WMM Info element (Subtype=0x00), length=7
        var info = new byte[] { 221, 7, 0x00, 0x50, 0xF2, 0x02, 0x00, 0x01, 0x03 };
        WmmParser.ParseQosInfo(info).Should().Be(0x03);
    }
}
