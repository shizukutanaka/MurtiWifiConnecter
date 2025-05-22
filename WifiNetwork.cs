using System;

namespace MurtiWifiConnecter
{
    public class WifiNetwork : IEquatable<WifiNetwork>
    {
        public string SSID { get; set; }
        public int SignalStrength { get; set; } // 0-100
        public bool IsConnected { get; set; }

        public override int GetHashCode() => SSID?.GetHashCode() ?? 0;
        public override bool Equals(object obj) => Equals(obj as WifiNetwork);
        public bool Equals(WifiNetwork other) => other != null && SSID == other.SSID;
    }
}
