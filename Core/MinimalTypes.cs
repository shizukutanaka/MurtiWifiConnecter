using System;
using System.Security;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Minimal type definitions for the application
    /// </summary>

    // Result pattern for error handling
    public readonly struct Result<T>
    {
        public T Value { get; }
        public string Error { get; }
        public bool IsSuccess { get; }

        private Result(T value, string error, bool isSuccess)
        {
            Value = value;
            Error = error;
            IsSuccess = isSuccess;
        }

        public static Result<T> Success(T value) => new(value, string.Empty, true);
        public static Result<T> Failure(string error) => new(default!, error ?? "Unknown error", false);
    }

    // Basic WiFi network information
    public class WifiNetwork
    {
        public string SSID { get; set; } = string.Empty;
        public int SignalStrength { get; set; }
        public string Authentication { get; set; } = "Open";
        public string Encryption { get; set; } = "None";
        public bool IsSecured => !string.Equals(Authentication, "Open", StringComparison.OrdinalIgnoreCase);
        public bool IsConnected { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public bool HasConnectedBefore { get; set; }

        public string SignalQuality => SignalStrength switch
        {
            >= 80 => "Excellent",
            >= 60 => "Good",
            >= 40 => "Fair",
            >= 20 => "Weak",
            _ => "Very Weak"
        };

        public string DisplayText => HasConnectedBefore ? $"{SSID} ★" : SSID;

        public override string ToString() => $"{SSID} ({SignalStrength}%)";
    }

    public class WifiAdapterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public bool IsUp { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    // Connection result
    public class WifiConnectionResult
    {
        public bool Success { get; set; }
        public string SSID { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }

        public static WifiConnectionResult CreateSuccess(string ssid) => new()
        {
            Success = true,
            SSID = ssid,
            Message = "Connected successfully",
            Timestamp = DateTime.UtcNow
        };

        public static WifiConnectionResult CreateFailure(string message) => new()
        {
            Success = false,
            Message = message,
            ErrorMessage = message,
            Timestamp = DateTime.UtcNow
        };
    }

    // Process execution result
    public class ProcessResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public int ExitCode { get; set; }
    }

    // Network information
    public class NetworkInfo
    {
        public string SSID { get; set; } = string.Empty;
        public int SignalStrength { get; set; }
        public string Authentication { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
    }

    // Connection state
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Disconnecting,
        Error
    }

    // Error severity
    public enum ErrorSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    // Event args for connection state changes
    public class ConnectionStateChangedEventArgs : EventArgs
    {
        public string SSID { get; }
        public ConnectionState PreviousState { get; }
        public ConnectionState CurrentState { get; }
        public string? Message { get; }

        public ConnectionStateChangedEventArgs(string ssid, ConnectionState previous, ConnectionState current, string? message = null)
        {
            SSID = ssid;
            PreviousState = previous;
            CurrentState = current;
            Message = message;
        }

        // Compatibility constructor
        public ConnectionStateChangedEventArgs(ConnectionState previous, ConnectionState current, string? ssid, string? source)
            : this(ssid ?? string.Empty, previous, current, source)
        {
        }
    }

    // Event args for WiFi errors
    public class WifiErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; }
        public Exception? Exception { get; }
        public string? SSID { get; }
        public ErrorSeverity Severity { get; }
        public string? Context { get; }

        public WifiErrorEventArgs(string errorMessage, Exception? exception = null, string? ssid = null,
            ErrorSeverity severity = ErrorSeverity.Error, string? context = null)
        {
            ErrorMessage = errorMessage;
            Exception = exception;
            SSID = ssid;
            Severity = severity;
            Context = context;
        }

        // Compatibility constructor
        public WifiErrorEventArgs(Exception error, string operation, string? ssid, ErrorSeverity severity, bool canRetry, string source)
            : this(error?.Message ?? "Unknown error", error, ssid, severity, $"{source}.{operation}")
        {
        }
    }

    // SecureString extension
    public static class SecureStringExtensions
    {
        public static string ToUnsecuredString(this SecureString secureString)
        {
            if (secureString == null)
                return string.Empty;

            var ptr = IntPtr.Zero;
            try
            {
                ptr = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(secureString);
                return System.Runtime.InteropServices.Marshal.PtrToStringBSTR(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(ptr);
            }
        }
    }
}