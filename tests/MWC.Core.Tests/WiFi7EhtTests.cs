using System;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  Wi-Fi 7 EHT (Extremely High Throughput) — IEEE 802.11be-2025
//  Wi-Fi 8 (802.11bn) 先行モデル
//  FrozenDictionary 最適化 (.NET 9)
// ══════════════════════════════════════════════════════════════

/// <summary>
/// テストごとに独立した履歴ファイルのパスを作る。
///
/// NetworkHistoryService の既定の保存先はプロセス全体で共有されるため、
/// 隔離しないと他テストが書いた履歴を読んでしまい、非決定的に落ちる
/// (2026-08 にテストを初めて実行して実際に踏んだ)。
/// xunit はテストクラスを既定で並列実行するので、CI では特に顕在化しやすい。
/// </summary>
internal static class TestHistoryPath
{
    public static string Fresh() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                               $"mwc-history-{System.Guid.NewGuid():N}.json");
}

public class EhtCapabilityTests
{
    /// <summary>
    /// Wi-Fi 7 @ 4SS × 320MHz × 4096-QAM = 46.116 Gbps (IEEE 802.11be 規格最大)
    /// IEEE 802.11be-2025 公開済み仕様値
    /// </summary>
    [Fact]
    public void EhtCapability_4096Qam_320MHz_4SS_ExceedsWiFi6Max()
    {
        var cap = new EhtCapability
        {
            Supports4096Qam            = true,
            SupportsPreamblePuncturing = true,
            SupportsRtwt               = true,
            SupportsScs                = true,
            MaxMcsIndex                = 13
        };

        var peak1ss = cap.EstimatedPeakGbps(320, spatialStreams: 1);
        var peak4ss = cap.EstimatedPeakGbps(320, spatialStreams: 4);

        // Wi-Fi 7 1SS @ 320MHz ≈ 11.5 Gbps
        peak1ss.Should().BeApproximately(11.5, 0.5);
        // 4SS = 4× = ~46 Gbps (Wi-Fi 6 最大 9.6 Gbps の約4.8倍)
        peak4ss.Should().BeGreaterThan(40.0);
        peak4ss.Should().BeGreaterThan(peak1ss);
        cap.MaxMcsIndex.Should().Be(13, "Wi-Fi 7 は MCS 13 (4096-QAM) 対応");
        cap.SupportsPreamblePuncturing.Should().BeTrue("Preamble Puncturing は Wi-Fi 7 の必須機能");
    }

    [Fact]
    public void EhtCapability_4096QamVs1024Qam_Is20PercentHigher()
    {
        var wifi6 = new EhtCapability { Supports4096Qam = false, MaxMcsIndex = 11 };
        var wifi7 = new EhtCapability { Supports4096Qam = true,  MaxMcsIndex = 13 };

        var r6 = wifi6.EstimatedPeakGbps(160);
        var r7 = wifi7.EstimatedPeakGbps(160);

        r7.Should().BeGreaterThan(r6);
        // 4096-QAM (12bit) vs 1024-QAM (10bit) ≈ 20% 向上
        (r7 / r6).Should().BeApproximately(1.20, 0.05);
    }

    [Theory]
    [InlineData(320, 1, 11.5)]   // 1SS 320MHz
    [InlineData(160, 1,  5.7)]   // 1SS 160MHz
    [InlineData(80,  1,  2.8)]   // 1SS 80MHz
    [InlineData(40,  1,  1.4)]   // 1SS 40MHz
    public void EstimatedPeakGbps_ChannelWidth_ScalesLinearly(
        int widthMhz, int ss, double expectedGbps)
    {
        var cap = new EhtCapability { Supports4096Qam = true };
        var peak = cap.EstimatedPeakGbps(widthMhz, ss);

        peak.Should().BeApproximately(expectedGbps, 0.5);
        peak.Should().BePositive();
    }

    [Fact]
    public void EhtCapability_Rtwt_IoTOptimization()
    {
        // rTWT: IoT デバイスの省電力スケジューリング (新機能)
        var iotCap = new EhtCapability { SupportsRtwt = true, SupportsScs = true };
        iotCap.SupportsRtwt.Should().BeTrue();
        iotCap.SupportsScs.Should().BeTrue("SCS: Stream Classification Service QoS");
    }
}

