using System;
using System.IO;
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
//  以前は実ファイル (%LocalAppData%) に対して読み書きしていたため、過去の実行や
//  製品自体の利用で蓄積した件数を読み込み、固定 SSID のテストが再実行のたびに
//  落ちていた (2026-11 実測: 期待 1 件に対して 3 件。tools/run-tests.sh が
//  実行前に実データごと消す回避策を持っていたのがその証拠)。
//  現在は IsolatedStores 経由でテストごとに一時ファイルへ隔離する。
//  実ファイルへの書き込みは二重に防ぐ: テストがユーザーの実データを汚染せず、
//  実データがテスト結果を左右することもない。
// ══════════════════════════════════════════════════════════════
internal static class EapTestSsid
{
    // SSID は IEEE 802.11 の 32 バイト上限があるため、一意化には短い16進サフィックスのみ使う
    // (Guid.NewGuid() の文字列表現は 36 文字あり、接頭辞と合わせると容易に超過する)。
    public static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];
}

/// <summary>
/// テスト専用の隔離済みストア生成。保存先をテストごとに一意な一時ファイルへ向け、
/// 実ユーザーの %LocalAppData%/MWC 配下には一切触れない。
/// </summary>
internal static class IsolatedStores
{
    private static string NewTempFile(string name) => Path.Combine(
        Path.GetTempPath(), "mwc-tests", Guid.NewGuid().ToString("N"), name);

    public static EapAuthStatsService NewEapStatsService() =>
        new(statsPath: NewTempFile("eap-stats.json"));

    public static NetworkHistoryService NewHistoryService() =>
        new(NullLogger<NetworkHistoryService>.Instance, NewTempFile("history.json"));
}

public class EapAuthStatsServiceTests
{
    [Fact]
    public void RecordAttemptSuccessIncrementsSuccessCount()
    {
        var svc = IsolatedStores.NewEapStatsService();
        svc.RecordAttempt("EapTest_Success1", EapType.PEAP_MSCHAPv2, true);

        var stat = svc.GetStat("EapTest_Success1", EapType.PEAP_MSCHAPv2);
        stat.Should().NotBeNull();
        stat!.SuccessCount.Should().Be(1);
        stat.FailCount.Should().Be(0);
    }

    [Fact]
    public void RecordAttemptFailureIncrementsFailCount()
    {
        var svc = IsolatedStores.NewEapStatsService();
        svc.RecordAttempt("EapTest_Fail1", EapType.EAP_TLS, false);

        var stat = svc.GetStat("EapTest_Fail1", EapType.EAP_TLS);
        stat!.FailCount.Should().Be(1);
        stat.SuccessCount.Should().Be(0);
    }

    [Fact]
    public void RecordAttemptSameSsidDifferentEapTypeTracksSeparately()
    {
        var svc = IsolatedStores.NewEapStatsService();
        svc.RecordAttempt("EapTest_MultiType", EapType.PEAP_MSCHAPv2, true);
        svc.RecordAttempt("EapTest_MultiType", EapType.EAP_TLS, false);

        svc.GetStat("EapTest_MultiType", EapType.PEAP_MSCHAPv2)!.SuccessCount.Should().Be(1);
        svc.GetStat("EapTest_MultiType", EapType.EAP_TLS)!.FailCount.Should().Be(1);
    }

    [Fact]
    public void RecordAttemptAccumulatesAcrossCalls()
    {
        var svc = IsolatedStores.NewEapStatsService();
        svc.RecordAttempt("EapTest_Accum", EapType.EAP_TTLS, true);
        svc.RecordAttempt("EapTest_Accum", EapType.EAP_TTLS, true);
        svc.RecordAttempt("EapTest_Accum", EapType.EAP_TTLS, false);

        var stat = svc.GetStat("EapTest_Accum", EapType.EAP_TTLS);
        stat!.SuccessCount.Should().Be(2);
        stat.FailCount.Should().Be(1);
        stat.TotalAttempts.Should().Be(3);
    }

