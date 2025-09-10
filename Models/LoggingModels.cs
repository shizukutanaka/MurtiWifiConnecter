using System;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// ログレベル
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    /// <summary>
    /// ログエントリ
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// ログイベント引数
    /// </summary>
    public class LogEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public Exception Exception { get; set; }
    }
}