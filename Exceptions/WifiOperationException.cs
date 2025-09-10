using System;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// WiFi操作専用のカスタム例外 - より具体的なエラーハンドリング
    /// </summary>
    public class WifiOperationException : Exception
    {
        public WifiOperationException(string message) : base(message) { }
        public WifiOperationException(string message, Exception innerException) : base(message, innerException) { }
    }
}