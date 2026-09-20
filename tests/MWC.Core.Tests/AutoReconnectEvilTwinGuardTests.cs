using System.Collections.Generic;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  自動再接続に組み込んだ Evil Twin 防御の "無人動作ゆえの要件" を固定する。
//
//  背景: 自動再接続は evil twin 攻撃の主要な侵入口である。攻撃者が既知の SSID を
//  持つ偽 AP を立てるだけで、ユーザーが何も操作しなくても端末が接続してしまう。
//  手動接続なら NetworkDetailViewModel が画面に警告を出せるが、自動再接続は
//  無人であり誰も警告を見ない。そこで AutoReconnectService は接続前に
//  EvilTwinDetector.Analyze を呼び、HighRisk なら接続を中止する。
//
//  検出ロジックそのもの (混在セキュリティ・OUI 不一致・ダウングレードの検出) は
//  EvilTwinAndKalmanTests で既に網羅済み。ここで固定するのは重複しない別の関心事:
//
//    (a) 誤検知しないこと — 無人動作で正当な再接続を妨げる害は大きい。
//        AutoReconnectService は Suspicious では止めず HighRisk でのみ止める、
//        という閾値の判断が依拠する前提を検証する。
//    (b) RecordTrusted の配線に意味があること — 接続成功時に学習させなければ
//        BSSID/ベンダー/ダウングレードの検査は永久に無効のままになる。
// ══════════════════════════════════════════════════════════════
public class AutoReconnectEvilTwinGuardTests
{
    private static WifiNetwork Net(
        string ssid, AuthMethod auth, string bssid = "AA:BB:CC:11:22:33")
        => new()
        {
            Ssid = ssid,
            Auth = auth,
            BssEntries = new List<BssInfo> { new() { Bssid = bssid } },
        };

    // ── (a) 誤検知しないこと ────────────────────────────────────────

    [Fact]
    public void FirstEverConnection_NoLearningData_IsNotBlocked()
    {
        // 初めて見るネットワークをいきなり疑ってはならない。学習前は
        // 検査 2〜4 が発火しないため、単独で見える AP は None になる。
        var detector = new EvilTwinDetector();
        var fresh = Net("NewCafe", AuthMethod.WPA2PSK);

        detector.Analyze(fresh, new List<WifiNetwork> { fresh })
            .Risk.Should().Be(EvilTwinRisk.None,
                because: "auto-reconnect must not refuse a network merely for being new");
    }

    [Fact]
    public void SecurityUpgrade_IsNotTreatedAsDowngrade()
    {
        // WPA2 → WPA3 は改善であり攻撃ではない。ルーターのファームウェア更新後に
        // 自動再接続が止まってしまってはならない。
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("HomeNet", "AA:BB:CC:11:22:33", AuthMethod.WPA2PSK);

        var upgraded = Net("HomeNet", AuthMethod.WPA3SAE, "AA:BB:CC:11:22:33");

        detector.Analyze(upgraded, new List<WifiNetwork> { upgraded })
            .Reasons.Should().NotContain(r => r.Contains("downgrade"));
    }

    [Fact]
    public void UnchangedKnownNetwork_StaysNone_AcrossRepeatedReconnects()
    {
        // 同じ AP に何度再接続しても警告が蓄積してはならない
        // (AutoReconnectService は成功のたびに RecordTrusted を呼ぶため、
        //  自身の学習が自身を疑う原因になっていないことを確認する)。
        var detector = new EvilTwinDetector();
        var net = Net("HomeNet", AuthMethod.WPA2PSK, "AA:BB:CC:11:22:33");

        for (int i = 0; i < 5; i++)
        {
            detector.RecordTrusted(net.Ssid, "AA:BB:CC:11:22:33", net.Auth);
            detector.Analyze(net, new List<WifiNetwork> { net })
                .Risk.Should().Be(EvilTwinRisk.None);
        }
    }

    // ── (b) RecordTrusted の配線に意味があること ──────────────────────

