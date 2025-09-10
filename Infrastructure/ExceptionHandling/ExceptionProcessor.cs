using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Infrastructure.ExceptionHandling
{
    /// <summary>
    /// 例外処理プロセッサー
    /// </summary>
    public interface IExceptionProcessor
    {
        Task<ExceptionHandlingResult> ProcessExceptionAsync(Exception exception, ExceptionContext context = null);
        void RegisterHandler(IExceptionHandler handler);
        void UnregisterHandler(IExceptionHandler handler);
        Task<bool> HandleGlobalExceptionAsync(Exception exception);
    }

    /// <summary>
    /// 例外処理プロセッサーの実装
    /// </summary>
    public class ExceptionProcessor : IExceptionProcessor
    {
        private readonly List<IExceptionHandler> _handlers;
        private readonly object _lockObject = new object();

        public ExceptionProcessor()
        {
            _handlers = new List<IExceptionHandler>();
        }

        /// <summary>
        /// 例外を処理
        /// </summary>
        public async Task<ExceptionHandlingResult> ProcessExceptionAsync(Exception exception, ExceptionContext context = null)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            context ??= new ExceptionContext
            {
                OperationName = "Unknown",
                ComponentName = "Unknown",
                Timestamp = DateTime.Now
            };

            // ハンドラーを優先度順にソート
            var sortedHandlers = GetSortedHandlers();

            foreach (var handler in sortedHandlers)
            {
                try
                {
                    if (handler.CanHandle(exception))
                    {
                        var result = await handler.HandleAsync(exception, context);
                        if (result.Handled)
                        {
                            return result;
                        }
                    }
                }
                catch (Exception handlerException)
                {
                    // ハンドラー自体でエラーが発生した場合のフォールバック
                    System.Diagnostics.Debug.WriteLine($"Exception handler failed: {handlerException.Message}");
                    continue;
                }
            }

            // どのハンドラーも処理できなかった場合のデフォルト処理
            return new ExceptionHandlingResult
            {
                Handled = false,
                ShouldContinue = true,
                Message = "未処理の例外が発生しました。",
                ProcessedException = exception
            };
        }

        /// <summary>
        /// ハンドラーを登録
        /// </summary>
        public void RegisterHandler(IExceptionHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            lock (_lockObject)
            {
                if (!_handlers.Contains(handler))
                {
                    _handlers.Add(handler);
                }
            }
        }

        /// <summary>
        /// ハンドラーの登録を解除
        /// </summary>
        public void UnregisterHandler(IExceptionHandler handler)
        {
            if (handler == null)
                return;

            lock (_lockObject)
            {
                _handlers.Remove(handler);
            }
        }

        /// <summary>
        /// グローバル例外を処理
        /// </summary>
        public async Task<bool> HandleGlobalExceptionAsync(Exception exception)
        {
            try
            {
                var context = new ExceptionContext
                {
                    OperationName = "GlobalException",
                    ComponentName = "Application",
                    Timestamp = DateTime.Now
                };

                var result = await ProcessExceptionAsync(exception, context);
                return result.Handled && result.ShouldContinue;
            }
            catch
            {
                // 最後の手段として、すべての例外を握りつぶす
                return false;
            }
        }

        /// <summary>
        /// ハンドラーを優先度順に取得
        /// </summary>
        private List<IExceptionHandler> GetSortedHandlers()
        {
            lock (_lockObject)
            {
                return _handlers.OrderByDescending(h => h.Priority).ToList();
            }
        }
    }

    /// <summary>
    /// グローバル例外管理クラス
    /// </summary>
    public static class GlobalExceptionManager
    {
        private static IExceptionProcessor _processor;
        private static bool _isInitialized = false;

        /// <summary>
        /// 例外処理システムを初期化
        /// </summary>
        public static void Initialize(IExceptionProcessor processor)
        {
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));

            if (!_isInitialized)
            {
                // 未処理例外のイベントハンドラーを設定
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                _isInitialized = true;
            }
        }

        /// <summary>
        /// 未処理例外のハンドラー
        /// </summary>
        private static async void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception && _processor != null)
            {
                try
                {
                    await _processor.HandleGlobalExceptionAsync(exception);
                }
                catch
                {
                    // 何もしない - 最終的な安全弁
                }
            }
        }

        /// <summary>
        /// 観測されていないタスク例外のハンドラー
        /// </summary>
        private static async void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            if (_processor != null)
            {
                try
                {
                    var handled = await _processor.HandleGlobalExceptionAsync(e.Exception);
                    if (handled)
                    {
                        e.SetObserved(); // 例外を観測済みとしてマーク
                    }
                }
                catch
                {
                    // 何もしない
                }
            }
        }

        /// <summary>
        /// 例外を手動で処理
        /// </summary>
        public static async Task<ExceptionHandlingResult> HandleExceptionAsync(Exception exception, ExceptionContext context = null)
        {
            if (_processor == null)
                throw new InvalidOperationException("Exception processor is not initialized");

            return await _processor.ProcessExceptionAsync(exception, context);
        }
    }

    /// <summary>
    /// 例外処理ヘルパークラス
    /// </summary>
    public static class ExceptionHelper
    {
        /// <summary>
        /// 安全に実行（例外をキャッチして処理）
        /// </summary>
        public static async Task<T> ExecuteSafelyAsync<T>(
            Func<Task<T>> operation,
            ExceptionContext context = null,
            T defaultValue = default)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex)
            {
                var result = await GlobalExceptionManager.HandleExceptionAsync(ex, context);
                return result.ShouldContinue ? defaultValue : throw ex;
            }
        }

        /// <summary>
        /// 安全に実行（例外をキャッチして処理） - 戻り値なし
        /// </summary>
        public static async Task ExecuteSafelyAsync(
            Func<Task> operation,
            ExceptionContext context = null,
            bool rethrowOnError = false)
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                var result = await GlobalExceptionManager.HandleExceptionAsync(ex, context);
                if (!result.ShouldContinue && rethrowOnError)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// 同期メソッドを安全に実行
        /// </summary>
        public static T ExecuteSafely<T>(
            Func<T> operation,
            ExceptionContext context = null,
            T defaultValue = default)
        {
            try
            {
                return operation();
            }
            catch (Exception ex)
            {
                // 同期的に処理するため、Taskを待機
                var result = GlobalExceptionManager.HandleExceptionAsync(ex, context).GetAwaiter().GetResult();
                return result.ShouldContinue ? defaultValue : throw ex;
            }
        }

        /// <summary>
        /// 再試行機能付きの安全実行
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            int maxRetries = 3,
            TimeSpan? delay = null,
            ExceptionContext context = null,
            T defaultValue = default)
        {
            var actualDelay = delay ?? TimeSpan.FromSeconds(1);
            Exception lastException = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    if (attempt < maxRetries)
                    {
                        // 再試行前に遅延
                        await Task.Delay(actualDelay);
                        actualDelay = TimeSpan.FromMilliseconds(actualDelay.TotalMilliseconds * 1.5); // 指数バックオフ
                    }
                }
            }

            // 最終的に失敗した場合
            var result = await GlobalExceptionManager.HandleExceptionAsync(lastException, context);
            return result.ShouldContinue ? defaultValue : throw lastException;
        }

        /// <summary>
        /// 例外の詳細情報を取得
        /// </summary>
        public static string GetDetailedExceptionInfo(Exception exception)
        {
            if (exception == null)
                return "No exception information available";

            var details = new List<string>
            {
                $"Type: {exception.GetType().FullName}",
                $"Message: {exception.Message}",
                $"Source: {exception.Source}",
                $"TargetSite: {exception.TargetSite?.Name}"
            };

            if (exception.Data.Count > 0)
            {
                details.Add("Data:");
                foreach (var key in exception.Data.Keys)
                {
                    details.Add($"  {key}: {exception.Data[key]}");
                }
            }

            if (exception.InnerException != null)
            {
                details.Add($"Inner Exception: {GetDetailedExceptionInfo(exception.InnerException)}");
            }

            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                details.Add($"Stack Trace: {exception.StackTrace}");
            }

            return string.Join(Environment.NewLine, details);
        }

        /// <summary>
        /// 例外チェーンを取得
        /// </summary>
        public static IEnumerable<Exception> GetExceptionChain(Exception exception)
        {
            var current = exception;
            while (current != null)
            {
                yield return current;
                current = current.InnerException;
            }
        }

        /// <summary>
        /// 根本的な例外を取得
        /// </summary>
        public static Exception GetRootCause(Exception exception)
        {
            return GetExceptionChain(exception).LastOrDefault() ?? exception;
        }
    }
}