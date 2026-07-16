using System;

namespace MWC.Core.Models;

/// <summary>
/// プロファイル生成リクエスト。
/// 認証方式に応じて必要フィールドが異なる。Validate() で整合性検証。
/// </summary>
public sealed record WifiProfileSpec
{
    public required string Ssid { get; init; }
    public required AuthMethod Auth { get; init; }
    public CipherType? CipherOverride { get; init; }
    public bool AutoConnect { get; init; } = true;
    public bool NonBroadcast { get; init; } = false;

    // PSK系
    public string? Passphrase { get; init; }

    // Enterprise系
    public EapType? EapType { get; init; }
    public string? Username { get; init; }          // PEAP-MSCHAPv2
    public string? Password { get; init; }          // PEAP-MSCHAPv2
    public string? Domain { get; init; }
    /// <summary>サーバ証明書検証対象FQDN(複数)</summary>
    public string[] ServerNames { get; init; } = Array.Empty<string>();
    /// <summary>信頼ルートCA証明書サムプリント(SHA-1, 16進)</summary>
    public string[] TrustedRootCaThumbprints { get; init; } = Array.Empty<string>();
    /// <summary>EAP-TLS用クライアント証明書サムプリント</summary>
    public string? ClientCertThumbprint { get; init; }

    public ProfileValidation Validate()
    {
        if (string.IsNullOrEmpty(Ssid))
            return ProfileValidation.Fail("SSID required");
        if (System.Text.Encoding.UTF8.GetByteCount(Ssid) > 32)
            return ProfileValidation.Fail("SSID exceeds 32 bytes");

        return Auth switch
        {
            AuthMethod.Open or AuthMethod.OWE => ProfileValidation.Ok,

            AuthMethod.WEP => ValidateWepKey(),

            AuthMethod.WPAPSK or AuthMethod.WPA2PSK
                or AuthMethod.WPA3SAE or AuthMethod.WPA3Transition =>
                ValidatePassphrase(),

            AuthMethod.WPA2Enterprise or AuthMethod.WPA3Enterprise
                or AuthMethod.WPA3Enterprise192 =>
                ValidateEnterprise(),

            _ => ProfileValidation.Fail("Unknown auth method")
        };
    }

    private ProfileValidation ValidatePassphrase()
    {
        if (string.IsNullOrEmpty(Passphrase))
            return ProfileValidation.Fail("Passphrase required");
        int len = Passphrase.Length;
        // WPA/WPA2/WPA3 PSK: 64 桁 hex の raw PSK は別扱い
        if (len == 64 && IsHex(Passphrase))
            return ProfileValidation.Ok;
        // それ以外は 8-63 ASCII printable (0x20-0x7E)
        if (len < 8 || len > 63)
            return ProfileValidation.Fail("Passphrase must be 8-63 ASCII chars or exactly 64 hex digits");
        foreach (var c in Passphrase)
            if (c < 0x20 || c > 0x7E)
                return ProfileValidation.Fail(
                    $"Passphrase contains non-ASCII printable character U+{(int)c:X4}; WPA passphrases must use ASCII 0x20-0x7E");
        return ProfileValidation.Ok;
    }

    private ProfileValidation ValidateWepKey()
    {
        if (string.IsNullOrEmpty(Passphrase))
            return ProfileValidation.Fail("WEP key required");
        int len = Passphrase.Length;
        bool ascii = (len == 5 || len == 13);
        bool hex   = (len == 10 || len == 26) && IsHex(Passphrase);
        return ascii || hex
            ? ProfileValidation.Ok
            : ProfileValidation.Fail("WEP key must be 5/13 ASCII chars or 10/26 hex digits");
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private ProfileValidation ValidateEnterprise()
    {
        if (EapType is null)
            return ProfileValidation.Fail("EAP type required");
        if (EapType == Models.EapType.PEAP_MSCHAPv2)
        {
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
                return ProfileValidation.Fail("PEAP needs username+password");
        }
        // EAP-TLS: ClientCertThumbprint is accepted as metadata but Windows WLAN profile XML
        // does not support specifying a client cert by thumbprint — SimpleCertSelection is used
        // and Windows auto-selects from the user cert store at connection time. No validation
        // error is raised here; a missing thumbprint means auto-selection without a hint.
        if (EapType == Models.EapType.EAP_TTLS)
        {
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
                return ProfileValidation.Fail("EAP-TTLS needs username+password");
        }
        if (EapType == Models.EapType.EAP_AKA)
            return ProfileValidation.Fail("EAP-AKA (SIM-based) is not supported");
        return ProfileValidation.Ok;
    }
}

public readonly record struct ProfileValidation(bool IsValid, string? Error)
{
    public static ProfileValidation Ok => new(true, null);
    public static ProfileValidation Fail(string error) => new(false, error);
}
