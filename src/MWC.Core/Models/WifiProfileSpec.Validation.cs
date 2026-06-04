using System;
using System.Text;

namespace MWC.Core.Models;

/// <summary>
/// WifiProfileSpec の入力検証。
/// IEEE 802.11-2020 に準拠した制約を実施する。
///
/// SSID:        1-32 オクテット (UTF-8) — 空白 / 制御文字を拒否
/// Passphrase:  WPA2/WPA3: 8-63 ASCII 文字、または 64桁16進数 (raw PSK)
/// Open/OWE:    パスフレーズ不要
/// Enterprise:  パスフレーズ不要 (EAP credentials を使用)
/// </summary>
public static class WifiProfileValidator
{
    // ── 定数 ──────────────────────────────────────────────────────

    /// <summary>SSID 最大バイト長 (IEEE 802.11)</summary>
    public const int MaxSsidBytes      = 32;
    /// <summary>WPA/WPA2/WPA3 パスフレーズ最小文字数</summary>
    public const int MinPassphraseLen  = 8;
    /// <summary>WPA/WPA2/WPA3 パスフレーズ最大文字数</summary>
    public const int MaxPassphraseLen  = 63;
    /// <summary>64桁16進 raw PSK の長さ</summary>
    public const int RawPskLen         = 64;

    // ── 主エントリ ────────────────────────────────────────────────

    /// <summary>
    /// WifiProfileSpec 全体を検証し、問題があれば例外を投げる。
    /// </summary>
    /// <exception cref="ArgumentException">検証失敗</exception>
    public static void Validate(WifiProfileSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        ValidateSsid(spec.Ssid);
        ValidatePassphrase(spec.Auth, spec.Passphrase);
    }

    /// <summary>
    /// SSID の検証。
    /// - null/空文字列を拒否
    /// - UTF-8 エンコードで 32 バイト超を拒否
    /// - 制御文字を拒否
    /// </summary>
    public static void ValidateSsid(string ssid)
    {
        if (string.IsNullOrEmpty(ssid))
            throw new ArgumentException("SSID must not be null or empty.", nameof(ssid));

        var byteLen = Encoding.UTF8.GetByteCount(ssid);
        if (byteLen > MaxSsidBytes)
            throw new ArgumentException(
                $"SSID exceeds {MaxSsidBytes}-byte IEEE 802.11 limit (got {byteLen} bytes).",
                nameof(ssid));

        foreach (var c in ssid)
        {
            if (char.IsControl(c) && c != '\t')
                throw new ArgumentException(
                    $"SSID contains invalid control character U+{(int)c:X4}.",
                    nameof(ssid));
        }
    }

    /// <summary>
    /// パスフレーズの検証。認証方式に応じたルールを適用する。
    /// </summary>
    public static void ValidatePassphrase(AuthMethod auth, string? passphrase)
    {
        bool needsPass = auth is AuthMethod.WPA2PSK or AuthMethod.WPA3SAE
                                or AuthMethod.WPAPSK or AuthMethod.WEP;
        bool enterpriseAuth = auth is AuthMethod.WPA2Enterprise or AuthMethod.WPA3Enterprise
                                     or AuthMethod.WPA3Enterprise192;

        if (!needsPass && !enterpriseAuth)
        {
            // Open / OWE: passphrase は無視
            return;
        }

        if (enterpriseAuth)
        {
            // Enterprise: パスフレーズ不要 (EAP credentials を使用)
            return;
        }

        // PSK 系: passphrase 必須
        if (string.IsNullOrEmpty(passphrase))
            throw new ArgumentException(
                $"Passphrase is required for {auth}.", nameof(passphrase));

        // 64 桁 hex raw PSK は別扱い
        if (passphrase.Length == RawPskLen && IsHex(passphrase))
            return;

        if (passphrase.Length < MinPassphraseLen)
            throw new ArgumentException(
                $"Passphrase must be at least {MinPassphraseLen} characters " +
                $"(got {passphrase.Length}).", nameof(passphrase));

        if (passphrase.Length > MaxPassphraseLen)
            throw new ArgumentException(
                $"Passphrase must not exceed {MaxPassphraseLen} characters " +
                $"(got {passphrase.Length}).", nameof(passphrase));

        foreach (var c in passphrase)
        {
            if (c < 0x20 || c > 0x7E)
                throw new ArgumentException(
                    $"Passphrase contains non-ASCII printable character U+{(int)c:X4}. " +
                    "WPA passphrases must use ASCII 0x20-0x7E.", nameof(passphrase));
        }
    }

    // ── 試行的検証 (例外を投げない) ─────────────────────────────

    /// <summary>検証して問題なければ true を返す。</summary>
    public static bool TryValidate(WifiProfileSpec spec, out string? errorMessage)
    {
        try { Validate(spec); errorMessage = null; return true; }
        catch (Exception ex) { errorMessage = ex.Message; return false; }
    }

    /// <summary>SSID が有効かどうかを返す。</summary>
    public static bool IsValidSsid(string ssid)
    {
        try { ValidateSsid(ssid); return true; }
        catch { return false; }
    }

    // ── Private ─────────────────────────────────────────────────

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }
}
