using System;
using System.Collections.Generic;
using System.Linq;

namespace MWC.Core.Services;

/// <summary>
/// MAC アドレスそのものから、ランダム化されているかどうかを判定する。
///
/// **なぜ Core に置けるのか**(この判断は一度誤っていたので理由を残す):
///
/// 以前この機能は「Windows の設定値を読む必要があるので Core には切り出せない」と
/// 記録されていた。それは**問いを取り違えていた**。確かに「ランダムハードウェア
/// アドレス」という *設定* はビーコンにもアドレスにも現れない。しかし勧告に必要なのは
/// 設定ではなく **効果** — 「いま使われている MAC はランダム化されたものか」— であり、
/// これはアドレスのバイト列だけで判定できる。
///
/// 根拠は IEEE 802 のアドレス形式そのもの:
///   - オクテット 0 の bit 1 = **Locally Administered (LAA)**。
///     IEEE が割り当てた焼き込みアドレスは必ずこのビットが 0(Universally Administered)。
///     ランダム生成した MAC は実在の OUI と衝突しないよう LAA を 1 にする決まりで、
///     Windows のランダム化もこれに従う。
///   - オクテット 0 の bit 0 = Group/Multicast。端末アドレスでは常に 0。
///
/// したがって **LAA=1 ならランダム化済み**、**LAA=0 かつ OUI が実在ベンダに解決すれば
/// 焼き込みアドレス**と判定できる。設定の読み取りは要らない。
///
/// 判定できないのは「ランダム化の *種類*」(ネットワーク別か日次か)だけで、
/// これは複数回の観測を突き合わせれば分かる(<see cref="FromHistory"/>)。
///
/// 観測値からの推定であり、OS 設定の問い合わせではない。したがってユーザーが設定を
/// 変更した直後など、再接続までは古い判定が出ることがある。
/// </summary>
public static class MacAddressModeInference
{
    /// <summary>(control mutant: comment only)</summary>
    public const byte LocallyAdministeredBit = 0x02;

    /// <summary>IEEE 802 オクテット 0 の bit 0 — Group/Multicast。端末アドレスでは 0。</summary>
    public const byte GroupBit = 0x01;

    /// <summary>
    /// 単一の MAC アドレスから判定する。種類(日次/ネットワーク別)までは決まらないため、
    /// ランダム化されていることだけが分かった場合は <see cref="MacAddressMode.Randomized"/> を返す。
    /// </summary>
    /// <param name="mac">6 バイトの MAC アドレス。</param>
    /// <param name="oui">
    /// 焼き込みアドレスの裏取りに使う OUI DB(任意)。渡すと判定根拠が具体的になるが、
    /// 内蔵 DB は IEEE 全体の部分集合なので、**解決しないことは randomized の根拠にならない**。
    /// </param>
    public static MacModeInference FromAddress(ReadOnlySpan<byte> mac, OuiLookupService? oui = null)
    {
        if (mac.Length != 6)
            return new MacModeInference(MacAddressMode.Unknown, false, MacModeEvidence.MalformedAddress);

        if ((mac[0] & GroupBit) != 0)
            return new MacModeInference(MacAddressMode.Unknown, false, MacModeEvidence.NotAUnicastAddress);

        if ((mac[0] & LocallyAdministeredBit) != 0)
            return new MacModeInference(MacAddressMode.Randomized, true,
                                        MacModeEvidence.LocallyAdministeredBitSet);

        // ここから先は LAA=0。焼き込みアドレスであることはほぼ確定している。
        // OUI が引ければ根拠が強くなるだけで、引けなくても結論は変わらない
        // (内蔵 DB は代表的な OUI の抜粋にすぎないため)。
        var vendor = oui?.Lookup(mac);
        return new MacModeInference(
            MacAddressMode.Hardware, false,
            vendor is null ? MacModeEvidence.UniversallyAdministered
                           : MacModeEvidence.UniversallyAdministeredWithKnownVendor);
    }

