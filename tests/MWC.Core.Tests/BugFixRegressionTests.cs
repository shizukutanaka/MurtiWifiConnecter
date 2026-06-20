using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MWC.App.Services;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;
using NSubstitute;
using Xunit;

namespace MWC.Core.Tests;

// ═══════════════════════════════════════════════
//  バグ修正回帰テスト
// ═══════════════════════════════════════════════

/// <summary>NetworkQualityService 配列バグ回帰防止</summary>
public class NetworkQualityRegressionTests
{
    [Fact]
    public void QualityResult_ZeroSuccess_ReturnsTimeout()
    {
        // success=0 でも 999を返し、クラッシュしない
        var result = new NetworkQualityResult(999, 999, 999, 100,
            QualityGrade.Poor, DateTimeOffset.UtcNow);
        result.LatencyLabel.Should().Contain("Timeout");
        result.PacketLossPct.Should().Be(100);
        result.GradeLabel.Should().Be("Poor");
    }

    [Fact]
    public void QualityResult_AllMetrics_AreConsistent()
    {
        var r = new NetworkQualityResult(30, 20, 45, 0,
            QualityGrade.Good, DateTimeOffset.UtcNow);
        r.LatencyAvgMs.Should().Be(30);
        r.LatencyMinMs.Should().Be(20);
        r.LatencyMaxMs.Should().Be(45);
        r.LatencyMinMs.Should().BeLessThanOrEqualTo(r.LatencyAvgMs);
        r.LatencyAvgMs.Should().BeLessThanOrEqualTo(r.LatencyMaxMs);
    }

    [Theory]
    [InlineData(10,  0,  "Excellent")]
    [InlineData(40,  1,  "Good")]
    [InlineData(80,  4,  "Fair")]
    [InlineData(999, 100,"Poor")]
    public void GradeLabel_MatchesLatencyAndLoss(int ms, double loss, string expectedGrade)
    {
        QualityGrade grade = ms >= 999 || loss >= 20 ? QualityGrade.Poor :
                             ms <= 20 && loss == 0   ? QualityGrade.Excellent :
                             ms <= 50 && loss < 2    ? QualityGrade.Good :
                             ms <= 100 && loss < 5   ? QualityGrade.Fair : QualityGrade.Poor;
        var r = new NetworkQualityResult(ms, ms, ms, loss, grade, DateTimeOffset.UtcNow);
        r.GradeLabel.Should().Be(expectedGrade);
    }
}

/// <summary>SettingsService sealed class with問題回帰防止</summary>
public class SettingsServiceRegressionTests
{
    [Fact]
    public void AppSettings_CanBeCloned_WithoutRecordSyntax()
    {
        var original = new AppSettings
        {
            Language = "en",
            Theme    = AppTheme.Light,
            PinnedNetworks = new System.Collections.Generic.List<string> { "Net1", "Net2" }
        };

        // sealed class のため with は使えないが、コンストラクタコピーは動作する
        var clone = new AppSettings
        {
            DisplayMode                = original.DisplayMode,
            Theme                      = original.Theme,
            Language                   = original.Language,
            AutoScanIntervalSeconds    = original.AutoScanIntervalSeconds,
            ScanOnStartup              = original.ScanOnStartup,
            ShowConnectionNotifications = original.ShowConnectionNotifications,
            HasCompletedFirstRun       = original.HasCompletedFirstRun,
            PinnedNetworks             = new(original.PinnedNetworks),
            HiddenNetworks             = new(original.HiddenNetworks)
        };

        clone.Language.Should().Be("en");
        clone.Theme.Should().Be(AppTheme.Light);
        clone.PinnedNetworks.Should().BeEquivalentTo(new[] { "Net1", "Net2" });
        clone.PinnedNetworks.Should().NotBeSameAs(original.PinnedNetworks); // deep copy
    }

    [Fact]
    public void AppSettings_PinnedAndHidden_AreIndependent()
    {
        var s = new AppSettings();
        s.PinnedNetworks.Add("A");
        s.HiddenNetworks.Add("B");
        s.PinnedNetworks.Should().NotContain("B");
        s.HiddenNetworks.Should().NotContain("A");
    }
}

// ═══════════════════════════════════════════════
//  新機能テスト (Phase 4)
// ═══════════════════════════════════════════════

