using System;
using System.Xml.Linq;
using MWC.Core.Models;

namespace MWC.Core.Profile;

/// <summary>
/// WLANProfile XML 生成器。全認証方式網羅。
/// XElement経由でエスケープ自動、インジェクション不可。
///
/// 仕様根拠:
///   https://learn.microsoft.com/en-us/windows/win32/nativewifi/wireless-profile-samples
///   https://learn.microsoft.com/en-us/windows/win32/nativewifi/wlan-profileschema-elements
///
/// 設計方針(Carmack/Pike流):
///   - 状態を持たない static メソッド
///   - 失敗時は ArgumentException、成功時は完成XML文字列
///   - 各認証方式は private メソッドに分離、テスト容易
/// </summary>
public static class ProfileXmlBuilder
{
    // ───── 名前空間定義 ─────
    private static readonly XNamespace WlanNs   = "http://www.microsoft.com/networking/WLAN/profile/v1";
    private static readonly XNamespace OneXNs   = "http://www.microsoft.com/networking/OneX/v1";
    private static readonly XNamespace EhcNs    = "http://www.microsoft.com/provisioning/EapHostConfig";
    private static readonly XNamespace EcNs     = "http://www.microsoft.com/provisioning/EapCommon";
    private static readonly XNamespace BeNs     = "http://www.microsoft.com/provisioning/BaseEapMethodConfig";
    private static readonly XNamespace BepNs    = "http://www.microsoft.com/provisioning/BaseEapConnectionPropertiesV1";
    private static readonly XNamespace MsPeapNs = "http://www.microsoft.com/provisioning/MsPeapConnectionPropertiesV1";
    private static readonly XNamespace McNs     = "http://www.microsoft.com/provisioning/MsChapV2ConnectionPropertiesV1";
    private static readonly XNamespace EtNs     = "http://www.microsoft.com/provisioning/EapTlsConnectionPropertiesV1";

