using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// VPN 使用可否に関する助言サービス。
///
/// ROADMAP.md 「VPN 自動切替(信頼済み AP では VPN オフ)」の助言基盤。
///
/// 本サービスは実際の VPN 接続状態を一切変更しない。OS の VPN 制御 API
/// (Windows RAS / VpnManagementAgent 等) との統合は環境依存が大きく、誤って
/// VPN を切ると機密トラフィックが露出するなど失敗時の影響が大きいため、
/// 本サービスは判断材料の提供のみに徹する — 他の *AdvisoryService と同じ
/// 「助言のみ、実行はしない」方針(<see cref="SecurityAdvisoryService"/>,
/// <see cref="PrivacyAdvisoryService"/> 参照)。
///
/// 判断軸:
///   1. 暗号化なし (Open) → 常に VPN 強く推奨(内容が誰にでも見える)
///   2. 未知のネットワーク(過去に MWC 経由で接続実績がない)→ 暗号化の有無に
///      関わらず VPN 推奨(AP 運営者や Evil Twin から内容が見える可能性)
///   3. 既知 + Enterprise → 通常は組織のファイアウォール/VPN を経由済みのため不要
///   4. 既知 + WPA3-SAE(transition mode でない)→ 任意(強固な暗号化)
///   5. 既知だが暗号化が弱い(WPA2 以下 / WPA3 transition mode)→ なお推奨
/// </summary>
public sealed class VpnAdvisoryService
{
    /// <summary>
    /// ネットワークの信頼性・暗号強度から VPN 使用の推奨度を判定する。
    /// </summary>
    /// <param name="network">対象ネットワーク</param>
    /// <param name="isKnownTrustedNetwork">
    /// 過去に MWC 経由で正常に接続した実績がある既知のネットワークかどうか
    /// (例: <see cref="NetworkHistoryService.GetEntry"/> が非 null かつ成功履歴あり)。
    /// </param>
    public VpnAdvisory Analyze(WifiNetwork network, bool isKnownTrustedNetwork)
    {
        // 1. 暗号化なし — 既知/未知を問わず常に強く推奨
        if (network.Auth is AuthMethod.Open)
            return new VpnAdvisory(
                VpnRecommendation.StronglyRecommended,
                "Unencrypted network — anyone nearby can see your traffic. Use a VPN.");

        // 2. 未知のネットワーク — 暗号化されていても AP 運営者や Evil Twin の懸念が残る
        if (!isKnownTrustedNetwork)
            return new VpnAdvisory(
                VpnRecommendation.Recommended,
                "Unfamiliar network. Even though it is encrypted, the access point operator " +
                "(or an impersonating Evil Twin) can still see your traffic. A VPN adds protection.");

        bool isEnterprise = network.Auth is AuthMethod.WPA2Enterprise
                                          or AuthMethod.WPA3Enterprise
                                          or AuthMethod.WPA3Enterprise192;

        // 3. 既知 + Enterprise — 通常は組織の VPN/ファイアウォールを経由済み
        if (isEnterprise)
            return new VpnAdvisory(
                VpnRecommendation.NotNeeded,
                "Known enterprise network — traffic typically already routes through your " +
                "organisation's firewall/VPN. A personal VPN may be redundant here.");

        bool strongPersonal = network.Auth == AuthMethod.WPA3SAE && !network.IsWpa3TransitionMode;

        // 4. 既知 + WPA3-SAE(非 transition)— 強固な暗号化のため任意
        if (strongPersonal)
            return new VpnAdvisory(
                VpnRecommendation.Optional,
                "Known trusted network with strong (WPA3-SAE) encryption. VPN is optional here.");

        // 5. 既知だが暗号化が相対的に弱い(WPA2/WPA/WEP または WPA3 transition mode)
        return new VpnAdvisory(
            VpnRecommendation.Recommended,
            "Known network, but its encryption is weaker (WPA2 or below, or WPA3 transition " +
            "mode which is vulnerable to downgrade attacks). A VPN still adds protection.");
    }
}

// ── データ型 ─────────────────────────────────────────────────────────

/// <summary>VPN 使用推奨度。</summary>
public enum VpnRecommendation
{
    /// <summary>不要 — 既に十分保護されている(例: 既知 Enterprise ネットワーク)</summary>
    NotNeeded,
    /// <summary>任意 — 既知の信頼できるネットワークで強固な暗号化</summary>
    Optional,
    /// <summary>推奨 — 未知のネットワーク、または暗号化が弱い既知ネットワーク</summary>
    Recommended,
    /// <summary>強く推奨 — 暗号化なし(内容が誰にでも見える)</summary>
    StronglyRecommended
}

/// <summary>VPN 使用可否の助言。</summary>
public sealed record VpnAdvisory(
    VpnRecommendation Recommendation,
    string            Reason);
