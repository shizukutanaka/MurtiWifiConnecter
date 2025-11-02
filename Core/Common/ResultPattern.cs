using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core.Common
{
    /// <summary>
    /// エラータイプの列挙型
    /// </summary>
    public enum ErrorType
    {
        None = 0,
        Validation = 1,
        Network = 2,
        Security = 3,
        RateLimitExceeded = 4,
        Timeout = 5,
        Cancelled = 6,
        Unexpected = 7,
        NotFound = 8,
        PermissionDenied = 9,
        Configuration = 10
    }

    /// <summary>
    /// エラー情報を表現するクラス
    /// </summary>
    public class Error
    {
        public ErrorType Type { get; }
        public string Message { get; }
        public string Details { get; }
        public Dictionary<string, object> Context { get; }
        public Exception InnerException { get; }

        public Error(ErrorType type, string message, string details = null, Dictionary<string, object> context = null, Exception innerException = null)
        {
            Type = type;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Details = details;
            Context = context ?? new Dictionary<string, object>();
            InnerException = innerException;
        }

        public static Error Validation(string message, Dictionary<string, object> context = null)
            => new(ErrorType.Validation, message, context: context);

        public static Error Network(string message, string details = null, Exception innerException = null)
            => new(ErrorType.Network, message, details, innerException: innerException);

        public static Error Security(string message, string details = null)
            => new(ErrorType.Security, message, details);

        public static Error RateLimitExceeded(string message)
            => new(ErrorType.RateLimitExceeded, message);

        public static Error Timeout(string message)
            => new(ErrorType.Timeout, message);

        public static Error Cancelled(string message = "Operation was cancelled")
            => new(ErrorType.Cancelled, message);

        public static Error Unexpected(string message, Exception innerException = null)
            => new(ErrorType.Unexpected, message, innerException: innerException);

        public static Error NotFound(string resource)
            => new(ErrorType.NotFound, $"{resource} not found");

        public static Error PermissionDenied(string message)
            => new(ErrorType.PermissionDenied, message);

        public static Error Configuration(string message, Dictionary<string, object> context = null)
            => new(ErrorType.Configuration, message, context: context);
    }

    /// <summary>
    /// 操作結果を表現するクラス
    /// </summary>
    public class NetworkOperationResult<T>
    {
        public bool IsSuccess { get; }
        public T Data { get; }
        public Error Error { get; }
        public Dictionary<string, object> Metadata { get; }
        public DateTime Timestamp { get; }

        private NetworkOperationResult(bool isSuccess, T data, Error error, Dictionary<string, object> metadata)
        {
            IsSuccess = isSuccess;
            Data = data;
            Error = error;
            Metadata = metadata ?? new Dictionary<string, object>();
            Timestamp = DateTime.UtcNow;
        }

        public static NetworkOperationResult<T> Success(T data, Dictionary<string, object> metadata = null)
            => new(true, data, null, metadata);

        public static NetworkOperationResult<T> Failure(Error error, Dictionary<string, object> metadata = null)
            => new(false, default, error, metadata);

        public NetworkOperationResult<U> Map<U>(Func<T, U> mapper)
        {
            if (IsSuccess)
            {
                try
                {
                    return NetworkOperationResult<U>.Success(mapper(Data), Metadata);
                }
                catch (Exception ex)
                {
                    return NetworkOperationResult<U>.Failure(Error.Unexpected("Mapping failed", ex), Metadata);
                }
            }
            else
            {
                return NetworkOperationResult<U>.Failure(Error, Metadata);
            }
        }

        public async Task<NetworkOperationResult<U>> MapAsync<U>(Func<T, Task<U>> mapper)
        {
            if (IsSuccess)
            {
                try
                {
                    var result = await mapper(Data);
                    return NetworkOperationResult<U>.Success(result, Metadata);
                }
                catch (Exception ex)
                {
                    return NetworkOperationResult<U>.Failure(Error.Unexpected("Async mapping failed", ex), Metadata);
                }
            }
            else
            {
                return NetworkOperationResult<U>.Failure(Error, Metadata);
            }
        }

        public NetworkOperationResult<T> OnSuccess(Action<T> action)
        {
            if (IsSuccess)
            {
                try
                {
                    action(Data);
                }
                catch (Exception ex)
                {
                    return NetworkOperationResult<T>.Failure(Error.Unexpected("Success action failed", ex), Metadata);
                }
            }
            return this;
        }

        public NetworkOperationResult<T> OnFailure(Action<Error> action)
        {
            if (!IsSuccess)
            {
                action(Error);
            }
            return this;
        }
    }

    /// <summary>
    /// ネットワーク操作の拡張メソッド
    /// </summary>
    public static class NetworkOperationResultExtensions
    {
        public static async Task<NetworkOperationResult<T>> TryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await operation();
                return NetworkOperationResult<T>.Success(result);
            }
            catch (OperationCanceledException)
            {
                return NetworkOperationResult<T>.Failure(Error.Cancelled("Operation was cancelled"));
            }
            catch (ArgumentException ex)
            {
                return NetworkOperationResult<T>.Failure(Error.Validation(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return NetworkOperationResult<T>.Failure(Error.PermissionDenied(ex.Message));
            }
            catch (TimeoutException ex)
            {
                return NetworkOperationResult<T>.Failure(Error.Timeout(ex.Message));
            }
            catch (Exception ex)
            {
                return NetworkOperationResult<T>.Failure(Error.Unexpected(ex.Message, ex));
            }
        }

        public static NetworkOperationResult<T> Try<T>(Func<T> operation)
        {
            try
            {
                var result = operation();
                return NetworkOperationResult<T>.Success(result);
            }
            catch (ArgumentException ex)
            {
                return NetworkOperationResult<T>.Failure(Error.Validation(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return NetworkOperationResult<T>.Failure(Error.PermissionDenied(ex.Message));
            }
            catch (Exception ex)
            {
                return NetworkOperationResult<T>.Failure(Error.Unexpected(ex.Message, ex));
            }
        }

        public static NetworkOperationResult<Unit> Try(Action operation)
        {
            try
            {
                operation();
                return NetworkOperationResult<Unit>.Success(Unit.Value);
            }
            catch (ArgumentException ex)
            {
                return NetworkOperationResult<Unit>.Failure(Error.Validation(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return NetworkOperationResult<Unit>.Failure(Error.PermissionDenied(ex.Message));
            }
            catch (Exception ex)
            {
                return NetworkOperationResult<Unit>.Failure(Error.Unexpected(ex.Message, ex));
            }
        }
    }

    /// <summary>
    /// 単位型（何も値を持たない型）
    /// </summary>
    public struct Unit
    {
        public static readonly Unit Value = new();
    }

    /// <summary>
    /// バリデーション結果
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; }
        public string Message { get; }
        public Dictionary<string, object> Context { get; }

        private ValidationResult(bool isValid, string message, Dictionary<string, object> context)
        {
            IsValid = isValid;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Context = context ?? new Dictionary<string, object>();
        }

        public static ValidationResult Success(string message = "Validation successful")
            => new(true, message, new Dictionary<string, object>());

        public static ValidationResult Failure(string message, Dictionary<string, object> context = null)
            => new(false, message, context);
    }

    /// <summary>
    /// レート制限結果
    /// </summary>
    public class RateLimitResult
    {
        public bool IsAllowed { get; }
        public string Message { get; }
        public TimeSpan RetryAfter { get; }
        public Dictionary<string, object> Context { get; }

        private RateLimitResult(bool isAllowed, string message, TimeSpan retryAfter, Dictionary<string, object> context)
        {
            IsAllowed = isAllowed;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            RetryAfter = retryAfter;
            Context = context ?? new Dictionary<string, object>();
        }

        public static RateLimitResult Allowed(string message = "Rate limit check passed")
            => new(true, message, TimeSpan.Zero, new Dictionary<string, object>());

        public static RateLimitResult Denied(string message, TimeSpan retryAfter, Dictionary<string, object> context = null)
            => new(false, message, retryAfter, context);
    }
}