    [Fact]
    public void WithoutLearning_DowngradeIsInvisible_WithLearning_ItIsDetected()
    {
        // AutoReconnectService が接続成功時に RecordTrusted を呼ぶ理由そのもの。
        // 学習しなければ、後からのダウングレードを検出する材料が存在しない。
        var detector = new EvilTwinDetector();
        var openAp = Net("Office", AuthMethod.Open, "FF:EE:DD:44:55:66");
        var scan = new List<WifiNetwork> { openAp };

        // 学習前 — ダウングレードとしては検出できない
        detector.Analyze(openAp, scan)
            .Reasons.Should().NotContain(r => r.Contains("downgrade"));

        // 学習後 — 同じ SSID が Open で現れたことをダウングレードとして検出
        detector.RecordTrusted("Office", "AA:BB:CC:11:22:33", AuthMethod.WPA2Enterprise);
        detector.Analyze(openAp, scan)
            .Reasons.Should().Contain(r => r.Contains("downgrade"),
                because: "recording each successful connection is what arms the detector");
    }

    // ── ベースラインの永続化 ──────────────────────────────────────────
    // 検査 2〜4 は学習を前提とするため、学習がプロセスメモリ限りだと再起動のたびに
    // 消え、直後は検査 1 しか発火しない = 理由が 1 件までしか積まれず HighRisk
    // (2 件以上) に到達できない。つまり再起動直後は防御が事実上無効になる。
    // 不正 AP 検出は信頼済み SSID/BSSID のベースラインを事前確立しておくことが
    // 前提の技術であり、その永続化はセキュリティ上の必須要件。

    [Fact]
    public void FreshDetector_CannotReachHighRisk_OnLoneRogueAp()
    {
        // これが永続化を必要とする理由そのもの。攻撃者が単独で偽 AP を出し
        // (本物が見えない) 学習も無い状態では、検出材料が存在しない。
        var fresh = new EvilTwinDetector();
        var rogue = Net("HomeNet", AuthMethod.Open, "FF:EE:DD:44:55:66");

        fresh.Analyze(rogue, new List<WifiNetwork> { rogue })
            .Risk.Should().NotBe(EvilTwinRisk.HighRisk,
                because: "without a learned baseline there is nothing to compare against — "
                       + "this is why the baseline must survive restarts");
    }

    [Fact]
    public void ExportImport_RestoresDetection_AcrossSimulatedRestart()
    {
        // セッション 1: 正当な AP に接続して学習
        var session1 = new EvilTwinDetector();
        session1.RecordTrusted("HomeNet", "AA:BB:CC:11:22:33", AuthMethod.WPA2PSK);
        var baseline = session1.ExportBaseline();

        // セッション 2 (再起動後): ベースラインを復元
        var session2 = new EvilTwinDetector();
        session2.ImportBaseline(baseline);

        // 復元後は、単独で現れた偽 AP のダウングレードを検出できる
        var rogue = Net("HomeNet", AuthMethod.Open, "FF:EE:DD:44:55:66");
        session2.Analyze(rogue, new List<WifiNetwork> { rogue })
            .Reasons.Should().Contain(r => r.Contains("downgrade"),
                because: "the whole point of persisting the baseline is that detection survives a restart");
    }

    [Fact]
    public void ImportedBaseline_DoesNotFlagTheLegitimateApItself()
    {
        // 復元したベースラインが、正当な AP を疑う原因になってはならない。
        var s1 = new EvilTwinDetector();
        s1.RecordTrusted("HomeNet", "AA:BB:CC:11:22:33", AuthMethod.WPA2PSK);

        var s2 = new EvilTwinDetector();
        s2.ImportBaseline(s1.ExportBaseline());

        var legit = Net("HomeNet", AuthMethod.WPA2PSK, "AA:BB:CC:11:22:33");
        s2.Analyze(legit, new List<WifiNetwork> { legit })
            .Risk.Should().Be(EvilTwinRisk.None);
    }

