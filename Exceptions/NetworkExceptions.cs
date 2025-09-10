using System;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// ネットワーク操作の種類
    /// </summary>
    public enum NetworkOperationType
    {
        Scan,
        Connect,
        Disconnect,
        Other
    }

    /// <summary>
    /// ネットワーク操作例外
    /// </summary>
    public class NetworkOperationException : Exception
    {
        public NetworkOperationType OperationType { get; }
        public string NetworkSSID { get; }

        public NetworkOperationException(string message, NetworkOperationType operationType = NetworkOperationType.Other, string ssid = null) 
            : base(message)
        {
            OperationType = operationType;
            NetworkSSID = ssid;
        }

        public NetworkOperationException(string message, Exception innerException, NetworkOperationType operationType = NetworkOperationType.Other, string ssid = null) 
            : base(message, innerException)
        {
            OperationType = operationType;
            NetworkSSID = ssid;
        }
    }

    /// <summary>
    /// リソース例外
    /// </summary>
    public class ResourceException : Exception
    {
        public ResourceException(string message) : base(message) { }
        public ResourceException(string message, Exception innerException) : base(message, innerException) { }
    }
}