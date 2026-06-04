namespace MWC.Core.Models;

/// <summary>
/// WLAN認証方式。WLANProfile XML の authentication 要素にマップ。
/// </summary>
public enum AuthMethod
{
    /// <summary>暗号化なし</summary>
    Open,
    /// <summary>OWE (RFC 8110, Enhanced Open) — 暗号化付き匿名接続</summary>
    OWE,
    /// <summary>WEP (非推奨、警告必須)</summary>
    WEP,
    /// <summary>WPA Personal</summary>
    WPAPSK,
    /// <summary>WPA2 Personal (現行PSK標準)</summary>
    WPA2PSK,
    /// <summary>WPA3 Personal (SAE)</summary>
    WPA3SAE,
    /// <summary>WPA2/WPA3 Transition (PSK+SAE併用)</summary>
    WPA3Transition,
    /// <summary>WPA2 Enterprise</summary>
    WPA2Enterprise,
    /// <summary>WPA3 Enterprise</summary>
    WPA3Enterprise,
    /// <summary>WPA3 Enterprise 192-bit (CNSA)</summary>
    WPA3Enterprise192
}

public enum CipherType
{
    None,
    WEP,
    TKIP,
    AES,        // CCMP
    GCMP256
}

public enum EapType
{
    /// <summary>PEAP-MSCHAPv2 (Type 25)</summary>
    PEAP_MSCHAPv2 = 25,
    /// <summary>EAP-TLS (Type 13)</summary>
    EAP_TLS = 13,
    /// <summary>EAP-AKA (Type 23)</summary>
    EAP_AKA = 23,
    /// <summary>EAP-TTLS (Type 21)</summary>
    EAP_TTLS = 21
}