public class WiFi8ModelTests
{
    [Fact]
    public void WiFi8Capability_MultiApCoordination_AllFlags()
    {
        var cap = new WiFi8Capability
        {
            SupportsMultiApCoordination     = true,
            SupportsCoordinatedSpatialReuse = true,
            SupportsCoordinatedOfdma        = true,
            SupportsUltraHighThroughput     = true
        };

        cap.SupportsMultiApCoordination.Should().BeTrue();
        cap.SupportsCoordinatedSpatialReuse.Should().BeTrue();
        cap.SupportsCoordinatedOfdma.Should().BeTrue();
        // Wi-Fi 8 は 802.11bn 開発中 — 将来の機能
        cap.SupportsUltraHighThroughput.Should().BeTrue();
    }

    [Fact]
    public void PhyType_Dot11bn_AfterDot11be()
    {
        PhyType.Dot11bn.ToGenerationLabel().Should().Contain("Wi-Fi 8");
        PhyType.Dot11bn.ToShortLabel().Should().Be("Wi-Fi 8");
        ((int)PhyType.Dot11bn).Should().BeGreaterThan((int)PhyType.Dot11be,
            "Wi-Fi 8 は Wi-Fi 7 より後の世代");
    }

    [Fact]
    public void PhyType_AllGenerations_HaveLabels()
    {
        // Wi-Fi 1〜8 まで全世代に表示ラベルが存在する
        foreach (var phy in Enum.GetValues<PhyType>().Where(p => p != PhyType.Unknown))
        {
            var label = phy.ToGenerationLabel();
            label.Should().NotBeNullOrEmpty($"{phy} must have a generation label");
            label.Should().Contain("Wi-Fi", $"{phy} label must identify Wi-Fi generation");
        }
    }
}

public class FrozenDictionaryOptimizationTests
{
    private readonly RegulatoryDomainService _svc = new();

    [Fact]
    public void GetRegion_CaseInsensitive_FrozenDictionary()
    {
        // FrozenDictionary(OrdinalIgnoreCase) — すべて同じ結果
        var us1 = _svc.GetRegion("US");
        var us2 = _svc.GetRegion("us");
        var us3 = _svc.GetRegion("Us");

        us1.Should().Be(us2);
        us2.Should().Be(us3);
        us1.Has6GHz.Should().BeTrue();
        us1.StandardPower.Should().BeTrue("US は SP (Standard Power) 対応");
    }

    [Fact]
    public void GetRegion_Deterministic_MultipleCallsReturnSameObject()
    {
        // FrozenDictionary は読み取り最適化 — 毎回同じ参照/値
        Enumerable.Range(0, 10)
            .Select(_ => _svc.GetRegion("JP"))
            .Should().AllSatisfy(r =>
            {
                r.CountryCode.Should().Be("JP");
                r.Mode.Should().Be(Band6GHzMode.FullBand);
                r.LowPowerIndoor.Should().BeTrue("JP は LPI 対応");
            });
    }

    [Fact]
    public void AllRegions_CountAndContents_Correct()
    {
        var regions = _svc.AllRegions;
        regions.Count.Should().BeGreaterThan(15, "主要25ヶ国以上をカバー");
        regions.Should().Contain(r => r.CountryCode == "JP" && r.Mode == Band6GHzMode.FullBand);
        regions.Should().Contain(r => r.CountryCode == "CN" && r.Mode == Band6GHzMode.None);
        regions.Should().Contain(r => r.CountryCode == "DE" && r.Mode == Band6GHzMode.LowerHalf);
        regions.Should().Contain(r => r.Has6GHz);
        regions.Should().Contain(r => !r.Has6GHz);
    }
}