    [Fact]
    public void ImportBaseline_MergesWithoutDiscardingLaterLearning()
    {
        // 復元後に RecordTrusted しても、復元分が消えてはならない (加算的マージ)。
        var detector = new EvilTwinDetector();
        detector.ImportBaseline(new[]
        {
            new TrustedApBaseline("NetA", AuthMethod.WPA2PSK,
                new List<string> { "AcmeCorp" }),
        });
        detector.RecordTrusted("NetB", "11:22:33:44:55:66", AuthMethod.WPA3SAE);

        // 復元分 (NetA) はダウングレード検出に使える状態で残っている
        var rogueA = Net("NetA", AuthMethod.Open, "FF:EE:DD:44:55:66");
        detector.Analyze(rogueA, new List<WifiNetwork> { rogueA })
            .Reasons.Should().Contain(r => r.Contains("downgrade"));

        // 後から学習した分 (NetB) も生きている
        detector.GetTrustedBssids("NetB").Should().NotBeEmpty();
    }

    [Fact]
    public void ImportBaseline_SkipsMalformedEntries_RatherThanFailing()
    {
        // 破損データで防御全体を失うより、読める分だけでも復旧させる方が安全側。
        var detector = new EvilTwinDetector();
        var act = () => detector.ImportBaseline(new[]
        {
            new TrustedApBaseline("", AuthMethod.WPA2PSK, new List<string>()),
            new TrustedApBaseline("Good", AuthMethod.WPA2Enterprise, new List<string>()),
        });

        act.Should().NotThrow();

        // 読めた分 (Good) は復元されている — Auth が入っているので降格を検出できる
        var rogue = Net("Good", AuthMethod.Open, "FF:EE:DD:44:55:66");
        detector.Analyze(rogue, new List<WifiNetwork> { rogue })
            .Reasons.Should().Contain(r => r.Contains("downgrade"));
    }

    [Fact]
    public void ExportBaseline_RoundTripsThroughJson()
    {
        // 実際の永続化は JSON 経由 (AutoReconnectService)。
        // レコードが System.Text.Json でラウンドトリップできることを保証する。
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("HomeNet", "AA:BB:CC:11:22:33", AuthMethod.WPA2Enterprise);

        var json = System.Text.Json.JsonSerializer.Serialize(detector.ExportBaseline());
        var restored = System.Text.Json.JsonSerializer
            .Deserialize<List<TrustedApBaseline>>(json);

        restored.Should().NotBeNull();
        var revived = new EvilTwinDetector();
        revived.ImportBaseline(restored!);

        // Auth が渡っていることをダウングレード検出で確認する
        var rogue = Net("HomeNet", AuthMethod.Open, "FF:EE:DD:44:55:66");
        revived.Analyze(rogue, new List<WifiNetwork> { rogue })
            .Reasons.Should().Contain(r => r.Contains("downgrade"));
    }

    // ── BSSID は永続化しない(位置プライバシー)────────────────────────
    // BSSID は AP の MAC アドレスで、Wi-Fi 測位システムに問い合わせると位置に
    // 変換できる。永続化するとファイルが「訪問した場所の履歴」になるため、
    // MAC 追跡リスクを警告する立場の本製品としては保存しない。
    // 将来のセッションが「再起動後に検査 2 が効かない」を欠陥と誤認して
    // BSSID 保存を復活させないよう、この契約をテストで固定する。

    [Fact]
    public void ExportedBaseline_ContainsNoBssid_SoTheFileIsNotALocationHistory()
    {
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("HomeNet", "AA:BB:CC:11:22:33", AuthMethod.WPA2PSK);

        var json = System.Text.Json.JsonSerializer.Serialize(detector.ExportBaseline());

        json.Should().NotContain("AA:BB:CC:11:22:33",
            because: "a persisted BSSID is geolocatable and would turn this file into "
                   + "a record of everywhere the user has been");
        json.Should().NotContain("AA:BB:CC");
    }

    [Fact]
    public void SessionLearning_StillTracksBssids_InMemory()
    {
        // 永続化しないだけで、セッション中の BSSID 学習は従来どおり働く
        // (public API GetTrustedBssids の挙動は不変)。
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("HomeNet", "AA:BB:CC:11:22:33", AuthMethod.WPA2PSK);

        detector.GetTrustedBssids("HomeNet").Should().Contain("AA:BB:CC:11:22:33");
    }

