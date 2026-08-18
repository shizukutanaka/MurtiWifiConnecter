using System;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  GUI (ConnectDialog) が組み立てる WifiProfileSpec の契約。
//
//  ConnectDialog は Window 派生でテストプロジェクトからインスタンス化できない
//  (AI-SESSION-HANDBOOK §3)。そこで「ダイアログが BuildSpec() で作る形の spec」を
//  ここで再現し、CLI と同じ検証を通ることを固定する。
//
//  ダイアログ側の実際の組み立ては src/MWC.App/Views/ConnectDialog.xaml.cs の
//  BuildSpec()。CLI 側の対応物は src/MWC.Cli/Program.cs の BuildConnect。
//  両者が同じ spec を作ることが、GUI と CLI で挙動が食い違わない根拠になる。
//
//  ここで検証したいのは「ダイアログの入力規則が Core の検証規則と一致しているか」:
//    - PEAP / EAP-TTLS はユーザー名+パスワード必須
//    - EAP-TLS は証明書認証なので資格情報不要
//    - Enterprise に PSK のパスフレーズ長規則を適用してはいけない
// ══════════════════════════════════════════════════════════════
public class GuiEnterpriseSpecContractTests
{
    // ConnectDialog.BuildSpec() の Enterprise 分岐と同じ形。
    // 空欄は null に落とす (ダイアログは IsNullOrWhiteSpace で判定している)。
    private static WifiProfileSpec DialogSpec(
        AuthMethod auth = AuthMethod.WPA2Enterprise,
        EapType? eap = EapType.PEAP_MSCHAPv2,
        string? username = "student@univ.ac.jp",
        string? password = "pw",
        string? identity = null,
        string serverNameField = "")
        => new()
        {
            Ssid     = "eduroam",
            Auth     = auth,
            EapType  = eap,
            Username = string.IsNullOrWhiteSpace(username) ? null : username,
            Password = string.IsNullOrEmpty(password) ? null : password,
            Domain   = string.IsNullOrWhiteSpace(identity) ? null : identity,
            ServerNames = string.IsNullOrWhiteSpace(serverNameField)
                ? Array.Empty<string>()
                : serverNameField.Split(';', StringSplitOptions.RemoveEmptyEntries
                                            | StringSplitOptions.TrimEntries),
        };

    [Fact]
    public void Peap_WithUsernameAndPassword_IsValid()
    {
        DialogSpec().Validate().IsValid.Should().BeTrue();
    }

    [Fact]
    public void Peap_WithoutUsername_IsRejected()
    {
        // ダイアログは接続前にこれを検出して Error_EapUsernameRequired を出す。
        DialogSpec(username: "   ").Validate().IsValid.Should().BeFalse();
    }

    [Fact]
    public void Peap_WithoutPassword_IsRejected()
    {
        DialogSpec(password: "").Validate().IsValid.Should().BeFalse();
    }

    [Fact]
    public void EapTls_WithoutCredentials_IsValid()
    {
        // EAP-TLS は証明書認証。ダイアログはこの方式を選ぶとユーザー名/パスワード欄を隠す。
        // ここで検証が通らないと、隠した結果 接続不能になってしまう。
        DialogSpec(eap: EapType.EAP_TLS, username: "", password: "")
            .Validate().IsValid.Should().BeTrue();
    }

    [Fact]
    public void EapTtls_RequiresCredentials_LikePeap()
    {
        DialogSpec(eap: EapType.EAP_TTLS).Validate().IsValid.Should().BeTrue();
        DialogSpec(eap: EapType.EAP_TTLS, password: "").Validate().IsValid.Should().BeFalse();
    }

    [Fact]
    public void ShortPassword_IsAcceptedForEnterprise_UnlikePsk()
    {
        // PSK は 8 文字以上を要求するが、EAP パスワードにその規則は無い。
        // ダイアログが Enterprise で IsPassphraseValid を使い回すと、
        // 正当な短い EAP パスワードを弾いてしまう — その退行を防ぐ。
        DialogSpec(password: "ab").Validate().IsValid.Should().BeTrue();
    }

    [Fact]
    public void ServerNameField_SplitsOnSemicolon_AndTrims()
    {
        var spec = DialogSpec(serverNameField: " radius1.univ.ac.jp ; radius2.univ.ac.jp ");
        spec.ServerNames.Should().Equal("radius1.univ.ac.jp", "radius2.univ.ac.jp");
    }

    [Fact]
    public void EmptyServerNameField_YieldsNoPinning_NotAnEmptyString()
    {
        // 空文字が 1 件入ると ProfileXmlBuilder は「サーバ名を指定した」と解釈し、
        // 照合不能な検証を有効化してしまう。空欄は必ず空配列にする。
        DialogSpec(serverNameField: "   ").ServerNames.Should().BeEmpty();
    }

    [Fact]
    public void IdentityField_BecomesTheAnonymousOuterIdentity()
    {
        // ダイアログの「匿名 ID」欄は spec.Domain に入り、PEAP では PeapExtensions の
        // IdentityPrivacy、EAP-TTLS では Phase1Identity として平文の外部 ID になる。
        var spec = DialogSpec(identity: "anonymous@univ.ac.jp",
                              serverNameField: "radius.univ.ac.jp");
        spec.Domain.Should().Be("anonymous@univ.ac.jp");

        var xml = ProfileXmlBuilder.Build(spec);
        xml.Should().Contain("anonymous@univ.ac.jp");
        // 実ユーザー名が外部 ID として出てはならない
        xml.Should().NotContain("<AnonymousUserName>student@univ.ac.jp");
    }

    [Fact]
    public void DialogSpec_BuildsValidProfileXml_ForEveryOfferedEapType()
    {
        // ダイアログが提示する 3 方式すべてで、実際にプロファイル XML が生成できること。
        foreach (var eap in new[] { EapType.PEAP_MSCHAPv2, EapType.EAP_TLS, EapType.EAP_TTLS })
        {
            var spec = eap == EapType.EAP_TLS
                ? DialogSpec(eap: eap, username: "", password: "",
                             serverNameField: "radius.univ.ac.jp")
                : DialogSpec(eap: eap, serverNameField: "radius.univ.ac.jp");

            spec.Validate().IsValid.Should().BeTrue(because: $"{eap} is offered in the dialog");
            var act = () => ProfileXmlBuilder.Build(spec);
            act.Should().NotThrow(because: $"the dialog must not offer an unbuildable {eap}");
        }
    }

    [Fact]
    public void NonEnterpriseAuth_StillUsesPassphrase_NotEapFields()
    {
        // 非 Enterprise 分岐 (BuildSpec の前半) の形。
        var spec = new WifiProfileSpec
        {
            Ssid = "HomeNet", Auth = AuthMethod.WPA2PSK, Passphrase = "correct horse",
        };
        spec.Validate().IsValid.Should().BeTrue();
        spec.EapType.Should().BeNull();
        spec.Username.Should().BeNull();
    }
}
