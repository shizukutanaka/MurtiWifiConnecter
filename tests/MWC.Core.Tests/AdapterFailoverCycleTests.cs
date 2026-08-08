using System;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  フェイルオーバー設定の循環検出。
//
//  フェイルオーバーは「*他の*子機へ退避する」ことが定義であり、A→A(自己参照)や
//  A→B→A(相互参照)は定義上の矛盾。AdapterFailoverService は全アダプターを独立に
//  走査する (foreach (var adapter in adapters)) ため、相互参照が設定されると双方の
//  切断時に互いへ接続を試み、無意味なスキャン・接続・通知が双方向に発生する。
//  信頼性工学では循環依存は「要求がループしてリソースを消費し最終的にタイムアウトする」
//  既知の障害パターンであり、対策は書き込み時点での検出。
//
//  UI (AdapterPreferencesDialog.xaml.cs) は候補一覧から自分自身を除外しているが、
//  AdapterPreferencesService は Core にあり sdk/MWC.SDK.csproj 経由で外部にも
//  出荷されるため、ドメイン不変条件は UI ではなく Core で守られねばならない。
//  これらのテストはその不変条件を固定する。
//
//  各テストは Guid.NewGuid() で分離する (既存テストの慣例。実ファイルを共有するため)。
// ══════════════════════════════════════════════════════════════
public class AdapterFailoverCycleTests
{
    [Fact]
    public void SelfReference_IsRejected_AndFailoverDisabled()
    {
        var svc = new AdapterPreferencesService();
        var a = Guid.NewGuid();

        svc.SetFailover(a, a, enabled: true);

        var pref = svc.Get(a);
        pref.FailoverAdapterId.Should().BeNull(
            because: "an adapter cannot fail over to itself — that is not a backup");
        pref.EnableFailover.Should().BeFalse();
    }

    [Fact]
    public void MutualReference_SecondEdgeIsRejected()
    {
        var svc = new AdapterPreferencesService();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // A → B is fine.
        svc.SetFailover(a, b, enabled: true);
        svc.Get(a).FailoverAdapterId.Should().Be(b);

        // B → A would close the loop: both adapters would try to rescue each other.
        svc.SetFailover(b, a, enabled: true);

        var prefB = svc.Get(b);
        prefB.FailoverAdapterId.Should().BeNull();
        prefB.EnableFailover.Should().BeFalse();

        // The pre-existing, valid A → B edge must survive untouched.
        svc.Get(a).FailoverAdapterId.Should().Be(b);
        svc.Get(a).EnableFailover.Should().BeTrue();
    }

    [Fact]
    public void LongerCycle_IsRejected()
    {
        var svc = new AdapterPreferencesService();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        svc.SetFailover(a, b, enabled: true);
        svc.SetFailover(b, c, enabled: true);

        // C → A would close a 3-hop cycle (A→B→C→A).
        svc.SetFailover(c, a, enabled: true);

        svc.Get(c).FailoverAdapterId.Should().BeNull();
        // The rest of the chain is untouched.
        svc.Get(a).FailoverAdapterId.Should().Be(b);
        svc.Get(b).FailoverAdapterId.Should().Be(c);
    }

    [Fact]
    public void AcyclicChain_IsAllowed()
    {
        var svc = new AdapterPreferencesService();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        // A → B → C is a valid chain: no adapter is its own backup, directly or transitively.
        svc.SetFailover(a, b, enabled: true);
        svc.SetFailover(b, c, enabled: true);

        svc.Get(a).FailoverAdapterId.Should().Be(b);
        svc.Get(b).FailoverAdapterId.Should().Be(c);
        svc.Get(a).EnableFailover.Should().BeTrue();
        svc.Get(b).EnableFailover.Should().BeTrue();
    }

    [Fact]
    public void TwoAdaptersSharingOneBackup_IsAllowed()
    {
        var svc = new AdapterPreferencesService();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var backup = Guid.NewGuid();

        // A → Backup and B → Backup: a fan-in is not a cycle.
        svc.SetFailover(a, backup, enabled: true);
        svc.SetFailover(b, backup, enabled: true);

        svc.Get(a).FailoverAdapterId.Should().Be(backup);
        svc.Get(b).FailoverAdapterId.Should().Be(backup);
    }

    [Fact]
    public void ClearingFailover_WithNull_IsAlwaysAllowed()
    {
        var svc = new AdapterPreferencesService();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        svc.SetFailover(a, b, enabled: true);
        svc.SetFailover(a, null, enabled: false);

        svc.Get(a).FailoverAdapterId.Should().BeNull();
        svc.Get(a).EnableFailover.Should().BeFalse();
    }

    [Fact]
    public void AfterRejection_AdapterCanStillBeGivenAValidBackup()
    {
        var svc = new AdapterPreferencesService();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        svc.SetFailover(a, a, enabled: true);   // rejected
        svc.SetFailover(a, b, enabled: true);   // valid — must work

        svc.Get(a).FailoverAdapterId.Should().Be(b);
        svc.Get(a).EnableFailover.Should().BeTrue();
    }
}
