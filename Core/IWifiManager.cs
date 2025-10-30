using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// WiFi操作の抽象インターフェース - クロスプラットフォーム対応
    /// </summary>
    public interface IWifiManager
    {
        /// <summary>
        /// ネットワークに接続する
        /// </summary>
        Task<bool> ConnectAsync(string ssid, string password, CancellationToken ct = default);

        /// <summary>
        /// 現在のネットワークから切断する
        /// </summary>
        Task<bool> DisconnectAsync(CancellationToken ct = default);

        /// <summary>
        /// 利用可能なネットワークをスキャンする
        /// </summary>
        Task<List<WifiNetwork>> ScanNetworksAsync(CancellationToken ct = default);

        /// <summary>
        /// 現在の接続先SSIDを取得する
        /// </summary>
        Task<string?> GetCurrentSSIDAsync(CancellationToken ct = default);

        /// <summary>
        /// 保存済みプロファイルを取得する
        /// </summary>
        Task<List<string>> GetSavedProfilesAsync(CancellationToken ct = default);

        /// <summary>
        /// プロファイルを削除する
        /// </summary>
        Task<bool> DeleteProfileAsync(string ssid, CancellationToken ct = default);

        /// <summary>
        /// 利用可能なアダプタ情報を取得する
        /// </summary>
        Task<Result<IReadOnlyList<WifiAdapterInfo>>> GetAvailableAdaptersAsync(CancellationToken ct = default);

        /// <summary>
        /// 優先アダプタを設定する
        /// </summary>
        void SetPreferredAdapter(string? adapterName);

        /// <summary>
        /// 優先アダプタを取得する
        /// </summary>
        string? GetPreferredAdapter();
    }

    /// <summary>
    /// WiFiネットワーク情報を表すクラス
    /// </summary>
    public class WifiNetwork
    {
        public string Ssid { get; set; } = "";
        public string Bssid { get; set; } = "";
        public int SignalStrength { get; set; }
        public WifiSecurityMode SecurityMode { get; set; }
        public WifiFrequencyBand FrequencyBand { get; set; }
        public bool IsConnected { get; set; }
        public string? AuthenticationAlgorithm { get; set; }
        public string? EncryptionAlgorithm { get; set; }
    }

    /// <summary>
    /// WiFiセキュリティモード
    /// </summary>
    public enum WifiSecurityMode
    {
        Open,
        Wep,
        Wpa,
        Wpa2,
        Wpa3,
        Wpa2Enterprise,
        Wpa3Enterprise
    }

    /// <summary>
    /// WiFi周波数帯
    /// </summary>
    public enum WifiFrequencyBand
    {
        Unknown,
        Band2_4GHz,
        Band5GHz,
        Band6GHz
    }

    /// <summary>
    /// WiFiアダプタ情報を表すクラス
    /// </summary>
    public class WifiAdapterInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsConnected { get; set; }
        public NetworkInterfaceType InterfaceType { get; set; }
        public string? PhysicalAddress { get; set; }
        public long Speed { get; set; }
    }

    /// <summary>
    /// ネットワークインターフェイスタイプ
    /// </summary>
    public enum NetworkInterfaceType
    {
        Unknown,
        Ethernet,
        Wireless80211,
        TokenRing,
        Fddi,
        BasicIsdn,
        PrimaryIsdn,
        Ppp,
        Loopback,
        Slip,
        Atm,
        GenericModem,
        FastEthernetFx,
        Isdn,
        FastEthernetT,
        Tunnel,
        Ieee80211,
        Ieee1394
    }
}
