using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MurtiWifiConnecter.Infrastructure;

namespace MurtiWifiConnecter.Infrastructure.ExceptionHandling
{
    /// <summary>
    /// 例外ハンドラーインターフェース
    /// </summary>
    public interface IExceptionHandler
    {
        bool CanHandle(Exception exception);
        Task<ExceptionHandlingResult> HandleAsync(Exception exception, ExceptionContext context);
        int Priority { get; }
    }

    /// <summary>
    /// 例外処理結果
    /// </summary>
    public class ExceptionHandlingResult
    {
        public bool Handled { get; set; }
        public bool ShouldContinue { get; set; } = true;
        public string Message { get; set; }
        public Exception ProcessedException { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
    }

    /// <summary>
    /// 例外コンテキスト
    /// </summary>
    public class ExceptionContext
    {
        public string OperationName { get; set; }
        public string ComponentName { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string UserId { get; set; }
        public string SessionId { get; set; }
    }

    /// <summary>
    /// WiFi操作例外ハンドラー
    /// </summary>
    public class WifiExceptionHandler : IExceptionHandler
    {
        private readonly INotificationService _notificationService;
        private readonly ITelemetryService _telemetryService;

        public int Priority => 100;

        public WifiExceptionHandler(INotificationService notificationService, ITelemetryService telemetryService)
        {
            _notificationService = notificationService;
            _telemetryService = telemetryService;
        }

        public bool CanHandle(Exception exception)
        {
            return exception is WifiOperationException ||
                   exception is System.Net.NetworkInformation.NetworkInformationException ||
                   exception is UnauthorizedAccessException;
        }

        public async Task<ExceptionHandlingResult> HandleAsync(Exception exception, ExceptionContext context)
        {
            var result = new ExceptionHandlingResult();

            switch (exception)
            {
                case WifiOperationException wifiEx:
                    result = await HandleWifiExceptionAsync(wifiEx, context);
                    break;

                case System.Net.NetworkInformation.NetworkInformationException netEx:
                    result = await HandleNetworkExceptionAsync(netEx, context);
                    break;

                case UnauthorizedAccessException authEx:
                    result = await HandleAuthorizationExceptionAsync(authEx, context);
                    break;

                default:
                    result.Handled = false;
                    break;
            }

            if (result.Handled)
            {
                _telemetryService.TrackException(exception, new Dictionary<string, string>
                {
                    { "Handler", nameof(WifiExceptionHandler) },
                    { "Operation", context.OperationName },
                    { "Component", context.ComponentName }
                });
            }

            return result;
        }

        private async Task<ExceptionHandlingResult> HandleWifiExceptionAsync(WifiOperationException exception, ExceptionContext context)
        {
            var userMessage = exception.ErrorCode switch
            {
                WifiErrorCode.AdapterNotFound => "WiFiアダプターが見つかりません。デバイスを確認してください。",
                WifiErrorCode.NetworkNotFound => "指定されたネットワークが見つかりません。",
                WifiErrorCode.AuthenticationFailed => "認証に失敗しました。パスワードを確認してください。",
                WifiErrorCode.ConnectionTimeout => "接続がタイムアウトしました。再試行してください。",
                WifiErrorCode.SignalTooWeak => "信号が弱すぎます。アクセスポイントに近づいてください。",
                _ => "WiFi操作でエラーが発生しました。"
            };

            _notificationService.ShowError(userMessage, "WiFiエラー");

            return new ExceptionHandlingResult
            {
                Handled = true,
                ShouldContinue = exception.ErrorCode != WifiErrorCode.AdapterNotFound,
                Message = userMessage,
                Data = new Dictionary<string, object>
                {
                    { "ErrorCode", exception.ErrorCode },
                    { "SSID", exception.SSID }
                }
            };
        }

        private async Task<ExceptionHandlingResult> HandleNetworkExceptionAsync(System.Net.NetworkInformation.NetworkInformationException exception, ExceptionContext context)
        {
            var userMessage = "ネットワーク操作でエラーが発生しました。ネットワーク接続を確認してください。";
            _notificationService.ShowWarning(userMessage, "ネットワークエラー");

            return new ExceptionHandlingResult
            {
                Handled = true,
                ShouldContinue = true,
                Message = userMessage
            };
        }

