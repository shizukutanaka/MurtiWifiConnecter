using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  802.1X サーバ証明書の検証プロンプト設定。
//
//  これは 802.1X で最も悪用される設定である。Microsoft のスキーマ定義では:
//    true  → ユーザー入力なしで検証し、失敗すれば認証を失敗させる (厳格)
//    false → 「この証明書を信頼しますか」を尋ね、承認されれば接続する (危険)
//
//  攻撃者が偽 AP + 偽 RADIUS (hostapd-wpe 等) を立てて自己署名証明書を提示すると、
//  ユーザーが 1 度「はい」を押すだけで PEAP トンネルが成立し、MSCHAPv2 の
//  チャレンジ/レスポンスが攻撃者に渡ってオフライン解析される。
//  PEAP-MSCHAPv2 の資格情報窃取として広く知られた攻撃経路。
//
//  本製品の方針(ProfileXmlBuilder.SuppressServerValidationPrompt):
//    ServerNames か TrustedRootCaThumbprints が指定されている
//      → ユーザーは「この特定のサーバだけを信頼する」と明示した。
//        プロンプトを許すと 1 クリックでそのピン留めが無効化されるため厳格化する。
//    どちらも未指定
//      → 照合対象が存在しないため従来どおりプロンプトを許す
//        (初回設定や CAT 未導入の環境を壊さない)。
//
//  この対応関係が将来「単純化」で壊れないよう、全 EAP 方式について固定する。
// ══════════════════════════════════════════════════════════════
public class ServerValidationPromptTests
{
    private static readonly XNamespace MsPeap =
        "http://www.microsoft.com/provisioning/MsPeapConnectionPropertiesV1";
    private static readonly XNamespace EapTls =
        "http://www.microsoft.com/provisioning/EapTlsConnectionPropertiesV1";
    private static readonly XNamespace EapTtls =
        "http://www.microsoft.com/provisioning/EapTtlsConnectionPropertiesV1";

    private const string Thumb = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    private static XDocument Build(WifiProfileSpec spec)
        => XDocument.Parse(ProfileXmlBuilder.Build(spec));

    private static WifiProfileSpec Peap(string[]? servers = null, string[]? cas = null)
        => new()
        {
            Ssid = "eduroam",
            Auth = AuthMethod.WPA2Enterprise,
            EapType = EapType.PEAP_MSCHAPv2,
            Username = "student@univ.ac.jp",
            Password = "pw",
            ServerNames = servers ?? System.Array.Empty<string>(),
            TrustedRootCaThumbprints = cas ?? System.Array.Empty<string>(),
        };

    // ── PEAP ─────────────────────────────────────────────────────────

    [Fact]
    public void Peap_WithPinnedServerName_SuppressesThePrompt()
    {
        var doc = Build(Peap(servers: new[] { "radius.univ.ac.jp" }));

        doc.Descendants(MsPeap + "DisableUserPromptForServerValidation")
            .Single().Value.Should().Be("true",
                because: "a user who pinned a server name must not be able to click through "
                       + "a rogue RADIUS certificate and leak their MSCHAPv2 credentials");
    }

    [Fact]
    public void Peap_WithPinnedTrustedRootCa_SuppressesThePrompt()
    {
        var doc = Build(Peap(cas: new[] { Thumb }));

        doc.Descendants(MsPeap + "DisableUserPromptForServerValidation")
            .Single().Value.Should().Be("true");
    }

    [Fact]
    public void Peap_WithNoValidationMaterial_KeepsThePromptAvailable()
    {
        // 照合対象が無い状態で厳格化すると、初回設定や CAT 未導入の環境で
        // 接続手段が失われる。ここは意図的に従来どおり。
        var doc = Build(Peap());

        doc.Descendants(MsPeap + "DisableUserPromptForServerValidation")
            .Single().Value.Should().Be("false");
    }

    // ── EAP-TLS ──────────────────────────────────────────────────────

