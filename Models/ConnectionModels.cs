using System;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 接続統計サマリー
    /// </summary>
    public class ConnectionStatisticsSummary
    {
        public int TotalConnectionAttempts { get; set; }
        public int SuccessfulConnections { get; set; }
        public int FailedConnections { get; set; }
        public double SuccessRate { get; set; }
        public int TotalScans { get; set; }
        public double AverageScanTime { get; set; }
        public DateTime? LastConnectionAttempt { get; set; }
        public DateTime? LastScan { get; set; }
    }


    /// <summary>
    /// ネットワークセキュリティ評価
    /// </summary>
    public class NetworkSecurityAssessment
    {
        public string SSID { get; set; }
        public SecurityLevel Level { get; set; }
        public string Authentication { get; set; }
        public string Encryption { get; set; }
        public bool IsOpen { get; set; }
        public bool UsesWEP { get; set; }
        public bool UsesWPA { get; set; }
        public bool UsesWPA2 { get; set; }
        public bool UsesWPA3 { get; set; }
        public string Recommendation { get; set; }
    }


    /// <summary>
    /// 保存されたプロファイル
    /// </summary>
    public class SavedProfile
    {
        public string SSID { get; set; }
        public string EncryptedPassword { get; set; }
        public DateTime LastConnected { get; set; }
        public bool AutoConnect { get; set; }
        public int Priority { get; set; }
    }
}