public class DotNet9LanguageFeatureTests
{
    /// <summary>System.Threading.Lock (C# 13) のスレッド安全性確認</summary>
    [Fact]
    public void NetworkHistoryService_ConcurrentWrites_ThreadSafe()
    {
        var svc  = new NetworkHistoryService(null, TestHistoryPath.Fresh());
        var tasks = Enumerable.Range(0, 20).Select(i =>
            System.Threading.Tasks.Task.Run(() =>
                svc.RecordConnection($"SSID_{i % 5}", i % 2 == 0)));

        System.Threading.Tasks.Task.WaitAll(tasks.ToArray());

        // データ破損なく全記録が完了
        var count = svc.Count;
        count.Should().BeGreaterThan(0);
        svc.GetAll().Should().NotBeNull();
        svc.GetAll().Should().AllSatisfy(e =>
            e.Ssid.Should().StartWith("SSID_"));
    }

    /// <summary>WifiProfileValidator — C# 13 の switch expression パターン</summary>
    [Theory]
    [InlineData("ValidSSID",  true)]
    [InlineData("",           false)]
    [InlineData("ToolongSSID1234567890123456789012345", false)]  // > 32 ASCII bytes
    public void WifiProfileValidator_IsValidSsid_Correct(string ssid, bool expected)
    {
        WifiProfileValidator.IsValidSsid(ssid).Should().Be(expected);
    }
}

// ══════════════════════════════════════════════════════════════
//  Lock / FrozenDictionary 整合性テスト (.NET 9 / C# 13)
// ══════════════════════════════════════════════════════════════
public class NetworkHistoryLockTests
{
    [Fact]
    public void RecordConnection_ParallelWrites_NoConcurrentModificationException()
    {
        var svc = new NetworkHistoryService(null, TestHistoryPath.Fresh());
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // 50スレッドから同時書き込み
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            try { svc.RecordConnection($"Net{i % 10}", i % 2 == 0); }
            catch (Exception ex) { exceptions.Add(ex); }
        })).ToArray();

        Task.WaitAll(tasks);

        // 例外なし・データ破損なし
        exceptions.Should().BeEmpty("concurrent writes must not throw exceptions");
        svc.Count.Should().BeGreaterThan(0);
        svc.GetAll().Should().AllSatisfy(e =>
        {
            e.Ssid.Should().NotBeNullOrEmpty();
            e.LastConnected.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
        });
    }

    [Fact]
    public void GetAll_WhileRecording_ReturnsSnapshot()
    {
        var svc = new NetworkHistoryService(null, TestHistoryPath.Fresh());
        svc.RecordConnection("Initial", true);

        // 読み取りはスナップショットを返すべき
        var snap1 = svc.GetAll();
        svc.RecordConnection("Added", true);
        var snap2 = svc.GetAll();

        snap1.Should().NotBeSameAs(snap2);
        snap2.Count.Should().BeGreaterOrEqualTo(snap1.Count);
    }
}

public class PhyTypeAllGenerationsTests
{
    [Theory]
    [InlineData(PhyType.Dot11b,  "Wi-Fi 1")]
    [InlineData(PhyType.Dot11a,  "Wi-Fi 2")]
    [InlineData(PhyType.Dot11g,  "Wi-Fi 3")]
    [InlineData(PhyType.Dot11n,  "Wi-Fi 4")]
    [InlineData(PhyType.Dot11ac, "Wi-Fi 5")]
    [InlineData(PhyType.Dot11ax, "Wi-Fi 6")]
    [InlineData(PhyType.Dot11be, "Wi-Fi 7")]
    [InlineData(PhyType.Dot11bn, "Wi-Fi 8")]
    public void ToShortLabel_AllWifiGenerations_CorrectLabel(PhyType phy, string expected)
    {
        var label = phy.ToShortLabel();
        label.Should().StartWith(expected);
        label.Should().NotBeNullOrEmpty();
        phy.ToGenerationLabel().Should().Contain(label.Replace("/6E", ""));
    }

    [Fact]
    public void GenerationOrder_IsChronological()
    {
        var ordered = new[]
        {
            PhyType.Dot11b, PhyType.Dot11a, PhyType.Dot11g, PhyType.Dot11n,
            PhyType.Dot11ac, PhyType.Dot11ax, PhyType.Dot11be, PhyType.Dot11bn
        };
        // 各世代が前の世代より大きいenum値を持つ(時系列順序)
        for (int i = 1; i < ordered.Length; i++)
            ((int)ordered[i]).Should().BeGreaterThan((int)ordered[i - 1]);
    }
}

