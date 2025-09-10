using System;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 接続再試行管理
    /// </summary>
    public class ConnectionRetryManager
    {
        private readonly ConnectionLogger _connectionLogger;
        private readonly int _maxRetries;
        private readonly int _retryDelayMs;
        private readonly int _maxRetryDelayMs;
        
        public ConnectionRetryManager(ConnectionLogger connectionLogger, int maxRetries = 3, int initialDelayMs = 1000)
        {
            _connectionLogger = connectionLogger;
            _maxRetries = maxRetries;
            _retryDelayMs = initialDelayMs;
            _maxRetryDelayMs = initialDelayMs * 8; // 最大8秒待機
        }

        /// <summary>
        /// 接続を再試行
        /// </summary>
        public async Task<WifiConnectionResult> ConnectWithRetryAsync(
            string ssid, 
            string password, 
            CancellationToken cancellationToken = default)
        {
            var lastResult = new WifiConnectionResult { Success = false };
            int currentDelay = _retryDelayMs;
            
            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    lastResult.ErrorMessage = "Connection cancelled";
                    break;
                }

                _connectionLogger?.Log(ConnectionLogger.LogLevel.Info, "Retry", 
                    $"Connection attempt {attempt}/{_maxRetries} for {ssid}");

                try
                {
                    lastResult = await FastWifiConnector.ConnectAsync(ssid, password, cancellationToken).ConfigureAwait(false);
                    
                    if (lastResult.Success)
                    {
                        _connectionLogger?.Log(ConnectionLogger.LogLevel.Info, "Retry", 
                            $"Successfully connected to {ssid} on attempt {attempt}");
                        return lastResult;
                    }

                    // 特定のエラーの場合は再試行しない
                    if (IsNonRetryableError(lastResult.ErrorMessage))
                    {
                        _connectionLogger?.Log(ConnectionLogger.LogLevel.Warning, "Retry", 
                            $"Non-retryable error for {ssid}: {lastResult.ErrorMessage}");
                        break;
                    }

                    // 最後の試行でなければ待機
                    if (attempt < _maxRetries)
                    {
                        _connectionLogger?.Log(ConnectionLogger.LogLevel.Info, "Retry", 
                            $"Waiting {currentDelay}ms before retry {attempt + 1}");
                        
                        await Task.Delay(currentDelay, cancellationToken).ConfigureAwait(false);
                        
                        // 指数バックオフ（2倍ずつ増加、最大値まで）
                        currentDelay = Math.Min(currentDelay * 2, _maxRetryDelayMs);
                    }
                }
                catch (OperationCanceledException)
                {
                    lastResult.ErrorMessage = "Connection cancelled";
                    break;
                }
                catch (Exception ex)
                {
                    lastResult.ErrorMessage = ex.Message;
                    ErrorHandler.LogError($"ConnectionRetryManager.Attempt{attempt}", ex, _connectionLogger);
                    
                    if (attempt == _maxRetries)
                    {
                        break;
                    }
                }
            }

            _connectionLogger?.Log(ConnectionLogger.LogLevel.Warning, "Retry", 
                $"Failed to connect to {ssid} after {_maxRetries} attempts");
            
            return lastResult;
        }

        /// <summary>
        /// 切断後の自動再接続
        /// </summary>
        public async Task<bool> ReconnectAfterDisconnectionAsync(
            string lastSSID,
            string password,
            int maxWaitSeconds = 30,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(lastSSID))
                return false;

            _connectionLogger?.Log(ConnectionLogger.LogLevel.Info, "Reconnect", 
                $"Attempting to reconnect to {lastSSID}");

            var endTime = DateTime.Now.AddSeconds(maxWaitSeconds);
            int delayMs = 2000; // 初期待機時間2秒

            while (DateTime.Now < endTime && !cancellationToken.IsCancellationRequested)
            {
                // 現在の接続状態を確認
                var currentSSID = await FastWifiConnector.GetCurrentConnectedSSIDAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(currentSSID))
                {
                    if (string.Equals(currentSSID, lastSSID, StringComparison.OrdinalIgnoreCase))
                    {
                        _connectionLogger?.Log(ConnectionLogger.LogLevel.Info, "Reconnect", 
                            $"Successfully reconnected to {lastSSID}");
                        return true;
                    }
                }

                // 再接続試行
                var result = await FastWifiConnector.ConnectAsync(lastSSID, password, cancellationToken).ConfigureAwait(false);
                if (result.Success)
                {
                    _connectionLogger?.Log(ConnectionLogger.LogLevel.Info, "Reconnect", 
                        $"Reconnected to {lastSSID}");
                    return true;
                }

                // 待機
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                
                // 待機時間を増加（最大10秒）
                delayMs = Math.Min(delayMs + 1000, 10000);
            }

            _connectionLogger?.Log(ConnectionLogger.LogLevel.Warning, "Reconnect", 
                $"Failed to reconnect to {lastSSID} within {maxWaitSeconds} seconds");
            
            return false;
        }

        private bool IsNonRetryableError(string? errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
                return false;

            // パスワードエラーなど、再試行しても意味がないエラー
            var nonRetryableKeywords = new[]
            {
                "password",
                "authentication",
                "incorrect key",
                "access denied",
                "not found",
                "invalid"
            };

            var lowerError = errorMessage.ToLowerInvariant();
            foreach (var keyword in nonRetryableKeywords)
            {
                if (lowerError.Contains(keyword))
                    return true;
            }

            return false;
        }
    }
}