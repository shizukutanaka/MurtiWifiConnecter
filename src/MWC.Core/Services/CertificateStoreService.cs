using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// EAP-TLS クライアント証明書ストア選択サービス。
///
/// EAP-TLS は企業 Wi-Fi で最も安全な認証方式。
/// ユーザーまたはコンピューターの証明書ストアから
/// 適切な証明書を選んでプロファイルに設定する。
///
/// 機能:
///   1. 現在のユーザー証明書ストアから EAP-TLS 対応証明書を列挙
///   2. 有効期限・失効・鍵使用法チェック
///   3. RADIUS サーバー証明書チェーン検証
///   4. 証明書 → WifiProfileSpec への自動マッピング
/// </summary>
public sealed class CertificateStoreService
{
    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// 現在ユーザーの証明書ストアから EAP-TLS 用クライアント証明書を列挙する。
    /// 条件: 有効期限内 / Client Authentication EKU / 秘密鍵あり
    /// </summary>
    /// <summary>Windows 証明書ストアから EAP-TLS 用クライアント証明書を列挙する。</summary>
    public IReadOnlyList<ClientCertInfo> GetClientCertificates(
        StoreLocation location = StoreLocation.CurrentUser)
    {
        var results = new List<ClientCertInfo>();
        try
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

            foreach (var cert in store.Certificates)
            {
                if (!IsEapTlsSuitable(cert)) continue;
                results.Add(FromCertificate(cert));
            }
        }
        catch (CryptographicException)
        {
            // 証明書ストアにアクセスできない環境では空を返す
        }
        return results.OrderByDescending(c => c.NotAfter).ToList();
    }

    /// <summary>
    /// 証明書サムプリント(SHA-1 16進)から証明書を取得する。
    /// </summary>
    public ClientCertInfo? FindByThumbprint(string thumbprint,
        StoreLocation location = StoreLocation.CurrentUser)
    {
        try
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

            var cert = store.Certificates.Find(
                X509FindType.FindByThumbprint, thumbprint, false)
                .FirstOrDefault();

            return cert is null ? null : FromCertificate(cert);
        }
        catch { return null; }
    }

    /// <summary>
    /// RADIUS サーバー証明書を検証し、信頼できる CA から発行されているか確認する。
    /// </summary>
    public RadiusCertValidationResult ValidateRadiusCert(
        byte[] derBytes,
        string? expectedHostname = null)
    {
        X509Certificate2 cert;
        try { cert = new X509Certificate2(derBytes); }
        catch (Exception ex)
        { return new(false, "Failed to load certificate", ex.Message, null, null); }

        using (cert)
        {
            // 有効期限
            if (DateTime.UtcNow < cert.NotBefore || DateTime.UtcNow > cert.NotAfter)
                return new(false, "Certificate expired",
                    $"Valid: {cert.NotBefore:yyyy-MM-dd} – {cert.NotAfter:yyyy-MM-dd}",
                    cert.Thumbprint, null);

            // チェーン検証
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode     = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag     = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.VerificationFlags  = X509VerificationFlags.NoFlag;

            bool chainValid = chain.Build(cert);

            // ホスト名確認
            if (expectedHostname is not null)
            {
                var cn = cert.GetNameInfo(X509NameType.DnsName, false);
                if (!MatchesHostname(cn, expectedHostname))
                    return new(false,
                        "Hostname mismatch",
                        $"Certificate: {cn}, expected: {expectedHostname}",
                        cert.Thumbprint,
                        cert.GetNameInfo(X509NameType.SimpleName, false));
            }

            var errors = chain.ChainStatus
                .Select(s => s.StatusInformation.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            return new(
                chainValid && errors.Count == 0,
                chainValid ? "Validation succeeded" : "Chain validation failed",
                string.Join("; ", errors),
                cert.Thumbprint,
                cert.GetNameInfo(X509NameType.SimpleName, false));
        }
    }

    /// <summary>
    /// 証明書情報から WifiProfileSpec を生成(EAP-TLS 用)。
    /// </summary>
    public WifiProfileSpec BuildEapTlsSpec(
        string ssid,
        ClientCertInfo cert,
        IEnumerable<string>? serverNames = null,
        AuthMethod auth = AuthMethod.WPA2Enterprise)
        => new()
        {
            Ssid                    = ssid,
            Auth                    = auth,
            EapType                 = EapType.EAP_TLS,
            ClientCertThumbprint    = cert.Thumbprint,
            ServerNames             = serverNames?.ToArray() ?? Array.Empty<string>(),
        };

    // ── Private ─────────────────────────────────────────────────────

    private static bool IsEapTlsSuitable(X509Certificate2 cert)
    {
        if (!cert.HasPrivateKey) return false;
        if (DateTime.UtcNow < cert.NotBefore || DateTime.UtcNow > cert.NotAfter) return false;

        // Client Authentication EKU (1.3.6.1.5.5.7.3.2) 確認
        var eku = cert.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .FirstOrDefault();
        if (eku is null) return true; // EKU なし = 汎用証明書として許可

        return eku.EnhancedKeyUsages
            .Cast<Oid>()
            .Any(o => o.Value == "1.3.6.1.5.5.7.3.2");
    }

    private static ClientCertInfo FromCertificate(X509Certificate2 cert)
    {
        var san = cert.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .SelectMany(e => e.EnumerateDnsNames())
            .ToList();

        return new ClientCertInfo(
            Subject:      cert.Subject,
            Thumbprint:   cert.Thumbprint,
            Issuer:       cert.Issuer,
            NotBefore:    cert.NotBefore,
            NotAfter:     cert.NotAfter,
            HasPrivateKey: cert.HasPrivateKey,
            SubjectAltNames: san,
            FriendlyName:  cert.FriendlyName);
    }

    private static bool MatchesHostname(string cn, string expected)
    {
        if (string.Equals(cn, expected, StringComparison.OrdinalIgnoreCase)) return true;
        // ワイルドカード: *.example.com
        if (cn.StartsWith("*."))
        {
            var suffix = cn[1..];  // .example.com
            return expected.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}

// ── データ型 ────────────────────────────────────────────────────────

/// <summary>クライアント証明書情報</summary>
public sealed record ClientCertInfo(
    string             Subject,
    string             Thumbprint,
    string             Issuer,
    DateTime           NotBefore,
    DateTime           NotAfter,
    bool               HasPrivateKey,
    IReadOnlyList<string> SubjectAltNames,
    string             FriendlyName)
{
    /// <summary>有効期限まで残り日数</summary>
    public int DaysUntilExpiry => (int)(NotAfter - DateTime.UtcNow).TotalDays;

    /// <summary>表示用ラベル</summary>
    public string DisplayLabel =>
        string.IsNullOrEmpty(FriendlyName) ? Subject : FriendlyName;
}

/// <summary>RADIUS証明書検証結果</summary>
public sealed record RadiusCertValidationResult(
    bool    IsValid,
    string  Summary,
    string  Detail,
    string? Thumbprint,
    string? CommonName);
