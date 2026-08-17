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
