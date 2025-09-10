using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Infrastructure.ExceptionHandling
{
    /// <summary>
    /// 統合例外処理システム - Chain of Responsibility + Strategy Pattern
    /// </summary>
    public class ExceptionProcessor
    {
        private readonly List<IExceptionHandler> _handlers = new();
        private readonly ConcurrentDictionary<Type, ExceptionMetrics> _metrics = new();
        private readonly ILoggingService? _loggingService;

        public ExceptionProcessor(ILoggingService? loggingService = null)
        {
            _loggingService = loggingService;
            RegisterDefaultHandlers();
        }

        /// <summary>
        /// 例外処理ハンドラーの登録
        /// </summary>
        public void RegisterHandler(IExceptionHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handlers.Add(handler);
        }

        /// <summary>
        /// 例外の統合処理
        /// </summary>
        public async Task<ExceptionProcessingResult> ProcessExceptionAsync(Exception exception, string context = "")
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));

            var processingContext = new ExceptionProcessingContext
            {
                Exception = exception,
                Context = context,
                Timestamp = DateTime.UtcNow,
                Severity = DetermineSeverity(exception),
                ProcessingId = Guid.NewGuid()
            };

            // メトリクス更新
            UpdateMetrics(exception);

            var result = new ExceptionProcessingResult
            {
                ProcessingId = processingContext.ProcessingId,
                OriginalException = exception,
                WasHandled = false,
                RecoveryActions = new List<RecoveryAction>(),
                SuggestedUserAction = "操作を再試行してください。"
            };

            // Chain of Responsibility パターンで例外処理
            foreach (var handler in _handlers.OrderBy(h => h.Priority))
            {
                if (await handler.CanHandleAsync(processingContext))
                {
                    try
                    {
                        var handlerResult = await handler.HandleAsync(processingContext);
                        
                        result.WasHandled = handlerResult.WasHandled;
                        result.RecoveryActions.AddRange(handlerResult.RecoveryActions);
                        
                        if (!string.IsNullOrEmpty(handlerResult.SuggestedUserAction))
                            result.SuggestedUserAction = handlerResult.SuggestedUserAction;

                        if (handlerResult.StopProcessing)
                            break;
                    }
                    catch (Exception handlerException)
                    {
                        // ハンドラー自体の例外は記録するが、処理は継続
                        _loggingService?.LogConnection("ExceptionProcessor", false, 0, 
                            $"Handler {handler.GetType().Name} failed: {handlerException.Message}");
                    }
                }
            }

            // 処理結果のログ記録
            await LogProcessingResultAsync(processingContext, result);

            return result;
        }

        /// <summary>
        /// 例外メトリクスの取得
        /// </summary>
        public Dictionary<Type, ExceptionMetrics> GetMetrics()
        {
            return _metrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// メトリクスのクリア
        /// </summary>
        public void ClearMetrics()
        {
            _metrics.Clear();
        }

        private void RegisterDefaultHandlers()
        {
            // 優先度順にハンドラーを登録
            RegisterHandler(new NetworkOperationExceptionHandler());
            RegisterHandler(new AuthenticationExceptionHandler());
            RegisterHandler(new ConfigurationExceptionHandler());
            RegisterHandler(new ResourceExceptionHandler());
            RegisterHandler(new GenericExceptionHandler()); // 最低優先度
        }

        private ErrorSeverity DetermineSeverity(Exception exception)
        {
            return exception switch
            {
                WifiExceptionBase wifiEx => wifiEx.Severity,
                OutOfMemoryException => ErrorSeverity.Critical,
                UnauthorizedAccessException => ErrorSeverity.High,
                TimeoutException => ErrorSeverity.Medium,
                ArgumentException => ErrorSeverity.Low,
                _ => ErrorSeverity.Medium
            };
        }

        private void UpdateMetrics(Exception exception)
        {
            var exceptionType = exception.GetType();
            _metrics.AddOrUpdate(exceptionType,
                new ExceptionMetrics { Count = 1, LastOccurrence = DateTime.UtcNow, ExceptionType = exceptionType },
                (key, existing) => 
                {
                    existing.Count++;
                    existing.LastOccurrence = DateTime.UtcNow;
                    return existing;
                });
        }

        private async Task LogProcessingResultAsync(ExceptionProcessingContext context, ExceptionProcessingResult result)
        {
            var logMessage = $"Exception processed: {context.Exception.GetType().Name} - " +
                           $"Handled: {result.WasHandled}, " +
                           $"Recovery actions: {result.RecoveryActions.Count}, " +
                           $"Context: {context.Context}";

            _loggingService?.LogConnection("ExceptionProcessor", result.WasHandled, 0, logMessage);
        }
    }

    /// <summary>
    /// 例外処理ハンドラーのインターフェース
    /// </summary>
    public interface IExceptionHandler
    {
        int Priority { get; }
        Task<bool> CanHandleAsync(ExceptionProcessingContext context);
        Task<ExceptionHandlingResult> HandleAsync(ExceptionProcessingContext context);
    }

    /// <summary>
    /// 例外処理コンテキスト
    /// </summary>
    public class ExceptionProcessingContext
    {
        public Exception Exception { get; set; } = null!;
        public string Context { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public ErrorSeverity Severity { get; set; }
        public Guid ProcessingId { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }

    /// <summary>
    /// 例外処理結果
    /// </summary>
    public class ExceptionProcessingResult
    {
        public Guid ProcessingId { get; set; }
        public Exception OriginalException { get; set; } = null!;
        public bool WasHandled { get; set; }
        public List<RecoveryAction> RecoveryActions { get; set; } = new();
        public string SuggestedUserAction { get; set; } = "";
    }

    /// <summary>
    /// ハンドラー処理結果
    /// </summary>
    public class ExceptionHandlingResult
    {
        public bool WasHandled { get; set; }
        public bool StopProcessing { get; set; }
        public List<RecoveryAction> RecoveryActions { get; set; } = new();
        public string SuggestedUserAction { get; set; } = "";
    }

    /// <summary>
    /// 復旧アクション
    /// </summary>
    public class RecoveryAction
    {
        public string ActionType { get; set; } = "";
        public string Description { get; set; } = "";
        public Func<Task<bool>>? ExecuteAsync { get; set; }
        public int Priority { get; set; }
    }

    /// <summary>
    /// 例外メトリクス
    /// </summary>
    public class ExceptionMetrics
    {
        public Type ExceptionType { get; set; } = null!;
        public int Count { get; set; }
        public DateTime LastOccurrence { get; set; }
        public DateTime FirstOccurrence { get; set; } = DateTime.UtcNow;
    }
}