    [Fact]
    public void EapTls_WithPinnedServerName_SuppressesThePrompt()
    {
        var doc = Build(new WifiProfileSpec
        {
            Ssid = "CorpNet",
            Auth = AuthMethod.WPA2Enterprise,
            EapType = EapType.EAP_TLS,
            ServerNames = new[] { "radius.corp.example" },
        });

        doc.Descendants(EapTls + "DisableUserPromptForServerValidation")
            .Single().Value.Should().Be("true");
    }

    [Fact]
    public void EapTls_WithNoValidationMaterial_KeepsThePromptAvailable()
    {
        var doc = Build(new WifiProfileSpec
        {
            Ssid = "CorpNet",
            Auth = AuthMethod.WPA2Enterprise,
            EapType = EapType.EAP_TLS,
        });

        doc.Descendants(EapTls + "DisableUserPromptForServerValidation")
            .Single().Value.Should().Be("false");
    }

    // ── EAP-TTLS ─────────────────────────────────────────────────────
    // TTLS のスキーマでは要素名が DisablePrompt だが意味は同じ
    // (true = プロンプト抑止 = 厳格)。

    [Fact]
    public void EapTtls_WithPinnedTrustedRootCa_SuppressesThePrompt()
    {
        var doc = Build(new WifiProfileSpec
        {
            Ssid = "eduroam",
            Auth = AuthMethod.WPA2Enterprise,
            EapType = EapType.EAP_TTLS,
            Username = "student@univ.ac.jp",
            Password = "pw",
            TrustedRootCaThumbprints = new[] { Thumb },
        });

        doc.Descendants(EapTtls + "DisablePrompt")
            .Single().Value.Should().Be("true");
    }

    [Fact]
    public void EapTtls_WithNoValidationMaterial_KeepsThePromptAvailable()
    {
        var doc = Build(new WifiProfileSpec
        {
            Ssid = "eduroam",
            Auth = AuthMethod.WPA2Enterprise,
            EapType = EapType.EAP_TTLS,
            Username = "student@univ.ac.jp",
            Password = "pw",
        });

        doc.Descendants(EapTtls + "DisablePrompt")
            .Single().Value.Should().Be("false");
    }

    // ── PEAP の PeapExtensions (V2 スキーマ) ─────────────────────────
    // 従来 PeapExtensions は空要素で、EAP-TLS だけが V2 の PerformServerValidation /
    // AcceptServerName を明示していた。最も広く使われる PEAP が緩いままだと
    // そこが最弱リンクになるため揃えた。
    // PeapExtensionsType は xs:sequence で順序が規定されている:
    //   PerformServerValidation → AcceptServerName → IdentityPrivacy → PeapExtensionsV2
    // 順序を誤ると Windows が取り込み時にプロファイル全体を拒否するため、順序も固定する。

    private static readonly XNamespace MsPeapV2 =
        "http://www.microsoft.com/provisioning/MsPeapConnectionPropertiesV2";

    [Fact]
    public void Peap_WithPinning_EmitsPerformServerValidation()
    {
        var doc = Build(Peap(servers: new[] { "radius.univ.ac.jp" }, cas: new[] { Thumb }));

        doc.Descendants(MsPeapV2 + "PerformServerValidation")
            .Single().Value.Should().Be("true");
    }

    [Fact]
    public void Peap_WithServerName_EmitsAcceptServerName()
    {
        var doc = Build(Peap(servers: new[] { "radius.univ.ac.jp" }));

        doc.Descendants(MsPeapV2 + "AcceptServerName")
            .Single().Value.Should().Be("true");
    }

    [Fact]
    public void Peap_WithCaButNoServerName_DoesNotClaimToMatchAServerName()
    {
        // 照合先 (ServerNames) が空なのに AcceptServerName=true を出すと
        // 検証が成立しない。CA のみのピン留めでは出してはならない。
        var doc = Build(Peap(cas: new[] { Thumb }));

        doc.Descendants(MsPeapV2 + "AcceptServerName").Should().BeEmpty();
        // 一方 PerformServerValidation は CA があるので出る
        doc.Descendants(MsPeapV2 + "PerformServerValidation").Should().ContainSingle();
    }

