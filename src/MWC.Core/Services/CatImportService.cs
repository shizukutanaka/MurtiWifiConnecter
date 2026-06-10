using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        XDocument doc;
        try { doc = XDocument.Parse(xmlContent); }
        catch (Exception ex) { throw new FormatException($"Failed to parse CAT XML: {ex.Message}", ex); }

        var root = doc.Root ?? throw new FormatException("XML has no root element.");

        // NameSpace 検出(CAT v1.0 / v2.0 両対応)
        var ns = root.GetDefaultNamespace();

        var profiles = new List<CatProfile>();

        // EAPIdentityProvider 要素を巡回
        var providers = root.Descendants(ns + "EAPIdentityProvider")
            .Concat(root.Descendants("EAPIdentityProvider"));  // 名前空間なしの古い形式

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
    /// eduroam の標準 SSID ("eduroam") を対象にしたデフォルトプロファイルを生成。
    /// </summary>
    public WifiProfileSpec BuildEduroamSpec(CatProfile profile)
    {
        var auth = profile.EapType switch
        {
            EapType.EAP_TLS         => AuthMethod.WPA2Enterprise,
            EapType.PEAP_MSCHAPv2   => AuthMethod.WPA2Enterprise,
            EapType.EAP_TTLS        => AuthMethod.WPA2Enterprise,
            _                       => AuthMethod.WPA2Enterprise
        };

        return new WifiProfileSpec
        {
            Ssid                    = profile.Ssid,
            Auth                    = auth,
            EapType                 = profile.EapType,
            Username                = profile.AnonymousIdentity,   // 匿名ユーザー名
            ServerNames             = profile.ServerNames.ToArray(),
            TrustedRootCaThumbprints = profile.CaThumbprints.ToArray(),
            Domain                  = profile.Domain
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