    [Fact]
    public void SuccessRateComputesCorrectly()
    {
        var svc = IsolatedStores.NewEapStatsService();
        svc.RecordAttempt("EapTest_Rate", EapType.EAP_AKA, true);
        svc.RecordAttempt("EapTest_Rate", EapType.EAP_AKA, true);
        svc.RecordAttempt("EapTest_Rate", EapType.EAP_AKA, true);
        svc.RecordAttempt("EapTest_Rate", EapType.EAP_AKA, false);

        svc.GetStat("EapTest_Rate", EapType.EAP_AKA)!.SuccessRate.Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public void SuccessRateNoAttemptsDefaultsToOptimistic()
    {
        // 記録が無い合成インスタンスは 1.0 (楽観値) を返す仕様。
        var stat = new EapAuthStat("Unrecorded", EapType.PEAP_MSCHAPv2, 0, 0, DateTimeOffset.UtcNow);
        stat.SuccessRate.Should().Be(1.0);
    }

    [Fact]
    public void GetStatUnknownSsidReturnsNull()
    {
        var svc = IsolatedStores.NewEapStatsService();
        svc.GetStat(EapTestSsid.Unique("EapTest_DoesNotExist_"), EapType.PEAP_MSCHAPv2).Should().BeNull();
    }

    [Fact]
    public void GetAllIncludesRecordedEntry()
    {
        var svc = IsolatedStores.NewEapStatsService();
        var uniqueSsid = EapTestSsid.Unique("EapTest_GetAll_");
        svc.RecordAttempt(uniqueSsid, EapType.EAP_TLS, true);

        svc.GetAll().Should().Contain(e => e.Ssid == uniqueSsid && e.EapType == EapType.EAP_TLS);
    }

    [Fact]
    public void InstancesWithDifferentPathsDoNotShareState()
    {
        // 回帰防止: 保存先が static 固定だった頃は全インスタンスが実ファイルを
        // 共有し、他所で記録された件数を読み込んでこの種のテストが落ちた。
        var pathA = Path.Combine(Path.GetTempPath(), "mwc-tests", Guid.NewGuid().ToString("N"), "eap-stats.json");
        var pathB = Path.Combine(Path.GetTempPath(), "mwc-tests", Guid.NewGuid().ToString("N"), "eap-stats.json");
        var ssid = EapTestSsid.Unique("EapTest_Isolation_");

        var a = new EapAuthStatsService(statsPath: pathA);
        a.RecordAttempt(ssid, EapType.EAP_TLS, true);

        var b = new EapAuthStatsService(statsPath: pathB);
        b.GetStat(ssid, EapType.EAP_TLS).Should().BeNull();

        // 同じパスを指定した別インスタンスは永続化された内容を読む (製品側の意味論)。
        var a2 = new EapAuthStatsService(statsPath: pathA);
        a2.GetStat(ssid, EapType.EAP_TLS)!.SuccessCount.Should().Be(1);
    }

    [Fact]
    public void DefaultPathIsTheLocalAppDataStore()
    {
        // 引数なし構築 (App/CLI の DI) が従来の保存先を指し続けることの固定。
        EapAuthStatsService.DefaultStatsPath.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MWC", "eap-stats.json"));
    }
}

// ══════════════════════════════════════════════════════════════
//  ConnectionExecutor → EapAuthStatsService wiring
// ══════════════════════════════════════════════════════════════
public class ConnectionExecutorEapStatsWiringTests
{
    [Fact]
    public async Task ConnectAsyncEnterpriseSpecSuccessRecordsEapStat()
    {
        var wifi = new FakeWifiService { NextConnectResult = ConnectionResult.Ok("ignored", true, false) };
        var hist = IsolatedStores.NewHistoryService();
        var eapStats = IsolatedStores.NewEapStatsService();
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
    public async Task ConnectAsyncEnterpriseSpecFailureRecordsEapFailure()
    {
        var wifi = new FakeWifiService { NextConnectResult = ConnectionResult.Fail(ConnectionFailure.BadCredentials) };
        var hist = IsolatedStores.NewHistoryService();
        var eapStats = IsolatedStores.NewEapStatsService();
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
    public async Task ConnectAsyncPersonalAuthDoesNotRecordEapStat()
    {
        // WPA2PSK には EapType がないため、EAP 統計には一切記録されないはず。
        var wifi = new FakeWifiService { NextConnectResult = ConnectionResult.Ok("ignored", true, false) };
        var hist = IsolatedStores.NewHistoryService();
        var eapStats = IsolatedStores.NewEapStatsService();
        var exec = new ConnectionExecutor(
            wifi, hist, NullLogger<ConnectionExecutor>.Instance, eapStats);

        var ssid = EapTestSsid.Unique("EapWiring_Psk_");
        var result = await exec.ConnectAsync(Guid.NewGuid(), ssid, AuthMethod.WPA2PSK, "pass12345");

        result.Success.Should().BeTrue();
        eapStats.GetAll().Should().NotContain(e => e.Ssid == ssid);
    }

    [Fact]
    public async Task ConnectAsyncNoEapStatsProvidedDoesNotThrow()
    {
        // 既存の 3 引数コンストラクタ(EapAuthStatsService 省略)との後方互換性確認。
        var wifi = new FakeWifiService { NextConnectResult = ConnectionResult.Ok("ignored", true, false) };
        var hist = IsolatedStores.NewHistoryService();
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
