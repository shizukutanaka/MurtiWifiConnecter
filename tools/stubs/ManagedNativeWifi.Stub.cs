// ─────────────────────────────────────────────────────────────────────────────
// tools/stubs/ManagedNativeWifi.Stub.cs — 型検査専用。ManagedNativeWifi 3.0.2 の
// 実ソース (github.com/emoacht/ManagedNativeWifi, HEAD の
// Source/ManagedNativeWifi/ManagedNativeWifi.csproj が <Version>3.0.2</Version> — NuGet
// にピン留めされたバージョンと一致することを確認済み。2026-09 に `git clone` して直接
// 突き合わせた)から**そのまま書き写した**シグネチャのみを含む。
// `tools/typecheck-platform.sh` が `WindowsWifiService.cs` を検査する際に使う。
//
// 循環しない根拠: このスタブの型・メンバー名は「検査対象のコードがどう呼んでいるか」
// からの逆算ではなく、クローンした実ソースファイルを直接引き写した。実装本体は空(型検査
// のみが目的、実行はしない)。
//
// 収録は WindowsWifiService.cs が実際に参照するメンバーのみ(全体の移植ではない)。
// このファイルを広げる/削る場合は、必ず実ソースを再クローンして裏取りすること
// (検査対象のコードから逆算しない)。
//
// 2026-09 のこの検査導入で判明した実欠陥(WindowsWifiService.cs 側で修正済み):
//   - `AuthAlgorithm` という型は存在しない(実際は `AuthenticationAlgorithm`)。
//     メンバー名も `RsnaPsk` ではなく `RSNA_PSK` (ネイティブ DOT11 定数由来の表記)。
//   - `CipherAlgorithm` の型名は合っていたが、メンバー名が `Ccmp`/`Wep`/`Tkip`/`Gcmp256`
//     ではなく `CCMP`/`WEP`/`TKIP`/`GCMP_256`(enum メンバーは大文字小文字を区別する)。
//   - `PhyType.B/.A/.G/.N/.Ac/.Ax/.Be` は存在しない
//     (実際は Ofdm/HrDsss/Erp/Ht/Vht/He/Eht — ManagedNativeWifi 自身の
//     `PhyTypeExtension.ToProtocolName()` が同じ対応表を公開している)。
//   - `BssNetworkInfo` に `Bandwidth` は無く、`ChannelBandwidth` という型自体が
//     実ソースのどこにも無い(チャネル幅はこのバージョンでは一切取得不可)。
//   - `BssNetworkInfo.Band` は float(GHz 帯域を示すだけ)で Nullable ではなく、
//     周波数(KHz)を表すのは別メンバー `Frequency`。
//   - `NetworkStateChangedEventArgs`(自製型)に `Ssid` は無い。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedNativeWifi
{
    public enum ActionResult { None = 0, Success, NotConnected, NotFound, NotSupported, OtherError }

    public enum InterfaceState
    {
        NotReady = 0, Connected, AdHocNetworkFormed, Disconnecting,
        Disconnected, Associating, Discovering, Authenticating
    }

    public enum BssType { None = 0, Infrastructure, Independent, Any }

    public enum ProfileType { AllUser = 0, GroupPolicy, PerUser }

    public enum ConnectionMode { Unknown = 0, }

    public enum AuthenticationAlgorithm
    {
        Unknown = 0, Open, Shared, WPA, WPA_PSK, WPA_NONE, RSNA, RSNA_PSK,
        WPA3_ENT_192, WPA3_ENT, WPA3_SAE, OWE, IHV_START, IHV_END
    }

    public enum CipherAlgorithm
    {
        None = 0, WEP, WEP_40, WEP_104, TKIP, CCMP, BIP, GCMP, GCMP_256,
        CCMP_256, BIP_GMAC_128, BIP_GMAC_256, BIP_CMAC_256, WPA_USE_GROUP,
        RSN_USE_GROUP, IHV_START, IHV_END
    }

    public enum PhyType
    {
        Unknown = 0, Any, Fhss, Dsss, IrBaseband, Ofdm, HrDsss, Erp, Ht,
        Vht, Dmg, He, Eht, IhvStart, IhvEnd
    }

    public class NetworkIdentifier
    {
        public NetworkIdentifier(string rawString) { }
        public NetworkIdentifier(byte[] rawBytes) { }
        public byte[] ToBytes() => throw new NotSupportedException();
        public override string ToString() => throw new NotSupportedException();
    }

    public class InterfaceInfo
    {
        public Guid Id { get; }
        public string Description { get; } = null!;
        public InterfaceState State { get; }
        public InterfaceInfo(Guid id, string description, InterfaceState state) { }
    }

    public class InterfaceConnectionInfo : InterfaceInfo
    {
        public bool IsRadioOn { get; }
        public bool IsConnected { get; }
        public InterfaceConnectionInfo(Guid id, string description, InterfaceState state) : base(id, description, state) { }
    }

    public class AvailableNetworkInfo
    {
        public NetworkIdentifier Ssid { get; } = null!;
        public BssType BssType { get; }
        public bool IsConnectable { get; }
        public int SignalQuality { get; }
        public bool IsSecurityEnabled { get; }
        public string ProfileName { get; } = null!;
        public AuthenticationAlgorithm AuthenticationAlgorithm { get; }
        public CipherAlgorithm CipherAlgorithm { get; }
    }

    public class AvailableNetworkPack : AvailableNetworkInfo
    {
        public InterfaceInfo InterfaceInfo { get; } = null!;
        [Obsolete("Use InterfaceInfo property instead.")]
        public InterfaceInfo Interface => InterfaceInfo;
    }

    public class BssNetworkInfo
    {
        public NetworkIdentifier Ssid { get; } = null!;
        public BssType BssType { get; }
        public NetworkIdentifier Bssid { get; } = null!;
        public PhyType PhyType { get; }
        public int Rssi { get; }
        public int LinkQuality { get; }
        public int Frequency { get; }
        public float Band { get; }
        public int Channel { get; }
    }

    public class BssNetworkPack : BssNetworkInfo
    {
        public InterfaceInfo InterfaceInfo { get; } = null!;
        [Obsolete("Use InterfaceInfo property instead.")]
        public InterfaceInfo Interface => InterfaceInfo;
    }

    public class ProfilePack
    {
        public InterfaceInfo InterfaceInfo { get; } = null!;
        [Obsolete("Use InterfaceInfo property instead.")]
        public InterfaceInfo Interface => InterfaceInfo;
        public string Name { get; } = null!;
        public ProfileType ProfileType { get; }
    }

    public class CurrentConnectionInfo
    {
        public InterfaceState InterfaceState { get; }
        public ConnectionMode ConnectionMode { get; }
        public string ProfileName { get; } = null!;
        public NetworkIdentifier Ssid { get; } = null!;
        public BssType BssType { get; }
        public NetworkIdentifier Bssid { get; } = null!;
        public PhyType PhyType { get; }
        public uint PhyIndex { get; }
        public int SignalQuality { get; }
        public int RxRate { get; }
        public int TxRate { get; }
        public bool IsSecurityEnabled { get; }
        public bool IsOneXEnabled { get; }
        public AuthenticationAlgorithm AuthenticationAlgorithm { get; }
        public CipherAlgorithm CipherAlgorithm { get; }
    }

    public static class NativeWifi
    {
        public static IEnumerable<InterfaceInfo> EnumerateInterfaces() => throw new NotSupportedException();
        public static Task<IEnumerable<Guid>> ScanNetworksAsync(TimeSpan timeout, CancellationToken cancellationToken) => throw new NotSupportedException();
        public static Task<IEnumerable<Guid>> ScanNetworksAsync(Guid interfaceId, TimeSpan timeout, CancellationToken cancellationToken) => throw new NotSupportedException();
        public static IEnumerable<AvailableNetworkPack> EnumerateAvailableNetworks() => throw new NotSupportedException();
        public static IEnumerable<BssNetworkPack> EnumerateBssNetworks() => throw new NotSupportedException();
        public static (ActionResult result, CurrentConnectionInfo value) GetCurrentConnection(Guid interfaceId) => throw new NotSupportedException();
        public static bool SetProfile(Guid interfaceId, ProfileType profileType, string profileXml, string profileSecurity, bool overwrite) => throw new NotSupportedException();
        public static bool ConnectNetwork(Guid interfaceId, string profileName, BssType bssType) => throw new NotSupportedException();
        public static bool DisconnectNetwork(Guid interfaceId) => throw new NotSupportedException();
        public static bool DeleteProfile(Guid interfaceId, string profileName) => throw new NotSupportedException();
        public static IEnumerable<ProfilePack> EnumerateProfiles() => throw new NotSupportedException();
        // ⚠ 意図的に NetworkStateChanged / EnumerateConnectedNetworks / ChannelBandwidth は
        // 定義しない — 実ソースに存在しないため。定義すると検査が意味を失う。
    }
}
