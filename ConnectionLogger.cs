using System;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 軽量接続ログクラス - 削除されたConnectionLoggerの代替
    /// </summary>
    public class ConnectionLogger : IDisposable
    {
        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        private readonly Services.SimpleLoggingService _loggingService;

        public ConnectionLogger()
        {
            _loggingService = new Services.SimpleLoggingService();
        }

        public void Log(LogLevel level, string category, string message)
        {
            switch (level)
            {
                case LogLevel.Debug:
                    _loggingService.LogDebug($"[{category}] {message}");
                    break;
                case LogLevel.Info:
                    _loggingService.LogInfo($"[{category}] {message}");
                    break;
                case LogLevel.Warning:
                    _loggingService.LogWarning($"[{category}] {message}");
                    break;
                case LogLevel.Error:
                    _loggingService.LogError($"[{category}] {message}");
                    break;
            }
        }

        public void LogConnection(string ssid, bool success, int signalStrength, string? errorMessage = null)
        {
            var status = success ? "成功" : "失敗";
            var message = $"接続: {ssid} - {status} (信号強度: {signalStrength}%)";
            
            if (!success && !string.IsNullOrEmpty(errorMessage))
                message += $" エラー: {errorMessage}";

            Log(success ? LogLevel.Info : LogLevel.Warning, "Connection", message);
        }

        public void Dispose()
        {
            _loggingService?.Dispose();
        }
    }
}