        private async Task<ExceptionHandlingResult> HandleAuthorizationExceptionAsync(UnauthorizedAccessException exception, ExceptionContext context)
        {
            var userMessage = "この操作を実行する権限がありません。管理者として実行してください。";
            _notificationService.ShowError(userMessage, "権限エラー");

            return new ExceptionHandlingResult
            {
                Handled = true,
                ShouldContinue = false,
                Message = userMessage
            };
        }
    }

    /// <summary>
    /// 一般的な例外ハンドラー
    /// </summary>
    public class GeneralExceptionHandler : IExceptionHandler
    {
        private readonly INotificationService _notificationService;
        private readonly ITelemetryService _telemetryService;

        public int Priority => 10; // 低優先度

        public GeneralExceptionHandler(INotificationService notificationService, ITelemetryService telemetryService)
        {
            _notificationService = notificationService;
            _telemetryService = telemetryService;
        }

        public bool CanHandle(Exception exception)
        {
            return true; // すべての例外を処理可能
        }

        public async Task<ExceptionHandlingResult> HandleAsync(Exception exception, ExceptionContext context)
        {
            var severity = DetermineSeverity(exception);
            var userMessage = GetUserFriendlyMessage(exception);

            switch (severity)
            {
                case ExceptionSeverity.Critical:
                    _notificationService.ShowError(userMessage, "重大なエラー");
                    break;
                case ExceptionSeverity.High:
                    _notificationService.ShowError(userMessage, "エラー");
                    break;
                case ExceptionSeverity.Medium:
                    _notificationService.ShowWarning(userMessage, "警告");
                    break;
                case ExceptionSeverity.Low:
                    _notificationService.ShowInfo(userMessage, "情報");
                    break;
            }

            _telemetryService.TrackException(exception, new Dictionary<string, string>
            {
                { "Handler", nameof(GeneralExceptionHandler) },
                { "Severity", severity.ToString() },
                { "Operation", context.OperationName }
            });

            return new ExceptionHandlingResult
            {
                Handled = true,
                ShouldContinue = severity != ExceptionSeverity.Critical,
                Message = userMessage,
                Data = new Dictionary<string, object>
                {
                    { "Severity", severity },
                    { "ExceptionType", exception.GetType().Name }
                }
            };
        }

        private ExceptionSeverity DetermineSeverity(Exception exception)
        {
            return exception switch
            {
                OutOfMemoryException => ExceptionSeverity.Critical,
                StackOverflowException => ExceptionSeverity.Critical,
                System.Security.SecurityException => ExceptionSeverity.High,
                UnauthorizedAccessException => ExceptionSeverity.High,
                ArgumentException => ExceptionSeverity.Medium,
                InvalidOperationException => ExceptionSeverity.Medium,
                NotImplementedException => ExceptionSeverity.Low,
                _ => ExceptionSeverity.Medium
            };
        }

        private string GetUserFriendlyMessage(Exception exception)
        {
            return exception switch
            {
                OutOfMemoryException => "メモリが不足しています。アプリケーションを再起動してください。",
                UnauthorizedAccessException => "この操作を実行する権限がありません。",
                ArgumentException => "入力された値が正しくありません。",
                InvalidOperationException => "現在この操作は実行できません。",
                NotImplementedException => "この機能はまだ実装されていません。",
                System.IO.FileNotFoundException => "必要なファイルが見つかりません。",
                System.IO.DirectoryNotFoundException => "指定されたフォルダが見つかりません。",
                TimeoutException => "操作がタイムアウトしました。再試行してください。",
                _ => "予期しないエラーが発生しました。"
            };
        }
    }

    /// <summary>
    /// ファイル操作例外ハンドラー
    /// </summary>
    public class FileOperationExceptionHandler : IExceptionHandler
    {
        private readonly INotificationService _notificationService;

        public int Priority => 80;

