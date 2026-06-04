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

            AuthMethod.WEP => string.IsNullOrEmpty(Passphrase)
                ? ProfileValidation.Fail("WEP key required")
                : ProfileValidation.Ok,

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
        // WPA/WPA2 PSK: 8-63 ASCII or 64-hex
        int len = Passphrase.Length;
        if (len < 8 || len > 64)
            return ProfileValidation.Fail("Passphrase length must be 8-63 chars or 64 hex");
        return ProfileValidation.Ok;
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
        if (EapType == Models.EapType.EAP_TLS)
        {
            if (string.IsNullOrEmpty(ClientCertThumbprint))
                return ProfileValidation.Fail("EAP-TLS needs client cert");
        }
        return ProfileValidation.Ok;
    }
}

public readonly record struct ProfileValidation(bool IsValid, string? Error)
{
    public static ProfileValidation Ok => new(true, null);
    public static ProfileValidation Fail(string error) => new(false, error);
}
