using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  ChannelPlannerService — 自 AP 用の最適チャネル推奨
// ══════════════════════════════════════════════════════════════
public class ChannelPlannerServiceTests
{
    private readonly ChannelPlannerService _svc = new();

    private static WifiNetwork Ap(WifiBand band, int channel, int quality, int widthMhz = 20) =>
        new() { Ssid = $"n{channel}", Band = band, Channel = channel,
                SignalQuality = quality, ChannelWidth = widthMhz };

    // ── 候補集合 ──────────────────────────────────────────────

    [Fact]
    public void Recommend_24GHz_OnlyConsiders_1_6_11()
    {
        var rec = _svc.Recommend(WifiBand.Band2_4GHz, new List<WifiNetwork>());
        rec!.Ranked.Select(r => r.Channel).Should().BeEquivalentTo(new[] { 1, 6, 11 });
    }

    [Fact]
    public void Recommend_5GHz_ExcludesDfsByDefault_IncludesWhenAsked()
    {
        var noDfs = _svc.Recommend(WifiBand.Band5GHz, new List<WifiNetwork>())!;
        noDfs.Ranked.Select(r => r.Channel).Should().NotContain(52);   // DFS 除外
        noDfs.Ranked.Select(r => r.Channel).Should().Contain(36);

        var withDfs = _svc.Recommend(WifiBand.Band5GHz, new List<WifiNetwork>(), includeDfs: true)!;
        withDfs.Ranked.Select(r => r.Channel).Should().Contain(52);    // DFS 含む
    }

    [Fact]
    public void Recommend_6GHz_UsesPscChannels()
    {
        var rec = _svc.Recommend(WifiBand.Band6GHz, new List<WifiNetwork>())!;
        rec.Ranked.Select(r => r.Channel).Should()
            .BeEquivalentTo(SixGhzChannelHelper.PreferredScanningChannels);
    }

    [Fact]
    public void Recommend_UnknownBand_ReturnsNull()
        => _svc.Recommend(WifiBand.Unknown, new List<WifiNetwork>()).Should().BeNull();

    // ── スコアリング ──────────────────────────────────────────

    [Fact]
    public void Recommend_EmptyScan_AllChannelsClear_Score100()
    {
        var rec = _svc.Recommend(WifiBand.Band2_4GHz, new List<WifiNetwork>())!;
        rec.Score.Should().Be(100);
        rec.CompetingApCount.Should().Be(0);
        rec.Reason.Should().Contain("clear channel");
    }

    [Fact]
    public void Recommend_24GHz_AvoidsTheCongestedChannel()
    {
        // ch 1 に強い AP が密集 → プランナは 6 か 11 を選ぶべき。
        var visible = new List<WifiNetwork>
        {
            Ap(WifiBand.Band2_4GHz, 1, 90),
            Ap(WifiBand.Band2_4GHz, 1, 85),
            Ap(WifiBand.Band2_4GHz, 1, 80),
        };
        var rec = _svc.Recommend(WifiBand.Band2_4GHz, visible)!;
        rec.RecommendedChannel.Should().NotBe(1);
        rec.RecommendedChannel.Should().BeOneOf(6, 11);
    }

    [Fact]
    public void Recommend_24GHz_AdjacentChannelInterferenceCounts()
    {
        // ch 3 の AP は ch 1 とも ch 6 とも重なる (2.4GHz の重なり)。
        // ch 11 は ch 3 と十分離れている (|11-3|=8≥5) → ch 11 が最良。
        var visible = new List<WifiNetwork>
        {
            Ap(WifiBand.Band2_4GHz, 3, 80),
        };
        var rec = _svc.Recommend(WifiBand.Band2_4GHz, visible)!;
        rec.RecommendedChannel.Should().Be(11);
        // ch 11 候補は重なり無し → competing 0、score 100。
        var ch11 = rec.Ranked.First(r => r.Channel == 11);
        ch11.CompetingApCount.Should().Be(0);
        ch11.Score.Should().Be(100);
    }

    [Fact]
    public void Recommend_StrongerNeighborHurtsMoreThanWeak()
    {
        // ch 1 に弱い AP (20%)、ch 6 に強い AP (100%)。
        // ch 11 は両者と十分離れ最良。弱 ch1 と強 ch6 の比較では ch1 の方がクリーン。
        var visible = new List<WifiNetwork>
        {
            Ap(WifiBand.Band2_4GHz, 1, 20),
            Ap(WifiBand.Band2_4GHz, 6, 100),
        };
        var ranked = _svc.RankCandidates(WifiBand.Band2_4GHz, visible);
        var s1  = ranked.First(r => r.Channel == 1).Score;
        var s6  = ranked.First(r => r.Channel == 6).Score;
        var s11 = ranked.First(r => r.Channel == 11).Score;

        s11.Should().Be(100, "ch 11 is far from both ch1 and ch6");
        s1.Should().BeGreaterThan(s6, "a weak (20%) co-channel neighbor interferes less than a strong (100%) one");
    }