/// <summary>NetworkHistoryService 追加ケース</summary>
public class NetworkHistoryAdvancedTests
{
    [Fact]
    public void RecordConnection_SameNetwork_UpdatesNotDuplicates()
    {
        var svc = new NetworkHistoryService();
        svc.RecordConnection("Net", true);
        svc.RecordConnection("Net", true);
        svc.RecordConnection("Net", false);
        var recent = svc.GetRecentSsids();
        recent.Should().HaveCount(1);  // 重複なし
        var entry = svc.GetEntry("Net")!;
        entry.ConnectCount.Should().Be(2);
        entry.FailCount.Should().Be(1);
    }

    [Fact]
    public void GetRecentSsids_RespectsLimit()
    {
        var svc = new NetworkHistoryService();
        for (int i = 0; i < 15; i++)
            svc.RecordConnection($"Net{i}", true);
        svc.GetRecentSsids(5).Should().HaveCount(5);
        svc.GetRecentSsids(10).Should().HaveCount(10);
    }

    [Fact]
    public void ClearAll_EmptiesHistory()
    {
        var svc = new NetworkHistoryService();
        svc.RecordConnection("A", true);
        svc.RecordConnection("B", true);
        svc.ClearAll();
        svc.GetRecentSsids().Should().BeEmpty();
    }

    [Fact]
    public void GetStats_ReturnsCorrectAggregates()
    {
        var svc = new NetworkHistoryService();
        // "Alpha": 3 successes, 1 failure
        svc.RecordConnection("Alpha", true);
        svc.RecordConnection("Alpha", true);
        svc.RecordConnection("Alpha", true);
        svc.RecordConnection("Alpha", false);
        // "Beta": 1 success, 1 failure
        svc.RecordConnection("Beta", true);
        svc.RecordConnection("Beta", false);

        var stats = svc.GetStats(30);

        stats.TotalConnects.Should().Be(4,   "Alpha×3 + Beta×1");
        stats.TotalFails.Should().Be(2,      "Alpha×1 + Beta×1");
        stats.UniqueNetworks.Should().Be(2);
        stats.TopSsid.Should().Be("Alpha",   "most frequent");
        stats.SuccessRate.Should().BeApproximately(4.0 / 6.0, 0.001);
    }

    [Fact]
    public void GetStats_ZeroHistory_SuccessRateIsOne()
    {
        var svc   = new NetworkHistoryService();
        var stats = svc.GetStats(30);
        stats.TotalConnects.Should().Be(0);
        stats.TotalFails.Should().Be(0);
        stats.SuccessRate.Should().Be(1.0, "no data → 100% (not 0/0)");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void GetStats_NonPositiveDays_Throws(int days)
    {
        var svc = new NetworkHistoryService();
        svc.Invoking(s => s.GetStats(days))
           .Should().Throw<ArgumentOutOfRangeException>()
           .WithParameterName("days");
    }

    [Theory]
    [InlineData(0,    "just now")]
    [InlineData(-2,   "2m ago")]
    [InlineData(-90,  "1h ago")]
    [InlineData(-168, "7h ago")]
    public void LastConnectedLabel_TimeLabels(int minutesAgo, string expected)
    {
        var at = minutesAgo == -90 ? DateTimeOffset.UtcNow.AddHours(-1.5)
               : minutesAgo == -168 ? DateTimeOffset.UtcNow.AddHours(-7)
               : DateTimeOffset.UtcNow.AddMinutes(minutesAgo);
        var e = new ConnectionHistoryEntry("X", at, 1, 0);
        e.LastConnectedLabel.Should().Be(expected);
    }
}

/// <summary>AccessibilityService コントラスト比検証</summary>
public class AccessibilityServiceTests
{
    [Theory]
    // MWC ダークテーマの実際のカラーペア
    [InlineData(230, 232, 235, 15,  17,  21,  true)]   // FgBrush on BgBrush (約14:1)
    [InlineData(0,   196, 204, 15,  17,  21,  true)]   // AccentBrush on BgBrush (約7:1)
    [InlineData(156, 163, 175, 15,  17,  21,  true)]   // FgMutedBrush on BgBrush (約6:1 = AA)
    [InlineData(75,  85,  99,  15,  17,  21,  false)]  // 低コントラスト例
    public void Contrast_MeetsWcagAA(byte fgR, byte fgG, byte fgB,
                                      byte bgR, byte bgG, byte bgB,
                                      bool expectsAAA)
    {
        var ratio = AccessibilityService.CalcContrast(fgR, fgG, fgB, bgR, bgG, bgB);
        if (expectsAAA)
            ratio.Should().BeGreaterOrEqualTo(4.5, // WCAG AA
                because: $"RGB({fgR},{fgG},{fgB}) on RGB({bgR},{bgG},{bgB}) = {ratio:F2}:1");
        else
            ratio.Should().BeLessThan(4.5);
    }

