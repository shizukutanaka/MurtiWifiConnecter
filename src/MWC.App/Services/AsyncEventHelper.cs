using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace MWC.App.Services;

/// <summary>
/// WPFイベントハンドラで `async void` が必須となる現実への対処。
///
/// async void は例外を上位に伝播せず、UI スレッドの未捕捉例外として
/// プロセスクラッシュにつながる。このヘルパーで全例外を捕捉し、
/// ログとユーザー通知に振り分ける。
///
/// 使い方:
///   private async void OnClick(object sender, RoutedEventArgs e)
///       => await AsyncEventHelper.SafeRunAsync(_log, "OnClick", async () =>
///       {
///           await DoSomethingAsync();
///       });
/// </summary>
public static class AsyncEventHelper
{
    /// <summary>
    /// 例外捕捉付きで非同期処理を実行。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Never-throw boundary by design (see class doc): SafeRunAsync exists so an async-void event handler can never crash the process; every failure is logged and routed to onError instead.")]
    public static async Task SafeRunAsync(
        ILogger? log,
        string operationName,
        Func<Task> action,
        Action<Exception>? onError = null)
    {
        try
        {
            await action().ConfigureAwait(true);  // UIスレッドに戻る
        }
        catch (OperationCanceledException)
        {
            // キャンセルは通常運転、無視
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "Event handler {Op} failed", operationName);
            try { onError?.Invoke(ex); } catch { /* onError 自身もクラッシュしない */ }
        }
    }

    /// <summary>
    /// 戻り値版。失敗時は default を返す。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Never-throw boundary by design (see class doc): failures are logged and converted to the fallback value; no caller exists that could handle a rethrown exception.")]
    public static async Task<T?> SafeRunAsync<T>(
        ILogger? log,
        string operationName,
        Func<Task<T>> action,
        T? fallback = default)
    {
        try { return await action().ConfigureAwait(true); }
        catch (OperationCanceledException) { return fallback; }
        catch (Exception ex)
        {
            log?.LogError(ex, "Event handler {Op} failed", operationName);
            return fallback;
        }
    }
}
