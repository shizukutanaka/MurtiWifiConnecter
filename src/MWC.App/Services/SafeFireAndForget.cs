using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MWC.App.Services;

/// <summary>
/// Apple HIG / .NET ベストプラクティス:
///   "async void は例外を握り潰す。常に Task を返してエラーを伝播せよ。"
///
/// やむを得ず fire-and-forget が必要なケース(イベントハンドラ等)では
/// このヘルパーで全例外を必ずログに記録する。
///
/// 使い方:
///   _someTask.SafeFireAndForget(_log, "コンテキスト");
///   _ = SafeFireAndForget.Run(() => DoSomethingAsync(), _log);
/// </summary>
public static class SafeFireAndForget
{
    /// <summary>
    /// Task に例外ハンドラを attach する。await されない Task の例外を必ずログに残す。
    /// </summary>
    public static void Forget(this Task task, ILogger? log = null,
        string? operation = null,
        [System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
    {
        task.ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception is not null)
            {
                log?.LogError(t.Exception,
                    "fire-and-forget failure in {caller} (operation={op})",
                    callerName, operation ?? "-");
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// 関数を非同期で実行し、例外を握り潰さない。
    /// </summary>
    public static Task Run(Func<Task> action, ILogger? log = null,
        string? operation = null)
    {
        var task = action();
        task.Forget(log, operation);
        return task;
    }

    /// <summary>
    /// 戻り値ありの fire-and-forget。
    /// </summary>
    public static Task<T?> RunWithFallback<T>(Func<Task<T>> action, T? fallback,
        ILogger? log = null, string? operation = null)
        => Task.Run(async () =>
        {
            try { return await action().ConfigureAwait(false); }
            catch (Exception ex)
            {
                log?.LogError(ex, "fire-and-forget failure (operation={op})", operation ?? "-");
                return fallback;
            }
        });
}