    [Fact]
    public void Recommend_5GHz_CoChannelDominates()
    {
        // ch 36 に強い AP → ch 36 は避ける。ch 40 (Δ=4) は軽い干渉、ch 149 は無干渉。
        var visible = new List<WifiNetwork>
        {
            Ap(WifiBand.Band5GHz, 36, 90),
        };
        var ranked = _svc.RankCandidates(WifiBand.Band5GHz, visible);
        var s36  = ranked.First(r => r.Channel == 36).Score;
        var s149 = ranked.First(r => r.Channel == 149).Score;
        s149.Should().Be(100);
        s36.Should().BeLessThan(s149);
        ranked[0].Channel.Should().NotBe(36, "the busiest channel must not be the top pick");
    }

    [Fact]
    public void Recommend_IgnoresNetworksOnOtherBands()
    {
        // 5GHz/6GHz の AP は 2.4GHz の推奨に影響しない。
        var visible = new List<WifiNetwork>
        {
            Ap(WifiBand.Band5GHz, 36, 100),
            Ap(WifiBand.Band6GHz, 37, 100),
        };
        var rec = _svc.Recommend(WifiBand.Band2_4GHz, visible)!;
        rec.Score.Should().Be(100, "no 2.4GHz neighbors → all 2.4GHz candidates are clear");
        rec.CompetingApCount.Should().Be(0);
    }

    // ── 全バンド一括 + 決定論性 ─────────────────────────────────

    [Fact]
    public void RecommendAllBands_ReturnsThreeBands()
    {
        var visible = new List<WifiNetwork> { Ap(WifiBand.Band2_4GHz, 1, 50) };
        var all = _svc.RecommendAllBands(visible);
        all.Select(r => r.Band).Should()
            .BeEquivalentTo(new[] { WifiBand.Band2_4GHz, WifiBand.Band5GHz, WifiBand.Band6GHz });
    }

    [Fact]
    public void Ranking_IsDeterministic_TiesBrokenByChannelNumber()
    {
        // 全 candidate がクリーン (score 100) → チャネル番号昇順で安定。
        var rec = _svc.Recommend(WifiBand.Band2_4GHz, new List<WifiNetwork>())!;
        rec.RecommendedChannel.Should().Be(1, "all tied at 100 → lowest channel wins deterministically");
        rec.Ranked.Select(r => r.Channel).Should().ContainInOrder(1, 6, 11);
    }

    [Fact]
    public void RankCandidates_NullVisible_Throws()
    {
        var act = () => _svc.RankCandidates(WifiBand.Band2_4GHz, null!);
        act.Should().Throw<System.ArgumentNullException>();
    }

    // ── 自己レビューで発見した短所の回帰テスト ─────────────────────

    [Fact]
    public void Recommend_SkipsNeighborsWithUnknownChannel()
    {
        // チャネル不明 (0) の AP は干渉に数えない。含めると |1-0|=1 で ch1 に偽の干渉が乗る。
        var visible = new List<WifiNetwork> { Ap(WifiBand.Band2_4GHz, 0, 100) };
        var rec = _svc.Recommend(WifiBand.Band2_4GHz, visible)!;
        rec.Score.Should().Be(100, "channel 0 (unknown) must not count as interference");
        rec.CompetingApCount.Should().Be(0);
    }

    [Fact]
    public void Recommend_5GHz_WideChannelNeighborReachesFurther()
    {
        // ch36 の AP。20MHz なら ch48 (Δ=12) には届かないが、80MHz だと 36–48 を占有し届く。
        var narrow = new List<WifiNetwork> { Ap(WifiBand.Band5GHz, 36, 90, widthMhz: 20) };
        var wide   = new List<WifiNetwork> { Ap(WifiBand.Band5GHz, 36, 90, widthMhz: 80) };

        var s48Narrow = _svc.RankCandidates(WifiBand.Band5GHz, narrow).First(r => r.Channel == 48).Score;
        var s48Wide   = _svc.RankCandidates(WifiBand.Band5GHz, wide).First(r => r.Channel == 48).Score;

        s48Narrow.Should().Be(100, "a 20MHz neighbor at ch36 does not overlap ch48");
        s48Wide.Should().BeLessThan(s48Narrow, "an 80MHz neighbor at ch36 occupies 36–48 and overlaps ch48");
    }
}
