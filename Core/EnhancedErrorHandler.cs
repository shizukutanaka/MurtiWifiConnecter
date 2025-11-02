using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Enhanced error handling with graceful degradation, retry logic, and user-friendly messages
    /// </summary>
    public class EnhancedErrorHandler
    {
        private static readonly ILogger _logger;
        private static readonly Dictionary<Type, Func<Exception, ErrorResponse>> _errorHandlers;
        private static readonly Dictionary<string, CircuitBreaker> _circuitBreakers;
        private static readonly object _lock = new object();

        static EnhancedErrorHandler()
        {
            // Initialize Serilog structured logging
            _logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "MurtiWifiConnecter")
                .Enrich.WithProperty("Version", "3.2.0")
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .WriteTo.File(
                    path: Path.Combine(GetLogDirectory(), "murtiwifi-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
                    shared: true)
                .WriteTo.File(
                    path: Path.Combine(GetLogDirectory(), "errors-.json"),
                    restrictedToMinimumLevel: LogEventLevel.Error,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 90,
                    formatter: new Serilog.Formatting.Json.JsonFormatter())
                .CreateLogger();

            // Initialize error handlers for specific exception types
            _errorHandlers = new Dictionary<Type, Func<Exception, ErrorResponse>>
            {
                [typeof(UnauthorizedAccessException)] = HandleUnauthorizedAccess,
                [typeof(System.Net.NetworkInformation.NetworkInformationException)] = HandleNetworkError,
                [typeof(System.Security.SecurityException)] = HandleSecurityError,
                [typeof(InvalidOperationException)] = HandleInvalidOperation,
                [typeof(TimeoutException)] = HandleTimeout,
                [typeof(System.ComponentModel.Win32Exception)] = HandleWin32Error,
                [typeof(IOException)] = HandleIOError,
                [typeof(ArgumentException)] = HandleArgumentError
            };

            _circuitBreakers = new Dictionary<string, CircuitBreaker>();
        }

        /// <summary>
        /// Handle exception with retry logic and circuit breaker pattern
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            int maxRetries = 3,
            int delayMs = 1000,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            var circuitBreaker = GetOrCreateCircuitBreaker(operationName);

            if (!circuitBreaker.CanExecute())
            {
                throw new InvalidOperationException(
                    $"Circuit breaker is OPEN for operation '{operationName}'. Too many recent failures.");
            }

            var exceptions = new List<Exception>();
            var stopwatch = Stopwatch.StartNew();

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.Information(
                        "Executing {OperationName} (Attempt {Attempt}/{MaxRetries}) from {Caller}:{Line}",
                        operationName, attempt, maxRetries, Path.GetFileName(callerFile), callerLine);

                    var result = await operation();

                    circuitBreaker.RecordSuccess();

                    _logger.Information(
                        "Successfully completed {OperationName} in {ElapsedMs}ms",
                        operationName, stopwatch.ElapsedMilliseconds);

                    return result;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    circuitBreaker.RecordFailure();

                    _logger.Warning(ex,
                        "Attempt {Attempt}/{MaxRetries} failed for {OperationName}: {ErrorMessage}",
                        attempt, maxRetries, operationName, ex.Message);

                    if (attempt < maxRetries)
                    {
                        var delay = CalculateExponentialBackoff(attempt, delayMs);
                        _logger.Information(
                            "Retrying {OperationName} in {DelayMs}ms...",
                            operationName, delay);
                        await Task.Delay(delay);
                    }
                }
            }

            // All retries exhausted
            var aggregateEx = new AggregateException(
                $"Operation '{operationName}' failed after {maxRetries} attempts", exceptions);

            _logger.Error(aggregateEx,
                "All retry attempts exhausted for {OperationName}. Total time: {ElapsedMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);

            throw aggregateEx;
        }

        /// <summary>
        /// Handle exception and provide user-friendly error response
        /// </summary>
        public static ErrorResponse HandleException(
            Exception ex,
            string context,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            // Log with full context
            _logger.Error(ex,
                "Exception in {Context} from {Caller}:{Line}: {ErrorMessage}",
                context, Path.GetFileName(callerFile), callerLine, ex.Message);

            // Find specific handler
            var handler = _errorHandlers
                .Where(kvp => kvp.Key.IsAssignableFrom(ex.GetType()))
                .Select(kvp => kvp.Value)
                .FirstOrDefault();

            if (handler != null)
            {
                return handler(ex);
            }

            // Default handler
            return new ErrorResponse
            {
                ErrorCode = "UNEXPECTED_ERROR",
                Title = "Unexpected Error",
                Message = "An unexpected error occurred. Please try again or contact support if the problem persists.",
                TechnicalDetails = ex.Message,
                Suggestions = new List<string>
                {
                    "Restart the application",
                    "Check that network adapter is enabled",
                    "Run as administrator",
                    "Check error logs for details"
                },
                Severity = ErrorSeverity.High,
                IsRecoverable = false,
                SupportInfo = GetSupportInfo(ex)
            };
        }

        private static ErrorResponse HandleUnauthorizedAccess(Exception ex)
        {
            return new ErrorResponse
            {
                ErrorCode = "PERMISSION_DENIED",
                Title = "Permission Denied",
                Message = "Administrator privileges are required for WiFi operations.",
                TechnicalDetails = ex.Message,
                Suggestions = new List<string>
                {
                    "Close the application",
                    "Right-click on MurtiWifiConnecter.exe",
                    "Select 'Run as administrator'",
                    "Try the operation again"
                },
                Severity = ErrorSeverity.High,
                IsRecoverable = true,
                SupportInfo = GetSupportInfo(ex)
            };
        }

        private static ErrorResponse HandleNetworkError(Exception ex)
        {
            return new ErrorResponse
            {
                ErrorCode = "NETWORK_ERROR",
                Title = "Network Configuration Error",
                Message = "A network configuration error occurred.",
                TechnicalDetails = ex.Message,
                Suggestions = new List<string>
                {
                    "Check that WiFi adapter is enabled",
                    "Verify network drivers are up to date",
                    "Try disabling and re-enabling WiFi adapter",
                    "Restart Windows WLAN AutoConfig service"
                },
                Severity = ErrorSeverity.Medium,
                IsRecoverable = true,
                SupportInfo = GetSupportInfo(ex)
            };
        }

        private static ErrorResponse HandleSecurityError(Exception ex)
        {
            return new ErrorResponse
            {
                ErrorCode = "SECURITY_ERROR",
                Title = "Security Error",
                Message = "A security error occurred. Check permissions and security policies.",
                TechnicalDetails = ex.Message,
                Suggestions = new List<string>
                {
                    "Verify user has appropriate permissions",
                    "Check antivirus is not blocking the application",
                    "Review Windows security policies",
                    "Try running as administrator"
                },
                Severity = ErrorSeverity.High,
                IsRecoverable = true,
                SupportInfo = GetSupportInfo(ex)
            };
        }

        private static ErrorResponse HandleInvalidOperation(Exception ex)
        {
            var isRateLimit = ex.Message.Contains("Rate limit");

            if (isRateLimit)
            {
                return new ErrorResponse
                {
                    ErrorCode = "RATE_LIMIT_EXCEEDED",
                    Title = "Rate Limit Exceeded",
                    Message = "Too many operations in a short time. Please wait a moment.",
                    TechnicalDetails = ex.Message,
                    Suggestions = new List<string>
                    {
                        "Wait 30 seconds before trying again",
                        "Reduce frequency of operations",
                        "Contact support if this persists"
                    },
                    Severity = ErrorSeverity.Low,
                    IsRecoverable = true,
                    SupportInfo = GetSupportInfo(ex)
                };
            }

            return new ErrorResponse
            {
                ErrorCode = "INVALID_OPERATION",
                Title = "Invalid Operation",
                Message = "The requested operation cannot be completed in the current state.",
                TechnicalDetails = ex.Message,
                Suggestions = new List<string>
                {
                    "Check network adapter status",
                    "Verify WiFi is enabled",
                    "Review operation prerequisites"
                },
                Severity = ErrorSeverity.Medium,
                IsRecoverable = true,
                SupportInfo = GetSupportInfo(ex)
            };
        }

        private static ErrorResponse HandleTimeout(Exception ex)
        {
            return new ErrorResponse
            {
                ErrorCode = "OPERATION_TIMEOUT",
                Title = "Operation Timeout",
                Message = "The operation took too long to complete.",
                TechnicalDetails = ex.Message,
                Suggestions = new List<string>
                {
                    "Check network connectivity",
                    "Try the operation again",
                    "Increase timeout if configurable",
                    "Check if other processes are interfering"
                },
                Severity = ErrorSeverity.Medium,
                IsRecoverable = true,
                SupportInfo = GetSupportInfo(ex)
            };
        }

        private static ErrorResponse HandleWin32Error(Exception ex)
        {
            var win32Ex = ex as System.ComponentModel.Win32Exception;
            var nativeCode = win32Ex?.NativeErrorCode ?? 0;

            return new ErrorResponse
            {
                ErrorCode = $"WIN32_ERROR_{nativeCode}",
                Title = "Windows System Error",
                Message = "A Windows system error occurred.",
                TechnicalDetails = $"{ex.Message} (Error Code: {nativeCode})",
                Suggestions = new List<string>
                {
                    "Check Windows Event Viewer for details",
                    "Verify system services are running",
                    "Update system drivers",
                    "Run Windows troubleshooter"
                },
                Severity = ErrorSeverity.High,
                IsRecoverable = false,
                SupportInfo = GetSupportInfo(ex)
            };
        }

        private static ErrorResponse HandleIOError(Exception ex)
        {
            return new ErrorResponse
            {
                ErrorCode = "IO_ERROR",
                Title = "File/Directory Error",
                Message = "A file or directory operation failed.",
                TechnicalDetails = ex.Message,
                Suggestions = new List<string>
                {
                    "Check disk space is available",
                    "Verify file/directory permissions",
                    "Ensure path is not too long",
                    "Check if file is in use by another process"
                },
                Severity = ErrorSeverity.Medium,
                IsRecoverable = true,
                SupportInfo = GetSupportInfo(ex)
            };
        }

        private static ErrorResponse HandleArgumentError(Exception ex)
        {
            return new ErrorResponse
            {
                ErrorCode = "INVALID_ARGUMENT",
                Title = "Invalid Argument",
                Message = "One or more arguments are invalid.",
                TechnicalDetails = ex.Message,
                Suggestions = new List<string>
                {
                    "Check command syntax: MurtiWifiConnecter.exe help",
                    "Verify all required parameters are provided",
                    "Check parameter format and values"
                },
                Severity = ErrorSeverity.Low,
                IsRecoverable = true,
                SupportInfo = GetSupportInfo(ex)
            };
        }

        private static SupportInfo GetSupportInfo(Exception ex)
        {
            var errorId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

            return new SupportInfo
            {
                ErrorId = errorId,
                Timestamp = DateTime.UtcNow,
                Version = "3.2.0",
                Platform = Environment.OSVersion.ToString(),
                ExceptionType = ex.GetType().Name,
                LogFilePath = Path.Combine(GetLogDirectory(), $"murtiwifi-{DateTime.Now:yyyyMMdd}.log"),
                SupportEmail = "support@murtisoft.com",
                SupportUrl = "https://github.com/murtisoft/murtiwifi-connector/issues"
            };
        }

        private static int CalculateExponentialBackoff(int attempt, int baseDelayMs)
        {
            // Exponential backoff with jitter: baseDelay * 2^(attempt-1) + random(0-500ms)
            var exponentialDelay = baseDelayMs * Math.Pow(2, attempt - 1);
            var jitter = new Random().Next(0, 500);
            return (int)exponentialDelay + jitter;
        }

        private static CircuitBreaker GetOrCreateCircuitBreaker(string operationName)
        {
            lock (_lock)
            {
                if (!_circuitBreakers.ContainsKey(operationName))
                {
                    _circuitBreakers[operationName] = new CircuitBreaker(
                        failureThreshold: 5,
                        timeoutSeconds: 60);
                }
                return _circuitBreakers[operationName];
            }
        }

        private static string GetLogDirectory()
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter", "Logs");

            Directory.CreateDirectory(logDir);
            return logDir;
        }

        /// <summary>
        /// Generate diagnostic report for support
        /// </summary>
        public static async Task<string> GenerateDiagnosticReport()
        {
            var reportPath = Path.Combine(GetLogDirectory(), $"diagnostic-{DateTime.Now:yyyyMMddHHmmss}.txt");
            var report = new StringBuilder();

            report.AppendLine("=== MurtiWifi Connector Diagnostic Report ===");
            report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();

            // System Information
            report.AppendLine("=== System Information ===");
            report.AppendLine($"OS: {Environment.OSVersion}");
            report.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            report.AppendLine($"Processor Count: {Environment.ProcessorCount}");
            report.AppendLine($"Machine Name: {Environment.MachineName}");
            report.AppendLine($"User: {Environment.UserName}");
            report.AppendLine($"CLR Version: {Environment.Version}");
            report.AppendLine();

            // Application Information
            report.AppendLine("=== Application Information ===");
            report.AppendLine($"Version: 3.2.0");
            report.AppendLine($"Working Directory: {Directory.GetCurrentDirectory()}");
            report.AppendLine($"Is Administrator: {IsRunningAsAdministrator()}");
            report.AppendLine();

            // Network Adapters
            report.AppendLine("=== Network Adapters ===");
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    report.AppendLine($"  {ni.Name}");
                    report.AppendLine($"    Type: {ni.NetworkInterfaceType}");
                    report.AppendLine($"    Status: {ni.OperationalStatus}");
                    report.AppendLine($"    Speed: {ni.Speed / 1_000_000} Mbps");
                }
            }
            catch (Exception ex)
            {
                report.AppendLine($"  Error getting network adapters: {ex.Message}");
            }
            report.AppendLine();

            // Recent Logs (last 50 lines)
            report.AppendLine("=== Recent Log Entries ===");
            try
            {
                var logFile = Path.Combine(GetLogDirectory(), $"murtiwifi-{DateTime.Now:yyyyMMdd}.log");
                if (File.Exists(logFile))
                {
                    var lines = await File.ReadAllLinesAsync(logFile);
                    var recentLines = lines.TakeLast(50);
                    foreach (var line in recentLines)
                    {
                        report.AppendLine(line);
                    }
                }
                else
                {
                    report.AppendLine("  No log file found for today");
                }
            }
            catch (Exception ex)
            {
                report.AppendLine($"  Error reading logs: {ex.Message}");
            }
            report.AppendLine();

            report.AppendLine("=== End of Diagnostic Report ===");

            await File.WriteAllTextAsync(reportPath, report.ToString());
            _logger.Information("Diagnostic report generated: {ReportPath}", reportPath);

            return reportPath;
        }

        private static bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Circuit breaker pattern implementation
    /// </summary>
    public class CircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly int _timeoutSeconds;
        private int _failureCount;
        private DateTime _lastFailureTime;
        private CircuitState _state;
        private readonly object _lock = new object();

        public CircuitBreaker(int failureThreshold, int timeoutSeconds)
        {
            _failureThreshold = failureThreshold;
            _timeoutSeconds = timeoutSeconds;
            _failureCount = 0;
            _state = CircuitState.Closed;
        }

        public bool CanExecute()
        {
            lock (_lock)
            {
                if (_state == CircuitState.Open)
                {
                    if ((DateTime.UtcNow - _lastFailureTime).TotalSeconds >= _timeoutSeconds)
                    {
                        _state = CircuitState.HalfOpen;
                        return true;
                    }
                    return false;
                }
                return true;
            }
        }

        public void RecordSuccess()
        {
            lock (_lock)
            {
                _failureCount = 0;
                _state = CircuitState.Closed;
            }
        }

        public void RecordFailure()
        {
            lock (_lock)
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;

                if (_failureCount >= _failureThreshold)
                {
                    _state = CircuitState.Open;
                }
            }
        }

        private enum CircuitState
        {
            Closed,  // Normal operation
            Open,    // Failures exceeded, blocking calls
            HalfOpen // Testing if service recovered
        }
    }

    /// <summary>
    /// Error response with user-friendly information
    /// </summary>
    public class ErrorResponse
    {
        public string ErrorCode { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string TechnicalDetails { get; set; } = string.Empty;
        public List<string> Suggestions { get; set; } = new();
        public ErrorSeverity Severity { get; set; }
        public bool IsRecoverable { get; set; }
        public SupportInfo? SupportInfo { get; set; }

        public void DisplayToUser()
        {
            Console.ForegroundColor = Severity switch
            {
                ErrorSeverity.Low => ConsoleColor.Yellow,
                ErrorSeverity.Medium => ConsoleColor.DarkYellow,
                ErrorSeverity.High => ConsoleColor.Red,
                _ => ConsoleColor.Red
            };

            Console.WriteLine($"\n[{ErrorCode}] {Title}");
            Console.ResetColor();
            Console.WriteLine(Message);

            if (Suggestions.Any())
            {
                Console.WriteLine("\nSuggested actions:");
                foreach (var suggestion in Suggestions)
                {
                    Console.WriteLine($"  • {suggestion}");
                }
            }

            if (SupportInfo != null)
            {
                Console.WriteLine($"\nError ID: {SupportInfo.ErrorId}");
                Console.WriteLine($"For support, visit: {SupportInfo.SupportUrl}");
                Console.WriteLine($"Log file: {SupportInfo.LogFilePath}");
            }

            Console.WriteLine();
        }
    }

    public enum ErrorSeverity
    {
        Low,    // Minor issue, operation can continue
        Medium, // Significant issue, operation failed but recoverable
        High    // Critical issue, requires immediate attention
    }

    public class SupportInfo
    {
        public string ErrorId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string ExceptionType { get; set; } = string.Empty;
        public string LogFilePath { get; set; } = string.Empty;
        public string SupportEmail { get; set; } = string.Empty;
        public string SupportUrl { get; set; } = string.Empty;
    }
}