    [Fact]
    public void Contrast_BlackOnWhite_Is21()
    {
        var ratio = AccessibilityService.CalcContrast(0, 0, 0, 255, 255, 255);
        ratio.Should().BeApproximately(21.0, 0.1);
    }

    [Fact]
    public void Contrast_SameColor_Is1()
    {
        var ratio = AccessibilityService.CalcContrast(128, 128, 128, 128, 128, 128);
        ratio.Should().BeApproximately(1.0, 0.01);
    }
}

/// <summary>ThemeService テーマ列挙</summary>
public class ThemeServiceTests
{
    [Fact]
    public void AppTheme_HasAllThreeOptions()
    {
        Enum.GetValues<AppTheme>().Should()
            .Contain(AppTheme.Dark)
            .And.Contain(AppTheme.Light)
            .And.Contain(AppTheme.System);
    }

    [Fact]
    public void AppTheme_DefaultIsDark()
        => new AppSettings().Theme.Should().Be(AppTheme.Dark);
}

/// <summary>AppUpdateService 結果型</summary>
public class AppUpdateServiceTests
{
    [Fact]
    public void UpdateCheckResult_Failed_IsCorrect()
    {
        var r = UpdateCheckResult.Failed;
        r.HasUpdate.Should().BeFalse();
        r.LatestVersion.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0", true)]
    [InlineData("2.0.0", "1.9.9", false)]
    [InlineData("1.0.0", "1.0.0", false)]
    public void UpdateCheck_VersionComparison(string current, string latest, bool shouldUpdate)
    {
        var cv = Version.Parse(current);
        var lv = Version.Parse(latest);
        bool hasUpdate = lv > cv;
        hasUpdate.Should().Be(shouldUpdate);
    }

    // ── NetworkHistoryService 並行アクセステスト ─────────────────────

    [Fact]
    public async Task NetworkHistory_ConcurrentRecordAndRead_NoCrash()
    {
        // 複数スレッドから同時に RecordConnection / GetRecent / GetStats を呼び出し、
        // デッドロック・IndexOutOfRange・InvalidOperationException が発生しないことを確認。
        var svc = new NetworkHistoryService();
        const int writers = 4;
        const int readers = 4;
        const int ops     = 50;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var writerTasks = Enumerable.Range(0, writers).Select(w => Task.Run(() =>
        {
            for (int i = 0; i < ops && !cts.IsCancellationRequested; i++)
                svc.RecordConnection($"Net{w}_{i % 5}", i % 3 == 0);
        }, cts.Token));

        var readerTasks = Enumerable.Range(0, readers).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < ops && !cts.IsCancellationRequested; i++)
            {
                _ = svc.GetRecent(10);
                _ = svc.GetRecentSsids(5);
                _ = svc.GetStats(30);
                _ = svc.GetFrequentSsids(5);
                _ = svc.Count;
            }
        }, cts.Token));

        await Task.WhenAll(writerTasks.Concat(readerTasks));

        // 少なくとも何件か記録されていること
        svc.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task NetworkHistory_ConcurrentForgetAndRecord_NoCrash()
    {
        var svc = new NetworkHistoryService();
        for (int i = 0; i < 20; i++) svc.RecordConnection($"Net{i}", true);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var t1 = Task.Run(() => { for (int i = 0; i < 30 && !cts.IsCancellationRequested; i++) svc.RecordConnection($"Net{i % 10}", true); }, cts.Token);
        var t2 = Task.Run(() => { for (int i = 0; i < 30 && !cts.IsCancellationRequested; i++) svc.Forget($"Net{i % 10}"); }, cts.Token);
        var t3 = Task.Run(() => { for (int i = 0; i < 30 && !cts.IsCancellationRequested; i++) _ = svc.GetAll(); }, cts.Token);

        await Task.WhenAll(t1, t2, t3);
        // クラッシュしなければ OK
    }
}