    /// <summary>
    /// 同一端末の複数観測から、ランダム化の**種類**まで判定する。
    ///
    /// 判定規則:
    ///   - どの観測も焼き込みアドレス   → <see cref="MacAddressMode.Hardware"/>
    ///   - 同じ SSID で日をまたいで変化 → <see cref="MacAddressMode.RandomDaily"/>
    ///   - SSID ごとに別だが同 SSID 内では安定 → <see cref="MacAddressMode.RandomPerNetwork"/>
    ///   - ランダムだが上記を区別できるだけの観測が無い → <see cref="MacAddressMode.Randomized"/>
    ///
    /// 観測が 0 件なら <see cref="MacAddressMode.Unknown"/>。
    /// </summary>
    public static MacModeInference FromHistory(IReadOnlyList<MacObservation> observations,
                                               OuiLookupService? oui = null)
    {
        if (observations is null || observations.Count == 0)
            return new MacModeInference(MacAddressMode.Unknown, false, MacModeEvidence.NoObservations);

        var perAddress = observations
            .Select(o => FromAddress(o.Address.AsSpan(), oui))
            .ToList();

        if (perAddress.All(r => r.Mode == MacAddressMode.Hardware))
            return new MacModeInference(MacAddressMode.Hardware, false,
                                        MacModeEvidence.UniversallyAdministered);

        if (!perAddress.Any(r => r.IsRandomized))
            return new MacModeInference(MacAddressMode.Unknown, false, MacModeEvidence.MalformedAddress);

        // 同じ SSID の中でアドレスが変化しているか = 日次ローテーションの徴候。
        foreach (var group in observations.GroupBy(o => o.Ssid, StringComparer.Ordinal))
        {
            var distinct = group.Select(o => Format(o.Address)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count <= 1) continue;

            // 同一 SSID で複数のアドレス。別の日にまたがっていれば日次ローテーション。
            var days = group.Select(o => o.ObservedAt.UtcDateTime.Date).Distinct().Count();
            if (days > 1)
                return new MacModeInference(MacAddressMode.RandomDaily, true,
                                            MacModeEvidence.AddressChangedWithinSameSsidAcrossDays);
        }

        // SSID ごとにアドレスが違い、同 SSID 内では一定 = ネットワーク別ランダム。
        var ssids = observations.Select(o => o.Ssid).Distinct(StringComparer.Ordinal).Count();
        var addrs = observations.Select(o => Format(o.Address)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (ssids > 1 && addrs >= ssids)
            return new MacModeInference(MacAddressMode.RandomPerNetwork, true,
                                        MacModeEvidence.AddressDiffersPerSsid);

        return new MacModeInference(MacAddressMode.Randomized, true,
                                    MacModeEvidence.LocallyAdministeredBitSet);
    }

    /// <summary>"AA:BB:CC:DD:EE:FF" 等の文字列を 6 バイトへ。区切りは : - . と無しを許容。</summary>
    public static bool TryParse(string? text, out byte[] mac)
    {
        mac = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(text)) return false;

        var hex = text.Replace(":", "").Replace("-", "").Replace(".", "").Trim();
        if (hex.Length != 12) return false;

        var bytes = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber,
                               System.Globalization.CultureInfo.InvariantCulture, out bytes[i]))
                return false;
        }
        mac = bytes;
        return true;
    }

    private static string Format(byte[] mac) => Convert.ToHexString(mac);
}

// ── データ型 ─────────────────────────────────────────────────────────

/// <summary>1 回の MAC 観測(どの SSID に、いつ、どのアドレスで繋いだか)。</summary>
public sealed record MacObservation(string Ssid, byte[] Address, DateTimeOffset ObservedAt);

/// <summary>判定結果。<paramref name="IsRandomized"/> は種類が決まらなくても確定する。</summary>
public sealed record MacModeInference(
    MacAddressMode  Mode,
    bool            IsRandomized,
    MacModeEvidence Evidence);

/// <summary>判定の根拠。UI/CLI はこれを人間向け文言へ写す(Core は文言を持たない)。</summary>
public enum MacModeEvidence
{
    /// <summary>6 バイトでない、または解析できない。</summary>
    MalformedAddress,
    /// <summary>Group ビットが立っている = 端末アドレスではない。</summary>
    NotAUnicastAddress,
    /// <summary>観測が 1 件も無い。</summary>
    NoObservations,
    /// <summary>LAA ビットが立っている = ランダム生成アドレス。</summary>
    LocallyAdministeredBitSet,
    /// <summary>LAA ビットが 0 = IEEE 割当の焼き込みアドレス。</summary>
    UniversallyAdministered,
    /// <summary>LAA ビットが 0 で、かつ OUI が既知ベンダに解決した。</summary>
    UniversallyAdministeredWithKnownVendor,
    /// <summary>同一 SSID で日をまたいでアドレスが変化した。</summary>
    AddressChangedWithinSameSsidAcrossDays,
    /// <summary>SSID ごとにアドレスが異なる。</summary>
    AddressDiffersPerSsid
}