    /// <summary>WifiProfileSpec から Windows WLAN プロファイル XML を生成する。</summary>
    /// <summary>WifiProfileSpec から Windows WLAN プロファイル XML を生成する。</summary>
    /// <exception cref="ArgumentException">SSID / Passphrase が無効な場合</exception>
    public static string Build(WifiProfileSpec spec)
    {
        WifiProfileValidator.Validate(spec);
        ArgumentNullException.ThrowIfNull(spec);
        var v = spec.Validate();
        if (!v.IsValid) throw new ArgumentException(v.Error);

        var profile = new XElement(WlanNs + "WLANProfile",
            new XElement(WlanNs + "name", spec.Ssid),
            BuildSsidConfig(spec),
            new XElement(WlanNs + "connectionType", "ESS"),
            new XElement(WlanNs + "connectionMode", spec.AutoConnect ? "auto" : "manual"),
            BuildMsm(spec)
        );

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), profile);
        // Windows WLANサービスは BOM 無し UTF-8 で受理
        return doc.Declaration + Environment.NewLine + profile.ToString();
    }

    // ───── SSID設定 ─────
    private static XElement BuildSsidConfig(WifiProfileSpec spec)
    {
        var ssidConfig = new XElement(WlanNs + "SSIDConfig",
            new XElement(WlanNs + "SSID",
                new XElement(WlanNs + "name", spec.Ssid)));
        if (spec.NonBroadcast)
            ssidConfig.Add(new XElement(WlanNs + "nonBroadcast", "true"));
        return ssidConfig;
    }

    // ───── MSM(セキュリティ) ─────
    private static XElement BuildMsm(WifiProfileSpec spec)
    {
        var (auth, enc, useOneX) = MapAuth(spec.Auth, spec.CipherOverride);

        var authEnc = new XElement(WlanNs + "authEncryption",
            new XElement(WlanNs + "authentication", auth),
            new XElement(WlanNs + "encryption", enc),
            new XElement(WlanNs + "useOneX", useOneX ? "true" : "false"));

        // WPA3-Transitionは追加要素
        if (spec.Auth == AuthMethod.WPA3Transition)
            authEnc.Add(new XElement(WlanNs + "transitionMode",
                XNamespace.Get("http://www.microsoft.com/networking/WLAN/profile/v4") + "transitionMode", "true"));

        var security = new XElement(WlanNs + "security", authEnc);

        // PMF(Protected Management Frames): WPA3 では required
        if (spec.Auth is AuthMethod.WPA3SAE or AuthMethod.WPA3Enterprise or AuthMethod.WPA3Enterprise192)
        {
            security.Add(new XElement(
                XNamespace.Get("http://www.microsoft.com/networking/WLAN/profile/v3") + "pmkCacheMode", "enabled"));
        }

        // 認証方式別ボディ
        switch (spec.Auth)
        {
            case AuthMethod.Open:
            case AuthMethod.OWE:
                // sharedKey なし
                break;

            case AuthMethod.WEP:
                security.Add(BuildWepKey(spec.Passphrase!));
                break;

            case AuthMethod.WPAPSK:
            case AuthMethod.WPA2PSK:
            case AuthMethod.WPA3SAE:
            case AuthMethod.WPA3Transition:
                security.Add(BuildPsk(spec.Passphrase!));
                break;

            case AuthMethod.WPA2Enterprise:
            case AuthMethod.WPA3Enterprise:
            case AuthMethod.WPA3Enterprise192:
                security.Add(BuildOneX(spec));
                break;
        }

        return new XElement(WlanNs + "MSM", security);
    }

    /// <summary>
    /// 認証方式 → (XML値: authentication, encryption, useOneX)
    /// </summary>
    private static (string auth, string enc, bool useOneX) MapAuth(AuthMethod m, CipherType? cipherOverride)
    {
        return m switch
        {
            AuthMethod.Open                => ("open",       "none",                              false),
            AuthMethod.OWE                 => ("OWE",        Cipher(cipherOverride, CipherType.AES),  false),
            AuthMethod.WEP                 => ("open",       "WEP",                               false),
            AuthMethod.WPAPSK              => ("WPAPSK",     Cipher(cipherOverride, CipherType.AES),  false),
            AuthMethod.WPA2PSK             => ("WPA2PSK",    Cipher(cipherOverride, CipherType.AES),  false),
            AuthMethod.WPA3SAE             => ("WPA3SAE",    "AES",                               false),
            AuthMethod.WPA3Transition      => ("WPA3SAE",    "AES",                               false),
            AuthMethod.WPA2Enterprise      => ("WPA2",       "AES",                               true),
            AuthMethod.WPA3Enterprise      => ("WPA3",       "AES",                               true),
            AuthMethod.WPA3Enterprise192   => ("WPA3ENT192", "GCMP256",                           true),
            _ => throw new ArgumentOutOfRangeException(nameof(m))
        };
    }

    private static string Cipher(CipherType? o, CipherType d)
    {
        var c = o ?? d;
        return c switch
        {
            CipherType.None    => "none",
            CipherType.WEP     => "WEP",
            CipherType.TKIP    => "TKIP",
            CipherType.AES     => "AES",
            CipherType.GCMP256 => "GCMP256",
            _ => "AES"
        };
    }

    // ───── PSK/WEP共有鍵 ─────
    private static XElement BuildPsk(string passphrase) =>
        new XElement(WlanNs + "sharedKey",
            new XElement(WlanNs + "keyType", "passPhrase"),
            new XElement(WlanNs + "protected", "false"),
            new XElement(WlanNs + "keyMaterial", passphrase));

    private static XElement BuildWepKey(string key)
    {
        // 16進(10/26/32桁)= networkKey、平文 = passPhrase
        bool isHex = key.Length is 10 or 26 or 32 &&
                     System.Linq.Enumerable.All(key, c =>
                         c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F'));
        return new XElement(WlanNs + "sharedKey",
            new XElement(WlanNs + "keyType", isHex ? "networkKey" : "passPhrase"),
            new XElement(WlanNs + "protected", "false"),
            new XElement(WlanNs + "keyMaterial", key));
    }

    // ───── 802.1X / EAP ─────
    private static XElement BuildOneX(WifiProfileSpec spec)
    {
        var eapType = spec.EapType ?? throw new ArgumentException("EAP type required");
        return new XElement(OneXNs + "OneX",
            new XElement(OneXNs + "authMode", "user"),
            new XElement(OneXNs + "EAPConfig",
                BuildEapHostConfig(eapType, spec)));
    }

    private static XElement BuildEapHostConfig(EapType eapType, WifiProfileSpec spec)
    {
        var eapHost = new XElement(EhcNs + "EapHostConfig",
            new XElement(EhcNs + "EapMethod",
                new XElement(EcNs + "Type", (int)eapType),
                new XElement(EcNs + "VendorId", 0),
                new XElement(EcNs + "VendorType", 0),
                new XElement(EcNs + "AuthorId", 0)));

        var config = new XElement(EhcNs + "Config");
        config.Add(eapType switch
        {
            EapType.PEAP_MSCHAPv2 => BuildPeapConfig(spec),
            EapType.EAP_TLS       => BuildEapTlsConfig(spec),
            _ => throw new NotSupportedException($"EAP type {eapType} not implemented yet")
        });
        eapHost.Add(config);
        return eapHost;
    }

    private static XElement BuildPeapConfig(WifiProfileSpec spec)
    {
        var serverValidation = new XElement(MsPeapNs + "ServerValidation",
            new XElement(MsPeapNs + "DisableUserPromptForServerValidation", "false"),
            new XElement(MsPeapNs + "ServerNames",
                spec.ServerNames is { Length: > 0 } ? string.Join(";", spec.ServerNames) : ""));
        foreach (var thumb in spec.TrustedRootCaThumbprints)
            serverValidation.Add(new XElement(MsPeapNs + "TrustedRootCA", thumb));

        return new XElement(BepNs + "Eap",
            new XElement(BepNs + "Type", 25),  // PEAP
            new XElement(MsPeapNs + "EapType",
                serverValidation,
                new XElement(MsPeapNs + "FastReconnect", "true"),
                new XElement(MsPeapNs + "InnerEapOptional", "false"),
                new XElement(BepNs + "Eap",
                    new XElement(BepNs + "Type", 26),  // MSCHAPv2
                    new XElement(McNs + "EapType",
                        new XElement(McNs + "UseWinLogonCredentials", "false"))),
                new XElement(MsPeapNs + "EnableQuarantineChecks", "false"),
                new XElement(MsPeapNs + "RequireCryptoBinding", "false"),
                new XElement(MsPeapNs + "PeapExtensions")));
    }

    private static XElement BuildEapTlsConfig(WifiProfileSpec spec)
    {
        var serverValidation = new XElement(EtNs + "ServerValidation",
            new XElement(EtNs + "DisableUserPromptForServerValidation", "false"),
            new XElement(EtNs + "ServerNames",
                string.Join(";", spec.ServerNames)));
        foreach (var thumb in spec.TrustedRootCaThumbprints)
            serverValidation.Add(new XElement(EtNs + "TrustedRootCA", thumb));

        return new XElement(BepNs + "Eap",
            new XElement(BepNs + "Type", 13),  // EAP-TLS
            new XElement(EtNs + "EapType",
                new XElement(EtNs + "CredentialsSource",
                    new XElement(EtNs + "CertificateStore",
                        new XElement(EtNs + "SimpleCertSelection", "true"))),
                serverValidation,
                new XElement(EtNs + "DifferentUsername", "false"),
                new XElement(EtNs + "PerformServerValidation",
                    XNamespace.Get("http://www.microsoft.com/provisioning/EapTlsConnectionPropertiesV2") + "PerformServerValidation",
                    "true"),
                new XElement(EtNs + "AcceptServerName",
                    XNamespace.Get("http://www.microsoft.com/provisioning/EapTlsConnectionPropertiesV2") + "AcceptServerName",
                    "true")));
    }
}
