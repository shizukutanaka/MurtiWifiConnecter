namespace MWC.Core.Models;

/// <summary>
/// 接続/操作結果。例外を投げず Result<T,E> で返す。
/// FluentResults依存回避のため自前最小実装。
/// </summary>
public readonly record struct ConnectionResult
{
    public bool Success { get; init; }
    public ConnectionFailure? Failure { get; init; }
    public string? ConnectedSsid { get; init; }
    public bool HasInternet { get; init; }
    public bool BehindCaptivePortal { get; init; }

    public static ConnectionResult Ok(string ssid, bool internet, bool captive) =>
        new() { Success = true, ConnectedSsid = ssid, HasInternet = internet, BehindCaptivePortal = captive };

    public static ConnectionResult Fail(ConnectionFailure failure) =>
        new() { Success = false, Failure = failure };
}

public enum ConnectionFailure
{
    Unknown,
    InvalidProfile,
    AdapterDisabled,
    AdapterNotFound,
    BadCredentials,
    Timeout,
    Cancelled,
    ProfileRejected,
    NotInRange,
    InsufficientPrivilege,
    OsError
}
