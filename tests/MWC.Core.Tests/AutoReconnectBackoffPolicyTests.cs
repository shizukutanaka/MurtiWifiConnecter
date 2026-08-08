using System;
using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  AutoReconnectService が使うバックオフ方針の検証。
//
//  RetryPolicy 自体の性質 (遅延境界・ジッター分散・IsRetriable の分類) は
//  RecommendationAndPortalTests で検証済み。ここで固定するのは
//  「無人の自動再接続に与えた設定が妥当な範囲か」という別の関心事。
//
//  背景: 自動再接続は無人で動くため、失敗しても誰も止めない。バックオフが無いと
//  「切断 → 即再試行 → 失敗 → また切断イベント」が事実上のタイトループになる。
//  固定待機は再試行を同期させるだけで有効でなく、指数バックオフ + ジッターと、
//  決定的失敗の非リトライ化 + 最大試行回数が必要とされる。
//  AutoReconnectService の設定 (base=2s, max=2min, attempts=5) をここに写して、
//  将来「もっと短く」変更されたときに、それが意図的な判断であることを強制する。
// ══════════════════════════════════════════════════════════════
public class AutoReconnectBackoffPolicyTests
{
    // AutoReconnectService._retry と同じ設定。
    private static RetryPolicy AutoReconnectPolicy() => new(
        baseDelay: TimeSpan.FromSeconds(2),
        maxDelay: TimeSpan.FromMinutes(2),
        maxAttempts: 5);

    [Fact]
    public void Backoff_GrowsAcrossAttempts_NotFixed()
    {
        var policy = AutoReconnectPolicy();

        // 決定論版で比較(ジッター無し)。固定間隔ではなく増加していること。
        var d0 = policy.ComputeDeterministicDelay(0);
        var d1 = policy.ComputeDeterministicDelay(1);
        var d2 = policy.ComputeDeterministicDelay(2);

        d1.Should().BeGreaterThan(d0, because: "fixed intervals only synchronize retries");
        d2.Should().BeGreaterThan(d1);
        d0.Should().Be(TimeSpan.FromSeconds(2));
        d1.Should().Be(TimeSpan.FromSeconds(4));
        d2.Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void Backoff_IsCapped_SoRecoveryStaysResponsive()
    {
        var policy = AutoReconnectPolicy();

        // 上限に達しても 2 分を超えない — 電波が戻ったとき何時間も待たされないこと。
        policy.ComputeDeterministicDelay(20).Should().Be(TimeSpan.FromMinutes(2));

        // ジッター有りでも上限内。
        for (int i = 0; i < 50; i++)
            policy.ComputeDelay(20).Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void Backoff_FirstRetryIsNotInstant_ButStaysUnderTenSeconds()
    {
        var policy = AutoReconnectPolicy();

        // 初回リトライ待ちの上限。短すぎると無意味、長すぎると復帰が遅い。
        policy.ComputeDeterministicDelay(0).Should()
            .BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1))
            .And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void MaxAttempts_IsBounded_SoFailingSsidIsEventuallyAbandoned()
    {
        // 一時的失敗であっても無制限に試し続けない。
        AutoReconnectPolicy().MaxAttempts.Should().BeInRange(3, 10);
    }

    [Theory]
    [InlineData(ConnectionFailure.BadCredentials)]
    [InlineData(ConnectionFailure.InvalidProfile)]
    [InlineData(ConnectionFailure.ProfileRejected)]
    [InlineData(ConnectionFailure.InsufficientPrivilege)]
    public void DeterministicFailures_AreNotRetried_ByUnattendedReconnect(ConnectionFailure failure)
    {
        // これらは何度試しても同じ結果になる。AutoReconnectService はこの分類を使って
        // 即座に打ち切る(パスワード変更後に永久リトライし続けないため)。
        RetryPolicy.IsRetriable(failure).Should().BeFalse();
    }

    [Theory]
    [InlineData(ConnectionFailure.Timeout)]
    [InlineData(ConnectionFailure.NotInRange)]
    public void TransientFailures_AreRetried_WithBackoff(ConnectionFailure failure)
    {
        // 電波状況など一時的な要因は、バックオフを挟んで再試行する価値がある。
        RetryPolicy.IsRetriable(failure).Should().BeTrue();
    }

    [Fact]
    public void TotalWaitAcrossAllAttempts_IsBoundedAndReasonable()
    {
        var policy = AutoReconnectPolicy();

        // 打ち切りまでに費やす合計待機時間(決定論版)。
        // 短すぎれば実質リトライ無し、長すぎれば「壊れている」と誤認される。
        var total = Enumerable.Range(0, policy.MaxAttempts)
            .Select(i => policy.ComputeDeterministicDelay(i))
            .Aggregate(TimeSpan.Zero, (acc, d) => acc + d);

        total.Should().BeGreaterThan(TimeSpan.FromSeconds(30));
        total.Should().BeLessThan(TimeSpan.FromMinutes(5));
    }
}
