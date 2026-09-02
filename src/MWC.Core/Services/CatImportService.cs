using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// eduroam CAT (Configuration Assistant Tool) XML インポートサービス。
///
/// eduroam は IEEE 802.1X / EAP-TLS / PEAP-MSCHAPv2 を使用する世界規模の大学 Wi-Fi ネットワーク。
/// CAT は各機関の接続設定を配布するツールで、XML 形式の設定ファイル(eap-config)を出力する。
///
/// 本サービスは eap-config XML を解析して WifiProfileSpec に変換し、
/// Windows WLAN プロファイルとして登録できる形式にする。
///
/// 仕様参考: RFC 7585, draft-ietf-emu-eap-arpa, CAT API https://cat.eduroam.org/
/// </summary>
public sealed class CatImportService
{
    private static readonly XNamespace Ns = "urn:ietf:params:xml:ns:yang:ietf-eap-metadata";

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>
    /// CAT XML ファイル (.eap-config / .xml) をパースして接続プロファイル一覧を返す。
    /// </summary>
    /// <param name="xmlContent">eap-config XML 文字列</param>
    /// <returns>変換された WifiProfileSpec のリスト(SSID ごと)</returns>
    public IReadOnlyList<CatProfile> ParseEapConfig(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
            throw new ArgumentException("CAT XML content is empty.", nameof(xmlContent));

        // CAT / eap-config は eduroam からダウンロードされる **信頼できない外部 XML**。
        // XXE (外部実体によるローカルファイル漏洩 / SSRF) と DTD 実体展開 (billion laughs DoS)
        // を境界で明示的に封じる。XDocument.Parse は .NET の既定で安全だが、フレームワーク
        // 既定に依存せず不変条件をローカルに可視化・監査可能にする (CA3075 / OWASP XXE Prevention)。
        //   - DtdProcessing.Prohibit : <!DOCTYPE> を拒否し実体展開を不可能にする
        //   - XmlResolver = null     : 外部 DTD / 外部実体を一切解決しない
        var settings = new XmlReaderSettings
        {
            DtdProcessing             = DtdProcessing.Prohibit,
            XmlResolver               = null,
            MaxCharactersFromEntities = 0,
        };
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xmlContent), settings);
            doc = XDocument.Load(reader);
        }
        catch (Exception ex) { throw new FormatException($"Failed to parse CAT XML: {ex.Message}", ex); }

        var root = doc.Root ?? throw new FormatException("XML has no root element.");

        // NameSpace 検出(CAT v1.0 / v2.0 両対応)
        var ns = root.GetDefaultNamespace();

        var profiles = new List<CatProfile>();

        // EAPIdentityProvider 要素を巡回。
        //
        // **名前空間が無い文書で二重に数える不具合があった**: `ns` が空のとき
        // `ns + "EAPIdentityProvider"` は `"EAPIdentityProvider"` と同一になるため、
        // 下の 2 つの Descendants が**同じ要素を返し**、Concat で全プロファイルが
        // 倍になっていた (名前空間なしの CAT ファイルを取り込むと、各ネットワークが
        // 2 回現れる)。名前空間付きで拾えたときはそれを使い、空のときだけ
        // 名前空間なしの古い形式にフォールバックする。
        var providers = root.Descendants(ns + "EAPIdentityProvider").ToList();
        if (providers.Count == 0 && ns != XNamespace.None)
            providers = root.Descendants("EAPIdentityProvider").ToList();

        foreach (var provider in providers)
        {
            var profile = ParseProvider(provider, ns);
            if (profile is not null) profiles.Add(profile);
        }

        if (profiles.Count == 0)
            throw new FormatException("No valid EAPIdentityProvider found.");

        return profiles;
    }

    /// <summary>
    /// CAT プロファイルから接続 spec の「組織側で決まる部分」を組み立てる。
    ///
    /// **利用者の資格情報 (<see cref="WifiProfileSpec.Username"/> /
    /// <see cref="WifiProfileSpec.Password"/>) は入らない。** eduroam CAT の XML は
    /// 設計上それらを含まない — 各利用者が自分の学内アカウントを後から入力する方式だからである。
    /// したがって PEAP / EAP-TTLS では、この spec 単体は
    /// <see cref="WifiProfileSpec.Validate"/> を通らない(username+password 必須)。
    /// 呼び出し側が `with { Username = ..., Password = ... }` で補うこと。
    /// CLI の `mwc import-cat` がその参照実装。
    ///
    /// マッピングの注意: CAT の AnonymousIdentity は **外部 (Phase 1) アイデンティティ**であり、
    /// 本 spec では <see cref="WifiProfileSpec.Domain"/> に入る
    /// (<see cref="Profile.ProfileXmlBuilder"/> がここを PEAP の AnonymousUserName /
    ///  EAP-TTLS の匿名 ID として平文送出する)。`Username` は逆にトンネル内で使う実 ID なので
    /// 匿名 ID を入れてはならない — 2026-07 までここが取り違えられていた
    /// (未配線だったため露見していなかった)。
    /// CAT が AnonymousIdentity を明示しない場合は realm から `anonymous@realm` を組み立てる。
    /// realm も無ければ null のままにする(ProfileXmlBuilder 側が既定を決める)。
    /// </summary>
    public WifiProfileSpec BuildEduroamSpec(CatProfile profile)
    {
        var outerIdentity = !string.IsNullOrWhiteSpace(profile.AnonymousIdentity)
            ? profile.AnonymousIdentity
            : !string.IsNullOrWhiteSpace(profile.Domain)
                ? $"anonymous@{profile.Domain}"
                : null;

        return new WifiProfileSpec
        {
            Ssid                    = profile.Ssid,
            Auth                    = AuthMethod.WPA2Enterprise,
            EapType                 = profile.EapType,
            ServerNames             = profile.ServerNames.ToArray(),
            TrustedRootCaThumbprints = profile.CaThumbprints.ToArray(),
            Domain                  = outerIdentity,
        };
    }

    // ── Private ─────────────────────────────────────────────────────

    private static CatProfile? ParseProvider(XElement provider, XNamespace ns)
    {
        // SSID 抽出
        var ssidElement = provider.Descendants(ns + "SSID")
            .Concat(provider.Descendants("SSID"))
            .FirstOrDefault();
        var ssid = ssidElement?.Value?.Trim() ?? "eduroam";

        // EAP type 抽出
        var eapTypeEl = provider.Descendants(ns + "EAPMethod")
            .Concat(provider.Descendants("EAPMethod"))
            .SelectMany(el => el.Descendants(ns + "Type").Concat(el.Descendants("Type")))
            .FirstOrDefault();
        if (!int.TryParse(eapTypeEl?.Value, out var eapTypeNum)) return null;
        var eapType = (EapType)eapTypeNum;

        // Server name 抽出
        var serverNames = provider.Descendants(ns + "ServerName")
            .Concat(provider.Descendants("ServerName"))
            .Select(el => el.Value.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        // CA 証明書 (Base64 DER) → SHA-1 サムプリントを取得
        var caThumbs = new List<string>();
        foreach (var caEl in provider.Descendants(ns + "CA").Concat(provider.Descendants("CA")))
        {
            var base64 = caEl.Value.Trim().Replace("\n", "").Replace("\r", "").Replace(" ", "");
            var thumb  = ComputeThumbprint(base64);
            if (thumb is not null) caThumbs.Add(thumb);
        }

        // 匿名 ID
        var anonId = provider.Descendants(ns + "AnonymousIdentity")
            .Concat(provider.Descendants("AnonymousIdentity"))
            .FirstOrDefault()?.Value?.Trim();

        // 組織名
        var orgName = provider.Descendants(ns + "DisplayName")
            .Concat(provider.Descendants("DisplayName"))
            .FirstOrDefault()?.Value?.Trim();

        // ドメイン
        var domain = provider.Descendants(ns + "Domain")
            .Concat(provider.Descendants("Domain"))
            .FirstOrDefault()?.Value?.Trim();

        return new CatProfile(
            Ssid:              ssid,
            OrganizationName:  orgName ?? ssid,
            EapType:           eapType,
            ServerNames:       serverNames,
            CaThumbprints:     caThumbs,
            AnonymousIdentity: anonId,
            Domain:            domain);
    }

    private static string? ComputeThumbprint(string base64Der)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64Der);
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            var hash = sha1.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "");
        }
        catch { return null; }
    }
}

/// <summary>CAT XML から解析した接続プロファイル</summary>
public sealed record CatProfile(
    string             Ssid,
    string             OrganizationName,
    EapType            EapType,
    IReadOnlyList<string> ServerNames,
    IReadOnlyList<string> CaThumbprints,
    string?            AnonymousIdentity,
    string?            Domain)
{
    /// <summary>接続に必要な情報が揃っているか</summary>
    public bool IsValid =>
        !string.IsNullOrEmpty(Ssid) &&
        ServerNames.Count > 0 &&
        (EapType == EapType.PEAP_MSCHAPv2 || EapType == EapType.EAP_TLS || EapType == EapType.EAP_TTLS);
}