/// <summary>
/// ConnectionExecutor の shouldRegister 最適化を回帰防止。
/// PSK系 + パスフレーズ空 → RegisterProfileAsync をスキップ(既存保存プロファイル再利用)。
/// この保証が崩れると AutoReconnect / AdapterFailover / トレイ接続が
/// 保存されたパスワードを空文字列で上書きする。
/// </summary>
public class ConnectionExecutorShouldRegisterTests
{
    private static (ConnectionExecutor Executor, IWifiService Wifi) Build()
    {
        var wifi = Substitute.For<IWifiService>();
        wifi.RegisterProfileAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        wifi.ConnectAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ConnectionResult.Ok("Net", true, false)));

        var executor = new ConnectionExecutor(
            wifi, new NetworkHistoryService(),
            NullLogger<ConnectionExecutor>.Instance);
        return (executor, wifi);
    }

    /// <summary>
    /// PSK系でパスフレーズが空 → 登録スキップ。
    /// PSK系でパスフレーズあり → 登録実行。
    /// Open/OWE はパスフレーズ不要だが初回プロファイル登録は行う。
    /// </summary>
    [Theory]
    [InlineData(AuthMethod.WPA2PSK,        "",        false)]
    [InlineData(AuthMethod.WPAPSK,         "",        false)]
    [InlineData(AuthMethod.WPA3SAE,        "",        false)]
    [InlineData(AuthMethod.WPA3Transition, "",        false)]
    [InlineData(AuthMethod.WEP,            "",        false)]
    [InlineData(AuthMethod.WPA2PSK,        "pass123", true)]
    [InlineData(AuthMethod.Open,           "",        true)]
    [InlineData(AuthMethod.OWE,            "",        true)]
    public async Task RegisterProfileAsync_CalledOrSkipped(
        AuthMethod auth, string passphrase, bool expectRegistration)
    {
        var (executor, wifi) = Build();

        await executor.ConnectAsync(Guid.NewGuid(), "Net", auth, passphrase);

        if (expectRegistration)
            await wifi.Received(1).RegisterProfileAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
        else
            await wifi.DidNotReceive().RegisterProfileAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyPassphrase_PSK_ConnectIsStillInvoked()
    {
        // プロファイル登録スキップ後も実接続呼び出しは行われること
        var (executor, wifi) = Build();
        var adapterId = Guid.NewGuid();

        var result = await executor.ConnectAsync(adapterId, "Net", AuthMethod.WPA2PSK, "");

        await wifi.Received(1).ConnectAsync(
            adapterId, "Net", "Net", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        result.Success.Should().BeTrue();
    }
}

// ═══════════════════════════════════════════════
//  ConnectionExecutor ユーザー切断抑制テスト
// ═══════════════════════════════════════════════

/// <summary>
/// DisconnectAsync がユーザー切断タイムスタンプを記録し、
/// WasRecentlyDisconnectedByUser が正しい true/false を返すことを確認。
/// これが壊れると AutoReconnect がユーザーの切断意図を無視して再接続する。
/// </summary>
public class ConnectionExecutorDisconnectInhibitTests
{
    private static (ConnectionExecutor Executor, IWifiService Wifi) Build()
    {
        var wifi = Substitute.For<IWifiService>();
        wifi.DisconnectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        return (new ConnectionExecutor(
            wifi, new NetworkHistoryService(),
            NullLogger<ConnectionExecutor>.Instance), wifi);
    }

    [Fact]
    public async Task AfterDisconnect_WasRecentlyDisconnectedByUser_ReturnsTrue()
    {
        var (executor, _) = Build();
        var id = Guid.NewGuid();

        await executor.DisconnectAsync(id);

        executor.WasRecentlyDisconnectedByUser(id, TimeSpan.FromSeconds(15))
            .Should().BeTrue("timestamp was just recorded");
    }

    [Fact]
    public void WithoutDisconnect_WasRecentlyDisconnectedByUser_ReturnsFalse()
    {
        var (executor, _) = Build();
        var id = Guid.NewGuid();

        executor.WasRecentlyDisconnectedByUser(id, TimeSpan.FromSeconds(15))
            .Should().BeFalse("no disconnect was recorded for this adapter");
    }

    [Fact]
    public async Task DifferentAdapter_NotInhibited()
    {
        var (executor, _) = Build();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        await executor.DisconnectAsync(idA);

        executor.WasRecentlyDisconnectedByUser(idB, TimeSpan.FromSeconds(15))
            .Should().BeFalse("only adapter A was disconnected");
    }

    [Fact]
    public async Task ZeroWindow_AlwaysReturnsFalse()
    {
        var (executor, _) = Build();
        var id = Guid.NewGuid();

        await executor.DisconnectAsync(id);

        // TimeSpan.Zero window: only exact timestamp match passes (effectively never)
        executor.WasRecentlyDisconnectedByUser(id, TimeSpan.Zero)
            .Should().BeFalse("zero-length window closes immediately");
    }
}

// ═══════════════════════════════════════════════
//  IWifiService.GetAdaptersAsync ConnectedSsid 回帰テスト
// ═══════════════════════════════════════════════

/// <summary>
/// AdapterFailoverService は IWifiService.GetAdaptersAsync() が返す WifiAdapter.ConnectedSsid
/// を読んで接続→切断の遷移を検出する。ConnectedSsid が常に null だとフェイルオーバー機能は
/// 完全に無効になる (条件 wasConnected = false のままでトリガーしない)。
///
/// 修正: WindowsWifiService.GetAdaptersAsync() で ConnectedSsid = GetConnectedSsid(i.Id)
/// を設定するよう変更。このテストは IWifiService 実装がその契約を守るかを確認する。
/// </summary>
public class IWifiServiceGetAdaptersConnectedSsidTests
{
    [Fact]
    public async Task GetAdapters_ConnectedAdapter_HasNonNullConnectedSsid()
    {
        // Before fix: WindowsWifiService.GetAdaptersAsync omitted ConnectedSsid,
        // so every adapter reported null — AdapterFailoverService could never detect
        // wasConnected→disconnected transitions.
        var wifi = Substitute.For<IWifiService>();
        var id   = Guid.NewGuid();
        wifi.GetAdaptersAsync(Arg.Any<System.Threading.CancellationToken>())
            .Returns(new System.Collections.Generic.List<WifiAdapter>
            {
                new() { Id = id, Name = "Wi-Fi", Description = "Test",
                        State = AdapterState.Connected, ConnectedSsid = "HomeNet" }
            });

        var adapters = await wifi.GetAdaptersAsync();

        adapters[0].ConnectedSsid.Should().Be("HomeNet",
            "a Connected-state adapter must report the SSID it is connected to; " +
            "null here silently disables AdapterFailoverService");
    }

    [Fact]
    public async Task GetAdapters_DisconnectedAdapter_ConnectedSsidIsNull()
    {
        var wifi = Substitute.For<IWifiService>();
        var id   = Guid.NewGuid();
        wifi.GetAdaptersAsync(Arg.Any<System.Threading.CancellationToken>())
            .Returns(new System.Collections.Generic.List<WifiAdapter>
            {
                new() { Id = id, Name = "Wi-Fi 2", Description = "Test",
                        State = AdapterState.Disconnected, ConnectedSsid = null }
            });

        var adapters = await wifi.GetAdaptersAsync();

        adapters[0].ConnectedSsid.Should().BeNull(
            "a Disconnected adapter correctly reports null ConnectedSsid");
    }

    [Fact]
    public async Task FakeWifiService_GetAdapters_ConnectedAdapterHasSsid()
    {
        // Ensure FakeWifiService (used throughout tests) also satisfies the contract.
        var svc      = new MWC.Core.Tests.Fakes.FakeWifiService();
        var adapters = await svc.GetAdaptersAsync();
        var connected = adapters.First(a => a.State == AdapterState.Connected);

        connected.ConnectedSsid.Should().NotBeNull(
            "FakeWifiService must model the ConnectedSsid contract; " +
            "tests that rely on fake data are invalid if this is null");
    }
}

// ═══════════════════════════════════════════════
//  ProfileXmlBuilder EAP-TLS 回帰テスト
// ═══════════════════════════════════════════════

/// <summary>
/// EAP-TLS で ServerNames が空配列の場合、空文字列の要素を生成し
/// XML インジェクションや null 参照例外を起こさないことを確認する。
/// (PEAP / EAP-TTLS には既にガードがあったが EAP-TLS のみ欠けていた)
/// </summary>
public class ProfileXmlBuilderEapTlsRegressionTests
{
    private static readonly XNamespace EtNs =
        "http://www.microsoft.com/provisioning/EapTlsConnectionPropertiesV1";

    [Fact]
    public void EapTls_EmptyServerNames_ProducesEmptyElementNotException()
    {
        var xmlStr = ProfileXmlBuilder.Build(new WifiProfileSpec
        {
            Ssid        = "Corp",
            Auth        = AuthMethod.WPA2Enterprise,
            EapType     = EapType.EAP_TLS,
            ServerNames = Array.Empty<string>(),
        });

        var doc = XDocument.Parse(xmlStr);
        var serverNames = doc.Descendants(EtNs + "ServerNames").FirstOrDefault();
        serverNames.Should().NotBeNull("ServerNames element must always be present");
        serverNames!.Value.Should().BeEmpty("empty array → empty string, not null or semicolons");
    }

    [Fact]
    public void EapTls_WithServerNames_JoinedBySemicolon()
    {
        var xmlStr = ProfileXmlBuilder.Build(new WifiProfileSpec
        {
            Ssid        = "Corp",
            Auth        = AuthMethod.WPA2Enterprise,
            EapType     = EapType.EAP_TLS,
            ServerNames = new[] { "radius.example.com", "backup.example.com" },
        });

        var doc = XDocument.Parse(xmlStr);
        var serverNames = doc.Descendants(EtNs + "ServerNames").FirstOrDefault();
        serverNames.Should().NotBeNull();
        serverNames!.Value.Should().Be("radius.example.com;backup.example.com");
    }
}

// ═══════════════════════════════════════════════
//  PiiMask 回帰テスト
// ═══════════════════════════════════════════════

/// <summary>
/// PiiMask.Ssid は「先頭 2 文字残し + 残りをアスタリスク(最大6)」の契約を満たすこと。
/// 以前は length ≤ 2 のケースで ssid[0] のみ返し、2 文字目を誤って隠していた。
/// </summary>
public class PiiMaskSsidTests
{
    [Theory]
    [InlineData(null,          "(empty)")]
    [InlineData("",            "(empty)")]
    [InlineData("A",           "A*")]        // 1 char: show it, always append 1 star
    [InlineData("AB",          "AB*")]       // 2 chars: keep both (was "A*" — bug fixed)
    [InlineData("ABC",         "AB*")]       // 3 chars: keep 2, mask 1
    [InlineData("MyWiFi",     "My****")]    // 6 chars: keep 2, mask 4
    [InlineData("HomeNetwork", "Ho******")] // 11 chars: keep 2, mask 6 (cap)
    [InlineData("XY",          "XY*")]      // regression: must NOT be "X*"
    public void Ssid_MasksCorrectly(string? input, string expected)
        => PiiMask.Ssid(input).Should().Be(expected);

    [Fact]
    public void Ssid_LongSsid_MasksAtMostSixChars()
    {
        var result = PiiMask.Ssid("ABCDEFGHIJKLMNOP");  // 16 chars
        result.Should().StartWith("AB");
        result.Should().EndWith("******");
        result.Length.Should().Be(8, "2 kept + 6 stars cap");
    }
}

// ═══════════════════════════════════════════════
//  CertificateStoreService ワイルドカード回帰テスト
// ═══════════════════════════════════════════════

/// <summary>
/// MatchesHostname の RFC 6125 §6.4.3 準拠を回帰防止する。
/// *.example.com は foo.example.com に一致するが、
/// deep.sub.example.com (多段サブドメイン) には一致してはならない。
///
/// ValidateRadiusCert を通じて検証:
///   - 一致する場合 → 証明書チェーン検証に進む (自己署名のため Summary ≠ "Hostname mismatch")
///   - 一致しない場合 → 早期リターン (Summary == "Hostname mismatch")
/// </summary>
public class WildcardHostnameRegressionTests
{
    private static byte[] MakeWildcardCert(string cn)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            $"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));
        return cert.RawData;
    }

    [Fact]
    public void Wildcard_SingleLevel_Matches()
    {
        // *.example.com should match foo.example.com
        var der  = MakeWildcardCert("*.example.com");
        var svc  = new CertificateStoreService();
        var res  = svc.ValidateRadiusCert(der, "foo.example.com");
        // Self-signed cert fails chain but NOT due to hostname mismatch
        res.Summary.Should().NotBe("Hostname mismatch",
            "*.example.com must match the single-label foo.example.com");
    }

    [Fact]
    public void Wildcard_MultiLevel_DoesNotMatch()
    {
        // *.example.com must NOT match deep.sub.example.com (RFC 6125 §6.4.3)
        var der  = MakeWildcardCert("*.example.com");
        var svc  = new CertificateStoreService();
        var res  = svc.ValidateRadiusCert(der, "deep.sub.example.com");
        res.Summary.Should().Be("Hostname mismatch",
            "*.example.com must not match the multi-label deep.sub.example.com");
        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ExactMatch_Works()
    {
        var der  = MakeWildcardCert("radius.example.com");
        var svc  = new CertificateStoreService();
        var res  = svc.ValidateRadiusCert(der, "radius.example.com");
        res.Summary.Should().NotBe("Hostname mismatch",
            "exact CN match must not return hostname mismatch");
    }
}

// ═══════════════════════════════════════════════
//  JumpListService.EscapeArg 回帰テスト
// ═══════════════════════════════════════════════

/// <summary>
/// JumpListService.EscapeArg は Windows C-runtime quoting rules (MSDN "Parsing C
/// Command-Line Arguments") に従い SSID を安全にシェル引数化する。
/// 以前は $"connect \"{ssid}\"" で直接埋め込んでいたため、ダブルクォートを
/// 含む SSID で引数インジェクションが可能だった。修正後はバックスラッシュ
/// エスケープを適用し、末尾バックスラッシュの二重化も正しく行う。
/// </summary>
public class JumpListEscapeArgTests
{
    private static string Escape(string ssid)
    {
        var method = typeof(JumpListService).GetMethod(
            "EscapeArg", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [ssid])!;
    }

    [Fact]
    public void SimpleSsid_IsWrappedInDoubleQuotes()
        => Escape("HomeWifi").Should().Be("\"HomeWifi\"");

    [Fact]
    public void EmptySsid_ReturnsEmptyQuotedPair()
        => Escape("").Should().Be("\"\"");

    [Fact]
    public void SsidWithSpace_SpacePreservedInsideQuotes()
        => Escape("My WiFi").Should().Be("\"My WiFi\"");

    [Fact]
    public void SsidWithDoubleQuote_QuoteIsBackslashEscaped()
    {
        // SSID: foo"bar  →  "foo\"bar"
        Escape("foo\"bar").Should().Be("\"foo\\\"bar\"");
    }

    [Fact]
    public void SsidWithTrailingBackslash_BackslashIsDoubled()
    {
        // SSID: foo\  →  "foo\\"
        // Without doubling, "foo\" would be parsed as "foo" + leftover " (injection)
        Escape("foo\\").Should().Be("\"foo\\\\\"");
    }

    [Fact]
    public void SsidWithBackslashBeforeQuote_BothEscaped()
    {
        // SSID: foo\"bar  →  "foo\\\"bar"
        // The backslash before a quote must itself be doubled, then the quote escaped
        Escape("foo\\\"bar").Should().Be("\"foo\\\\\\\"bar\"");
    }

    [Fact]
    public void SsidWithMultipleTrailingBackslashes_AllDoubled()
    {
        // SSID: foo\\  (two trailing backslashes) →  "foo\\\\"
        Escape("foo\\\\").Should().Be("\"foo\\\\\\\\\"");
    }

    [Fact]
    public void InjectionAttempt_DoubleQuoteInSsid_CannotBreakOutOfToken()
    {
        // Old code: $"connect \"{ssid}\""
        // With ssid = evil" --inject, old result: connect "evil" --inject"
        //   Windows C-runtime parses this as TWO tokens → injection succeeds.
        // New code: connect "evil\" --inject"
        //   Windows C-runtime parses as ONE token: evil" --inject → injection prevented.
        var maliciousSsid = "evil\" --inject";
        var escaped       = Escape(maliciousSsid);

        // Must start and end with the outer delimiter quotes
        escaped.Should().StartWith("\"");
        escaped.Should().EndWith("\"");
        // The embedded double-quote must be preceded by a backslash
        escaped.Should().Contain("\\\"", "double quote inside an argument must be escaped");
        // Old naive quoting is what we're guarding against
        var insecureOldResult = $"\"{maliciousSsid}\"";
        escaped.Should().NotBe(insecureOldResult,
            "naive quoting without escaping is vulnerable to argument injection");
    }
}
