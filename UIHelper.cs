using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// UIヘルパー - シンプルなUI操作支援
    /// </summary>
    public static class UIHelper
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
        
        /// <summary>
        /// UIスレッドで非同期実行
        /// </summary>
        public static async Task RunOnUIThreadAsync(Action action)
        {
            if (Application.Current?.Dispatcher == null)
                return;
                
            await Application.Current.Dispatcher.InvokeAsync(action, DispatcherPriority.Background);
        }
        
        /// <summary>
        /// UIスレッドで同期実行
        /// </summary>
        public static void RunOnUIThread(Action action)
        {
            if (Application.Current?.Dispatcher == null)
                return;
                
            if (Application.Current.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(action, DispatcherPriority.Normal);
            }
        }
        
        /// <summary>
        /// 遅延実行
        /// </summary>
        public static async Task DelayedExecuteAsync(Action action, int delayMs)
        {
            await Task.Delay(delayMs);
            await RunOnUIThreadAsync(action);
        }
        
        /// <summary>
        /// プログレスダイアログ表示
        /// </summary>
        public static async Task<T> ShowProgressAsync<T>(
            string message,
            Func<Task<T>> operation,
            Window? owner = null)
        {
            // 実際のプログレスウィンドウは実装せず、シンプルにカーソル変更のみ
            Window? targetWindow = owner ?? Application.Current?.MainWindow;
            
            if (targetWindow != null)
            {
                var originalCursor = targetWindow.Cursor;
                targetWindow.Cursor = System.Windows.Input.Cursors.Wait;
                
                try
                {
                    return await operation();
                }
                finally
                {
                    targetWindow.Cursor = originalCursor;
                }
            }
            
            return await operation();
        }
        
        /// <summary>
        /// エラーメッセージ表示
        /// </summary>
        public static void ShowError(string message, string title = "エラー")
        {
            RunOnUIThread(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        
        /// <summary>
        /// 情報メッセージ表示
        /// </summary>
        public static void ShowInfo(string message, string title = "情報")
        {
            RunOnUIThread(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }
        
        /// <summary>
        /// 確認ダイアログ表示
        /// </summary>
        public static bool ShowConfirmation(string message, string title = "確認")
        {
            bool result = false;
            RunOnUIThread(() =>
            {
                result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            });
            return result;
        }
        
        /// <summary>
        /// タイムアウト付き操作
        /// </summary>
        public static async Task<T?> ExecuteWithTimeoutAsync<T>(
            Func<Task<T>> operation,
            TimeSpan? timeout = null)
        {
            timeout ??= DefaultTimeout;
            
            var timeoutTask = Task.Delay(timeout.Value);
            var operationTask = operation();
            
            var completedTask = await Task.WhenAny(operationTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"Operation timed out after {timeout.Value.TotalSeconds} seconds");
            }
            
            return await operationTask;
        }
        
        /// <summary>
        /// バッチUI更新
        /// </summary>
        public static void BatchUpdate(Action updateAction)
        {
            if (Application.Current?.Dispatcher == null)
                return;
                
            Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.DataBind,
                updateAction);
        }
        
        /// <summary>
        /// UIの応答性を保つ
        /// </summary>
        public static async Task KeepUIResponsiveAsync()
        {
            await Application.Current?.Dispatcher?.Yield(DispatcherPriority.Background);
        }
    }
}