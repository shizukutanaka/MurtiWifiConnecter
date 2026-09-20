using System.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  eduroam CAT インポートの配線契約 (`mwc import-cat`)。
//
//  CAT XML が持つのは「組織側で決まる情報」だけ — SSID・EAP 種別・RADIUS サーバ名・
//  信頼ルート CA・匿名 (外部) アイデンティティ。**利用者の資格情報は含まれない**。
//  各利用者が自分の学内アカウントを後から入力するのが eduroam の設計だからである。
//  したがって import-cat は「CAT の spec + 利用者の --username/-p」を合成する。
//
//  ここで固定するのは、その合成が正しく行われることと、
//  2026-07 に発見した取り違え (匿名 ID が Username に入っていた) が再発しないこと。
// ══════════════════════════════════════════════════════════════
public class CatImportWiringTests
{
    private const string CatXml = """
        <EAPIdentityProviderList>
          <EAPIdentityProvider>
            <SSID>eduroam</SSID>
            <AuthenticationMethods>
              <AuthenticationMethod><EAPMethod><Type>25</Type></EAPMethod></AuthenticationMethod>
            </AuthenticationMethods>
            <CredentialApplicability>
              <IEEE80211><ServerName>radius.univ.ac.jp</ServerName></IEEE80211>
            </CredentialApplicability>
            <ProviderInfo><DisplayName>Test University</DisplayName></ProviderInfo>
          </EAPIdentityProvider>
        </EAPIdentityProviderList>
        """;

    private static CatProfile Parsed()
        => new CatImportService().ParseEapConfig(CatXml).First(p => p.IsValid);

    [Fact]
    public void CatSpecAlone_IsIncomplete_BecauseCatNeverCarriesCredentials()
    {
        // これが import-cat が --username/-p を要求する理由。
        // PEAP はユーザー名+パスワード必須なので、CAT の情報だけでは検証を通らない。
        var spec = new CatImportService().BuildEduroamSpec(Parsed());

        spec.Username.Should().BeNull();
        spec.Password.Should().BeNull();
        spec.Validate().IsValid.Should().BeFalse(
            because: "a CAT file describes the institution, not the user");
    }

    [Fact]
    public void AddingUserCredentials_MakesItConnectable()
    {
        // import-cat が行う合成そのもの。
        var spec = new CatImportService().BuildEduroamSpec(Parsed()) with
        {
            Username = "student@univ.ac.jp",
            Password = "s3cret",
        };

        spec.Validate().IsValid.Should().BeTrue();
        spec.Ssid.Should().Be("eduroam");
        spec.Auth.Should().Be(AuthMethod.WPA2Enterprise);
        spec.EapType.Should().Be(EapType.PEAP_MSCHAPv2);
        spec.ServerNames.Should().Contain("radius.univ.ac.jp");
    }

    [Fact]
    public void AnonymousIdentity_GoesToDomain_NotUsername()
    {
        // 2026-07 に修正した取り違えの回帰テスト。
        // spec.Username はトンネル内で使う実 ID、spec.Domain は平文で送る外部 ID。
        // 匿名 ID を Username に入れると、実ユーザー名の置き場が無くなるうえ
        // 外部 ID が設定されない — 秘匿の意味が失われる。
        var profile = Parsed() with
        {
            AnonymousIdentity = "anonymous@univ.ac.jp",
            Domain            = "univ.ac.jp",
        };
        var spec = new CatImportService().BuildEduroamSpec(profile);

        spec.Domain.Should().Be("anonymous@univ.ac.jp");
        spec.Username.Should().BeNull(
            because: "the user's real identity comes from --username, never from the CAT file");
    }

    [Fact]
    public void WithoutExplicitAnonymousIdentity_OneIsDerivedFromTheRealm()
    {
        // CAT が匿名 ID を明示しない場合でも realm があれば anonymous@realm を組み立てる。
        // realm で経路制御する RADIUS 配備を壊さないため、素の "anonymous" にはしない。
        var profile = Parsed() with { AnonymousIdentity = null, Domain = "univ.ac.jp" };
        var spec = new CatImportService().BuildEduroamSpec(profile);

        spec.Domain.Should().Be("anonymous@univ.ac.jp");
    }

    [Fact]
    public void WithNeitherIdentityNorRealm_DomainStaysNull()
    {
        var profile = Parsed() with { AnonymousIdentity = null, Domain = null };
        new CatImportService().BuildEduroamSpec(profile).Domain.Should().BeNull();
    }

    [Fact]
    public void ImportedSpec_ProducesAProfileWithServerValidationEnforced()
    {
        // CAT はサーバ名を必ず持つ (IsValid の条件)。その結果、生成される
        // プロファイルでは証明書の信頼プロンプトが抑止される — つまり利用者が
        // 偽 RADIUS を1クリックで承認する余地が無い。
        var spec = new CatImportService().BuildEduroamSpec(Parsed()) with
        {
            Username = "student@univ.ac.jp", Password = "s3cret",
        };

        var xml = ProfileXmlBuilder.Build(spec);
        xml.Should().Contain("radius.univ.ac.jp");
        xml.Should().Contain("<DisableUserPromptForServerValidation>true");
    }

    [Fact]
    public void RealUsername_NeverBecomesTheCleartextOuterIdentity()
    {
        var spec = new CatImportService().BuildEduroamSpec(
            Parsed() with { AnonymousIdentity = "anonymous@univ.ac.jp" }) with
        {
            Username = "student@univ.ac.jp", Password = "s3cret",
        };

        var xml = ProfileXmlBuilder.Build(spec);
        xml.Should().Contain("<AnonymousUserName>anonymous@univ.ac.jp");
        xml.Should().NotContain("<AnonymousUserName>student@univ.ac.jp");
    }
}
