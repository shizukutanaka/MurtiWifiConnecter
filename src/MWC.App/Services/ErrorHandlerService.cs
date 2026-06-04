using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.App.Services;

/// <summary>
/// アプリ全体のエラー処理を一元化。
/// Apple HIG "Helpful Error Messages":
///   "Don't blame the user. Explain the problem. Suggest a solution."
///
/// 機能:
///   - 構造化ログ(操作種別 + コンテキスト)
///   - ユーザー向けメッセージ生成(技術用語なし)
///   - 例外種別別の自動分類
///   - 通知サービス連携
/// </summary>
public sealed class ErrorHandlerService
{
    private readonly ILogger<ErrorHandlerService> _log;
    private readonly NotificationService          _notify;

    public ErrorHandlerService(ILogger<ErrorHandlerService> log, NotificationService notify)
    {
        _log = log; _notify = notify;
    }

    /// <summary>
    /// 例外を捕捉して、ユーザーへの伝達を含めたフルハンドリング。
    /// 戻り値: ユーザー向けメッセージ
    /// </summary>
    public string Handle(Exception ex, string operation, string? context = null,
        bool notifyUser = false)
    {
        var category = Classify(ex);
        var userMsg  = BuildUserMessage(category, operation);

        // 構造化ログ
        _log.LogError(ex, "Operation={Op} Category={Cat} Context={Ctx}",
            operation, category, context ?? "-");

        if (notifyUser)
            _notify.NotifyFailed(operation, MapToFailure(category));

        return userMsg;
    }

    /// <summary>
    /// async タスク全体をラップ。例外時はエラーメッセージを返す。
    /// </summary>
    public async Task<TryResult<T>> TryAsync<T>(
        Func<Task<T>> task, string operation, string? context = null)
    {
        try
        {
            var result = await task();
            return TryResult<T>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            return TryResult<T>.Cancelled;
        }
        catch (Exception ex)
        {
            var msg = Handle(ex, operation, context);
            return TryResult<T>.Fail(msg);
        }
    }

    private static ErrorCategory Classify(Exception ex) => ex switch
    {
        UnauthorizedAccessException     => ErrorCategory.Permission,
        System.Net.NetworkInformation.NetworkInformationException
                                        => ErrorCategory.Network,
        System.IO.IOException           => ErrorCategory.Io,
        TimeoutException                => ErrorCategory.Timeout,
        ArgumentException               => ErrorCategory.InvalidInput,
        InvalidOperationException       => ErrorCategory.InvalidState,
        _                               => ErrorCategory.Unknown
    };

    private static string BuildUserMessage(ErrorCategory cat, string operation) => cat switch
    {
        ErrorCategory.Permission   =>
            $MWC.App.Resources.L.Get("Error_Permission"),
        ErrorCategory.Network      =>
            $MWC.App.Resources.L.Get("Error_Network"),
        ErrorCategory.Io           =>
            $MWC.App.Resources.L.Get("Error_Io"),
        ErrorCategory.Timeout      =>
            $MWC.App.Resources.L.Get("Error_Timeout"),
        ErrorCategory.InvalidInput =>
            $MWC.App.Resources.L.Get("Error_InvalidInput"),
        ErrorCategory.InvalidState =>
            $MWC.App.Resources.L.Get("Error_InvalidState"),
        _                          =>
            $MWC.App.Resources.L.Get("Error_Unknown")
    };

    private static ConnectionFailure MapToFailure(ErrorCategory cat) => cat switch
    {
        ErrorCategory.Permission => ConnectionFailure.InsufficientPrivilege,
        ErrorCategory.Network    => ConnectionFailure.NotInRange,
        ErrorCategory.Timeout    => ConnectionFailure.Timeout,
        _                        => ConnectionFailure.Unknown
    };
}

public enum ErrorCategory
{
    Unknown, Permission, Network, Io, Timeout, InvalidInput, InvalidState
}

/// <summary>例外を投げない結果型</summary>
public readonly record struct TryResult<T>(bool Success, T? Value, string? ErrorMessage, bool IsCancelled)
{
    public static TryResult<T> Ok(T value)              => new(true, value, null, false);
    public static TryResult<T> Fail(string msg)         => new(false, default, msg, false);
    public static TryResult<T> Cancelled                => new(false, default, null, true);
}