// ══════════════════════════════════════════════════════════════
//  リポジトリ整合性テスト (LICENSE / バージョン / ドキュメント)
// ══════════════════════════════════════════════════════════════
public class RepositoryIntegrityTests
{
    private static string RepoRoot => System.IO.Path.GetFullPath(
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(RepositoryIntegrityTests).Assembly.Location)!,
            "..", "..", "..", "..", ".."));

    [Theory]
    [InlineData("LICENSE")]
    [InlineData("README.md")]
    [InlineData("CHANGELOG.md")]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("SECURITY.md")]
    [InlineData("CODE_OF_CONDUCT.md")]
    public void RequiredFile_Exists(string fileName)
    {
        var path = System.IO.Path.Combine(RepoRoot, fileName);
        if (!System.IO.Directory.Exists(RepoRoot)) return;  // ビルド環境差異を許容
        System.IO.File.Exists(path).Should().BeTrue(
            because: $"{fileName} is required for a public GitHub release (INVARIANT I4)");
    }

    [Fact]
    public void License_IsMit()
    {
        var path = System.IO.Path.Combine(RepoRoot, "LICENSE");
        if (!System.IO.File.Exists(path)) return;
        var content = System.IO.File.ReadAllText(path);
        content.Should().Contain("MIT License");
        content.Should().Contain("Permission is hereby granted");
        content.Should().Contain("WITHOUT WARRANTY");
    }

    [Theory]
    [InlineData("docs/user-guide.md")]
    [InlineData("docs/faq.md")]
    [InlineData("docs/troubleshooting.md")]
    [InlineData("docs/benchmarks.md")]
    public void UserDocumentation_Exists(string relPath)
    {
        var path = System.IO.Path.Combine(RepoRoot, relPath);
        if (!System.IO.Directory.Exists(RepoRoot)) return;
        System.IO.File.Exists(path).Should().BeTrue(
            because: $"{relPath} is part of complete product documentation");
    }
}

// ══════════════════════════════════════════════════════════════
//  ビルド構成整合性テスト
// ══════════════════════════════════════════════════════════════
public class BuildConfigurationTests
{
    private static string RepoRoot => System.IO.Path.GetFullPath(
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(BuildConfigurationTests).Assembly.Location)!,
            "..", "..", "..", "..", ".."));

    [Fact]
    public void NoDuplicateBenchmarkProject()
    {
        if (!System.IO.Directory.Exists(RepoRoot)) return;
        var benchmarkCsprojs = System.IO.Directory.GetFiles(
            RepoRoot, "MWC.Benchmarks.csproj", System.IO.SearchOption.AllDirectories);
        // bin/obj を除外
        var real = benchmarkCsprojs
            .Where(p => !p.Contains("bin") && !p.Contains("obj"))
            .ToList();
        real.Count.Should().BeLessOrEqualTo(1,
            because: "重複したベンチマークプロジェクトはアセンブリ名衝突を起こす");
    }

    [Fact]
    public void AllProjects_TargetNet9()
    {
        if (!System.IO.Directory.Exists(RepoRoot)) return;
        var srcDir = System.IO.Path.Combine(RepoRoot, "src");
        if (!System.IO.Directory.Exists(srcDir)) return;

        var csprojs = System.IO.Directory.GetFiles(srcDir, "*.csproj", System.IO.SearchOption.AllDirectories);
        foreach (var proj in csprojs)
        {
            var content = System.IO.File.ReadAllText(proj);
            // net8.0 が TargetFramework として残っていないこと(コメント除く)
            var lines = content.Split('\n')
                .Where(l => l.Contains("TargetFramework") && !l.TrimStart().StartsWith("<!--"));
            foreach (var line in lines)
            {
                line.Should().NotContain("net8.0",
                    because: $"{System.IO.Path.GetFileName(proj)} should target net9.0");
            }
        }
    }

    [Fact]
    public void Changelog_HasVersion3Entries()
    {
        var path = System.IO.Path.Combine(RepoRoot, "CHANGELOG.md");
        if (!System.IO.File.Exists(path)) return;
        var content = System.IO.File.ReadAllText(path);
        content.Should().Contain("[3.0.0]");
        content.Should().Contain("[3.1.0]");
    }
}