    [Fact]
    public void AfterRestart_DowngradePlusVendorMismatch_StillReachesHighRisk()
    {
        // BSSID を落としても、前コミットが守ろうとした性質は保たれることの確認。
        // 検査 3 (Auth 由来) と検査 4 (ベンダー名由来) はどちらも永続化されるため、
        // 両方揃えば理由 2 件 = HighRisk となり自動再接続は中止される。
        //
        // 検査 4 が発火するには OUI DB がベンダー名を解決できる必要があるため、
        // 攻撃者側 BSSID には DB に実在するプレフィックス 00:11:22 (Apple) を使う。
        var restored = new EvilTwinDetector();
        restored.ImportBaseline(new[]
        {
            new TrustedApBaseline("HomeNet", AuthMethod.WPA2PSK,
                new List<string> { "TrustedVendorInc" }),   // 既知ベンダーは別物
        });

        // 攻撃者: 同じ SSID を Open で提示(降格)+ 既知と異なるベンダーの機器
        var rogue = Net("HomeNet", AuthMethod.Open, "00:11:22:44:55:66");
        var verdict = restored.Analyze(rogue, new List<WifiNetwork> { rogue });

        verdict.Reasons.Should().Contain(r => r.Contains("downgrade"));
        verdict.Reasons.Should().Contain(r => r.Contains("vendor"));
        verdict.Risk.Should().Be(EvilTwinRisk.HighRisk,
            because: "these two checks survive a restart, so the guard still aborts auto-reconnect");
    }

    [Fact]
    public void AfterRestart_UnknownVendorAttacker_IsOnlySuspicious_DocumentedLimitation()
    {
        // 受け入れた限界を明示的に固定する。攻撃者の OUI が OUI DB に無い場合、
        // 検査 4 は発火せず、検査 2 は BSSID 永続化を要するため使えない。
        // 結果として理由は降格の 1 件 = Suspicious となり、HighRisk には達しない
        // → 自動再接続は中止されない。
        // 位置履歴を平文で残す害の方が大きいと判断してこの限界を選んだ。
        // (docs/FEATURE-AUDIT.md §3 に記録)
        var restored = new EvilTwinDetector();
        restored.ImportBaseline(new[]
        {
            new TrustedApBaseline("HomeNet", AuthMethod.WPA2PSK, new List<string> { "SomeVendor" }),
        });

        // FF:EE:DD は OUI DB に無い → ベンダー解決不能 → 検査 4 は発火しない
        var rogue = Net("HomeNet", AuthMethod.Open, "FF:EE:DD:44:55:66");
        var verdict = restored.Analyze(rogue, new List<WifiNetwork> { rogue });

        verdict.Risk.Should().Be(EvilTwinRisk.Suspicious);
        verdict.IsSuspect.Should().BeTrue(
            because: "the downgrade is still detected and logged, it just does not cross the abort threshold");
    }

    [Fact]
    public void AttackerDowngradeWithForeignBssid_ReachesHighRisk_SoAutoReconnectRefuses()
    {
        // AutoReconnectService が実際に接続を中止する閾値は HighRisk。
        // 現実的な攻撃シナリオ (既知 SSID を Open で、別ベンダー機器から出す) が
        // その閾値に到達することを確認する — 到達しなければ防御は働かない。
        var detector = new EvilTwinDetector();
        detector.RecordTrusted("HomeNet", "AA:BB:CC:11:22:33", AuthMethod.WPA2PSK);

        var attacker = Net("HomeNet", AuthMethod.Open, "FF:EE:DD:44:55:66");
        var scan = new List<WifiNetwork>
        {
            attacker,
            Net("HomeNet", AuthMethod.WPA2PSK, "AA:BB:CC:11:22:33"),  // 本物も同時に見えている
        };

        detector.Analyze(attacker, scan).Risk.Should().Be(EvilTwinRisk.HighRisk,
            because: "nobody is watching during auto-reconnect — this must be refused, not merely logged");
    }
}
