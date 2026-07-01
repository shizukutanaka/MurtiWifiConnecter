using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MWC.Core.Models;
using MWC.Core.Services;
using MWC.Core.Tests.Fakes;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  EapAuthStatsService — 802.1X (Enterprise) 認証成功率の集計
//  (ROADMAP.md 「802.1X 自動テスト(EAP 認証成功率を計測)」の計測基盤)
//  NetworkHistoryServiceTests と同じスタイル: 実ファイル (%LocalAppData%)
//  に対して読み書きするため、SSID は他テストと衝突しない一意な接頭辞を使う。
// ══════════════════════════════════════════════════════════════
internal static class EapTestSsid
{
    // SSID は IEEE 802.11 の 32 バイト上限があるため、一意化には短い16進サフィックスのみ使う
    // (Guid.NewGuid() の文字列表現は 36 文字あり、接頭辞と合わせると容易に超過する)。
    public static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];
}

public class EapAuthStatsServiceTests
{
    [Fact]
    public void RecordAttempt_Success_IncrementsSuccessCount()
    {
        var svc = new EapAuthStatsService();
        svc.RecordAttempt("EapTest_Success1", EapType.PEAP_MSCHAPv2, true);

        var stat = svc.GetStat("EapTest_Success1", EapType.PEAP_MSCHAPv2);
        stat.Should().NotBeNull();
        stat!.SuccessCount.Should().Be(1);
        stat.FailCount.Should().Be(0);
    }

    [Fact]
    public void RecordAttempt_Failure_IncrementsFailCount()
    {
        var svc = new EapAuthStatsService();
        svc.RecordAttempt("EapTest_Fail1", EapType.EAP_TLS, false);

        var stat = svc.GetStat("EapTest_Fail1", EapType.EAP_TLS);
        stat!.FailCount.Should().Be(1);
        stat.SuccessCount.Should().Be(0);
    }

    [Fact]
    public void RecordAttempt_SameSsidDifferentEapType_TracksSeparately()
    {
        var svc = new EapAuthStatsService();
        svc.RecordAttempt("EapTest_MultiType", EapType.PEAP_MSCHAPv2, true);
        svc.RecordAttempt("EapTest_MultiType", EapType.EAP_TLS, false);

        svc.GetStat("EapTest_MultiType", EapType.PEAP_MSCHAPv2)!.SuccessCount.Should().Be(1);
        svc.GetStat("EapTest_MultiType", EapType.EAP_TLS)!.FailCount.Should().Be(1);
    }

    [Fact]
    public void RecordAttempt_AccumulatesAcrossCalls()
    {
        var svc = new EapAuthStatsService();
        svc.RecordAttempt("EapTest_Accum", EapType.EAP_TTLS, true);
        svc.RecordAttempt("EapTest_Accum", EapType.EAP_TTLS, true);
        svc.RecordAttempt("EapTest_Accum", EapType.EAP_TTLS, false);

        var stat = svc.GetStat("EapTest_Accum", EapType.EAP_TTLS);
        stat!.SuccessCount.Should().Be(2);
        stat.FailCount.Should().Be(1);
        stat.TotalAttempts.Should().Be(3);
    }