    [Fact]
    public void Peap_WithoutPinning_EmitsEmptyPeapExtensions()
    {
        // 何も指定が無ければ従来どおり空 — 挙動を変えない。
        var doc = Build(Peap());

        doc.Descendants(MsPeapV2 + "PerformServerValidation").Should().BeEmpty();
        doc.Descendants(MsPeapV2 + "AcceptServerName").Should().BeEmpty();
        doc.Descendants(MsPeapV2 + "IdentityPrivacy").Should().BeEmpty();
    }

    [Fact]
    public void Peap_WithDomain_EnablesIdentityPrivacyWithThatValue()
    {
        // PEAP の外部アイデンティティは TLS トンネル確立前に平文で送られる。
        // --domain が指定された場合はそれを匿名アイデンティティとして使う。
        var spec = Peap(servers: new[] { "radius.univ.ac.jp" });
        spec = spec with { Domain = "anonymous@univ.ac.jp" };

        var doc = Build(spec);
        var privacy = doc.Descendants(MsPeapV2 + "IdentityPrivacy").Single();

        privacy.Element(MsPeapV2 + "EnableIdentityPrivacy")!.Value.Should().Be("true");
        privacy.Element(MsPeapV2 + "AnonymousUserName")!.Value.Should().Be("anonymous@univ.ac.jp");
    }

    [Fact]
    public void Peap_WithoutDomain_LeavesIdentityPrivacyOff_SoRealmRoutingKeepsWorking()
    {
        // 既定で有効化してはならない。eduroam 等の RADIUS 配備は外部アイデンティティの
        // realm で経路制御しており、realm を欠いた "anonymous" を送ると認証が届かなくなる。
        var doc = Build(Peap(servers: new[] { "radius.univ.ac.jp" }));

        doc.Descendants(MsPeapV2 + "IdentityPrivacy").Should().BeEmpty(
            because: "a bare anonymous identity would break realm-based RADIUS routing");
    }

    [Fact]
    public void PeapExtensions_ChildrenFollowTheSchemaSequenceOrder()
    {
        // PeapExtensionsType は xs:sequence。順序違反は取り込み拒否になりうる。
        var spec = Peap(servers: new[] { "radius.univ.ac.jp" }, cas: new[] { Thumb });
        spec = spec with { Domain = "anonymous@univ.ac.jp" };

        var doc = Build(spec);
        var ext = doc.Descendants(
            (XNamespace)"http://www.microsoft.com/provisioning/MsPeapConnectionPropertiesV1"
            + "PeapExtensions").Single();

        ext.Elements().Select(e => e.Name.LocalName).Should().Equal(
            "PerformServerValidation", "AcceptServerName", "IdentityPrivacy");
    }

    // ── 全方式で一貫していること ──────────────────────────────────────

    [Theory]
    [InlineData(EapType.PEAP_MSCHAPv2)]
    [InlineData(EapType.EAP_TLS)]
    [InlineData(EapType.EAP_TTLS)]
    public void AllEapMethods_HonourPinning_Consistently(EapType eap)
    {
        // どの方式でも「ピン留めしたら厳格」が成り立つこと。
        // 一方式だけ緩いと、そこが攻撃者にとっての最弱リンクになる。
        var spec = new WifiProfileSpec
        {
            Ssid = "eduroam",
            Auth = AuthMethod.WPA2Enterprise,
            EapType = eap,
            Username = "student@univ.ac.jp",
            Password = "pw",
            ServerNames = new[] { "radius.univ.ac.jp" },
            TrustedRootCaThumbprints = new[] { Thumb },
        };

        var xml = ProfileXmlBuilder.Build(spec);
        var doc = XDocument.Parse(xml);

        var values = new List<string>();
        values.AddRange(doc.Descendants(MsPeap + "DisableUserPromptForServerValidation").Select(e => e.Value));
        values.AddRange(doc.Descendants(EapTls + "DisableUserPromptForServerValidation").Select(e => e.Value));
        values.AddRange(doc.Descendants(EapTtls + "DisablePrompt").Select(e => e.Value));

        values.Should().NotBeEmpty(because: "every EAP method must emit a server-validation prompt setting");
        values.Should().AllBe("true");
    }
}