        public FileOperationExceptionHandler(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public bool CanHandle(Exception exception)
        {
            return exception is System.IO.IOException ||
                   exception is System.IO.FileNotFoundException ||
                   exception is System.IO.DirectoryNotFoundException ||
                   exception is UnauthorizedAccessException;
        }

        public async Task<ExceptionHandlingResult> HandleAsync(Exception exception, ExceptionContext context)
        {
            var (userMessage, shouldContinue) = exception switch
            {
                System.IO.FileNotFoundException => ("必要なファイルが見つかりません。ファイルの場所を確認してください。", true),
                System.IO.DirectoryNotFoundException => ("指定されたフォルダが見つかりません。", true),
                UnauthorizedAccessException => ("ファイルにアクセスする権限がありません。", false),
                System.IO.IOException ioEx when ioEx.Message.Contains("being used") => ("ファイルが他のプロセスで使用中です。", true),
                System.IO.IOException => ("ファイル操作でエラーが発生しました。", true),
                _ => ("ファイル操作でエラーが発生しました。", true)
            };

            _notificationService.ShowError(userMessage, "ファイルエラー");

            return new ExceptionHandlingResult
            {
                Handled = true,
                ShouldContinue = shouldContinue,
                Message = userMessage,
                Data = new Dictionary<string, object>
                {
                    { "ExceptionType", exception.GetType().Name },
                    { "FilePath", ExtractFilePathFromException(exception) }
                }
            };
        }

        private string ExtractFilePathFromException(Exception exception)
        {
            return exception switch
            {
                System.IO.FileNotFoundException fileEx => fileEx.FileName ?? "不明",
                System.IO.DirectoryNotFoundException dirEx => ExtractPathFromMessage(dirEx.Message),
                System.IO.IOException ioEx => ExtractPathFromMessage(ioEx.Message),
                _ => "不明"
            };
        }

        private string ExtractPathFromMessage(string message)
        {
            // メッセージからファイルパスを抽出する簡単な実装
            var startIndex = message.IndexOf("'");
            if (startIndex >= 0)
            {
                var endIndex = message.IndexOf("'", startIndex + 1);
                if (endIndex > startIndex)
                {
                    return message.Substring(startIndex + 1, endIndex - startIndex - 1);
                }
            }
            return "不明";
        }
    }

    /// <summary>
    /// 例外重要度
    /// </summary>
    public enum ExceptionSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// 復旧可能例外ハンドラー
    /// </summary>
    public class RecoverableExceptionHandler : IExceptionHandler
    {
        private readonly INotificationService _notificationService;
        private readonly Dictionary<string, int> _retryCounters;
        private readonly int _maxRetries;

        public int Priority => 90;

        public RecoverableExceptionHandler(INotificationService notificationService, int maxRetries = 3)
        {
            _notificationService = notificationService;
            _retryCounters = new Dictionary<string, int>();
            _maxRetries = maxRetries;
        }

        public bool CanHandle(Exception exception)
        {
            return exception is TimeoutException ||
                   exception is System.Net.WebException ||
                   exception is TaskCanceledException;
        }

        public async Task<ExceptionHandlingResult> HandleAsync(Exception exception, ExceptionContext context)
        {
            var operationKey = $"{context.ComponentName}_{context.OperationName}";
            var currentRetryCount = _retryCounters.GetValueOrDefault(operationKey, 0);

            if (currentRetryCount < _maxRetries)
            {
                _retryCounters[operationKey] = currentRetryCount + 1;
                
                var userMessage = $"操作が失敗しました。自動的に再試行します。(試行回数: {currentRetryCount + 1}/{_maxRetries})";
                _notificationService.ShowInfo(userMessage, "再試行中");

                return new ExceptionHandlingResult
                {
                    Handled = true,
                    ShouldContinue = true,
                    Message = userMessage,
                    Data = new Dictionary<string, object>
                    {
                        { "RetryCount", currentRetryCount + 1 },
                        { "MaxRetries", _maxRetries },
                        { "ShouldRetry", true }
                    }
                };
            }
            else
            {
                // 最大再試行回数に達した
                _retryCounters.Remove(operationKey);
                
                var userMessage = "操作が複数回失敗しました。しばらく時間をおいてから再試行してください。";
                _notificationService.ShowError(userMessage, "操作失敗");

                return new ExceptionHandlingResult
                {
                    Handled = true,
                    ShouldContinue = false,
                    Message = userMessage,
                    Data = new Dictionary<string, object>
                    {
                        { "RetryCount", currentRetryCount },
                        { "MaxRetries", _maxRetries },
                        { "ShouldRetry", false }
                    }
                };
            }
        }

        public void ResetRetryCounter(string componentName, string operationName)
        {
            var operationKey = $"{componentName}_{operationName}";
            _retryCounters.Remove(operationKey);
        }
    }
}