    [Fact]
    public void SuccessRate_ComputesCorrectly()
    {
        var svc = new EapAuthStatsService();
        svc.RecordAttempt("EapTest_Rate", EapType.EAP_AKA, true);
        svc.RecordAttempt("EapTest_Rate", EapType.EAP_AKA, true);
        svc.RecordAttempt("EapTest_Rate", EapType.EAP_AKA, true);
        svc.RecordAttempt("EapTest_Rate", EapType.EAP_AKA, false);

        svc.GetStat("EapTest_Rate", EapType.EAP_AKA)!.SuccessRate.Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public void SuccessRate_NoAttempts_DefaultsToOptimistic()
    {
        // 記録が無い合成インスタンスは 1.0 (楽観値) を返す仕様。
        var stat = new EapAuthStat("Unrecorded", EapType.PEAP_MSCHAPv2, 0, 0, DateTimeOffset.UtcNow);
        stat.SuccessRate.Should().Be(1.0);
    }

    [Fact]
    public void GetStat_UnknownSsid_ReturnsNull()
    {
        var svc = new EapAuthStatsService();
        svc.GetStat(EapTestSsid.Unique("EapTest_DoesNotExist_"), EapType.PEAP_MSCHAPv2).Should().BeNull();
    }

    [Fact]
    public void GetAll_IncludesRecordedEntry()
    {
        var svc = new EapAuthStatsService();
        var uniqueSsid = EapTestSsid.Unique("EapTest_GetAll_");
        svc.RecordAttempt(uniqueSsid, EapType.EAP_TLS, true);

        svc.GetAll().Should().Contain(e => e.Ssid == uniqueSsid && e.EapType == EapType.EAP_TLS);
    }
}

// ══════════════════════════════════════════════════════════════
//  ConnectionExecutor → EapAuthStatsService wiring
// ══════════════════════════════════════════════════════════════
public class ConnectionExecutorEapStatsWiringTests
{
    [Fact]
    public async Task ConnectAsync_EnterpriseSpecSuccess_RecordsEapStat()
    {
        var wifi = new FakeWifiService { NextConnectResult = ConnectionResult.Ok("ignored", true, false) };
        var hist = new NetworkHistoryService(NullLogger<NetworkHistoryService>.Instance);
        var eapStats = new EapAuthStatsService();
        var exec = new ConnectionExecutor(
            wifi, hist, NullLogger<ConnectionExecutor>.Instance, eapStats);

        var ssid = EapTestSsid.Unique("EapWiring_Ok_");
        var spec = new WifiProfileSpec
        {
            Ssid     = ssid,
            Auth     = AuthMethod.WPA2Enterprise,
            EapType  = EapType.PEAP_MSCHAPv2,
            Username = "student",
            Password = "hunter22",
        };

        var result = await exec.ConnectAsync(Guid.NewGuid(), spec);

        result.Success.Should().BeTrue();
        var stat = eapStats.GetStat(ssid, EapType.PEAP_MSCHAPv2);
        stat.Should().NotBeNull();
        stat!.SuccessCount.Should().Be(1);
        stat.FailCount.Should().Be(0);
    }

    [Fact]
    public async Task ConnectAsync_EnterpriseSpecFailure_RecordsEapFailure()
    {
        var wifi = new FakeWifiService { NextConnectResult = ConnectionResult.Fail(ConnectionFailure.BadCredentials) };
        var hist = new NetworkHistoryService(NullLogger<NetworkHistoryService>.Instance);
        var eapStats = new EapAuthStatsService();
        var exec = new ConnectionExecutor(
            wifi, hist, NullLogger<ConnectionExecutor>.Instance, eapStats);

        var ssid = EapTestSsid.Unique("EapWiring_Fail_");
        var spec = new WifiProfileSpec
        {
            Ssid     = ssid,
            Auth     = AuthMethod.WPA3Enterprise,
            EapType  = EapType.EAP_TLS,
        };

        var result = await exec.ConnectAsync(Guid.NewGuid(), spec);

        result.Success.Should().BeFalse();
        var stat = eapStats.GetStat(ssid, EapType.EAP_TLS);
        stat!.FailCount.Should().Be(1);
        stat.SuccessCount.Should().Be(0);
    }

    [Fact]
    public async Task ConnectAsync_PersonalAuth_DoesNotRecordEapStat()
    {
        // WPA2PSK には EapType がないため、EAP 統計には一切記録されないはず。
        var wifi = new FakeWifiService { NextConnectResult = ConnectionResult.Ok("ignored", true, false) };
        var hist = new NetworkHistoryService(NullLogger<NetworkHistoryService>.Instance);
        var eapStats = new EapAuthStatsService();
        var exec = new ConnectionExecutor(
            wifi, hist, NullLogger<ConnectionExecutor>.Instance, eapStats);

        var ssid = EapTestSsid.Unique("EapWiring_Psk_");
        var result = await exec.ConnectAsync(Guid.NewGuid(), ssid, AuthMethod.WPA2PSK, "pass12345");

        result.Success.Should().BeTrue();
        eapStats.GetAll().Should().NotContain(e => e.Ssid == ssid);
    }

    [Fact]
    public async Task ConnectAsync_NoEapStatsProvided_DoesNotThrow()
    {
        // 既存の 3 引数コンストラクタ(EapAuthStatsService 省略)との後方互換性確認。
        var wifi = new FakeWifiService { NextConnectResult = ConnectionResult.Ok("ignored", true, false) };
        var hist = new NetworkHistoryService(NullLogger<NetworkHistoryService>.Instance);
        var exec = new ConnectionExecutor(wifi, hist, NullLogger<ConnectionExecutor>.Instance);

        var spec = new WifiProfileSpec
        {
            Ssid     = EapTestSsid.Unique("EapWiring_NoStats_"),
            Auth     = AuthMethod.WPA2Enterprise,
            EapType  = EapType.PEAP_MSCHAPv2,
            Username = "student",
            Password = "hunter22",
        };

        var act = async () => await exec.ConnectAsync(Guid.NewGuid(), spec);
        await act.Should().NotThrowAsync();
    }
}
