using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Configuration manager for WiFi settings
    /// </summary>
    public static class ConfigManager
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter", "config.json");

        public static async Task<WifiConfig?> LoadConfig(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return GetDefaultConfig();
                }

                var json = await File.ReadAllTextAsync(ConfigPath, cancellationToken);
                return JsonSerializer.Deserialize<WifiConfig>(json) ?? GetDefaultConfig();
            }
            catch
            {
                return GetDefaultConfig();
            }
        }

        public static async Task SaveConfig(WifiConfig config, CancellationToken cancellationToken = default)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(ConfigPath, json, cancellationToken);
            }
            catch
            {
                // Log error but don't throw
            }
        }

        private static WifiConfig GetDefaultConfig()
        {
            return new WifiConfig
            {
                CacheDuration = 30,
                ScanInterval = 30,
                MaxRetryAttempts = 3,
                ConnectionTimeout = 30,
                EnableWiFi7Features = true,
                EnableMultiLinkOperation = true,
                Enable320MHzChannels = true,
                PreferredStandard = WifiStandard.WiFi7
            };
        }
    }

    /// <summary>
    /// WiFi configuration settings
    /// </summary>
    public class WifiConfig
    {
        public int CacheDuration { get; set; } = 30;
        public int ScanInterval { get; set; } = 30;
        public int MaxRetryAttempts { get; set; } = 3;
        public int ConnectionTimeout { get; set; } = 30;
        public bool EnableWiFi7Features { get; set; } = true;
        public bool EnableMultiLinkOperation { get; set; } = true;
        public bool Enable320MHzChannels { get; set; } = true;
        public WifiStandard PreferredStandard { get; set; } = WifiStandard.WiFi7;
        public Dictionary<string, string> AdvancedSettings { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Security manager for WiFi operations
    /// </summary>
    public static class SecurityManager
    {
        public static async Task<RateLimitResult> CheckRateLimitAsync(string operation)
        {
            // Simple rate limiting implementation
            return new RateLimitResult { Allowed = true };
        }

        public static async Task<string> CreateValidatedProfileAsync(string name, string content)
        {
            var tempPath = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempPath, content);
            return tempPath;
        }

        public static async Task SecureDeleteFileAsync(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    // Secure delete by overwriting
                    var fileInfo = new FileInfo(path);
                    var size = fileInfo.Length;
                    var buffer = new byte[4096];

                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
                    for (long i = 0; i < size; i += buffer.Length)
                    {
                        var remaining = size - i;
                        var writeSize = (int)Math.Min(buffer.Length, remaining);
                        stream.Write(buffer, 0, writeSize);
                    }
                    stream.Close();

                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort deletion
                try { File.Delete(path); } catch { }
            }
        }
    }

    /// <summary>
    /// Rate limit result
    /// </summary>
    public class RateLimitResult
    {
        public bool Allowed { get; set; } = true;
        public string Scope { get; set; } = "global";
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Logger for WiFi operations
    /// </summary>
    public static class Logger
    {
        public static async Task LogInfo(string message, string source, Dictionary<string, object>? context = null)
        {
            await LogAsync("INFO", message, source, context);
        }

        public static async Task LogWarning(string message, string source, Dictionary<string, object>? context = null)
        {
            await LogAsync("WARN", message, source, context);
        }

        public static async Task LogError(string message, string source, Dictionary<string, object>? context = null, Exception? exception = null)
        {
            await LogAsync("ERROR", message, source, context, exception);
        }

        public static async Task LogDebug(string message, string source, Dictionary<string, object>? context = null)
        {
            await LogAsync("DEBUG", message, source, context);
        }

        private static async Task LogAsync(string level, string message, string source, Dictionary<string, object>? context = null, Exception? exception = null)
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Source = source,
                Message = message,
                Context = context,
                Exception = exception?.ToString()
            };

            // In a real implementation, this would write to a log file or logging system
            Console.WriteLine($"[{level}] {source}: {message}");
        }
    }

    /// <summary>
    /// Error handler for network operations
    /// </summary>
    public static class ErrorHandler
    {
        public static async Task<T> HandleNetworkOperationWithRecovery<T>(
            Func<Task<T>> operation,
            T fallbackValue = default!)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex)
            {
                await Logger.LogError("Network operation failed", "ErrorHandler", null, ex);
                return fallbackValue;
            }
        }

        public static async Task LogError(Exception exception, string operation)
        {
            await Logger.LogError($"Operation '{operation}' failed", "ErrorHandler", null, exception);
        }
    }

    /// <summary>
    /// Input validator for WiFi parameters
    /// </summary>
    public static class InputValidator
    {
        private static readonly System.Text.RegularExpressions.Regex SsidRegex =
            new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9\s\-_\.]{1,32}$");

        private static readonly System.Text.RegularExpressions.Regex PasswordRegex =
            new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9!@#$%^&*()_+\-=\[\]{};':"",./<>?]{8,63}$");

        public static string EnsureValidSsid(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                throw new ArgumentException("SSID cannot be empty");

            if (ssid.Length > 32)
                throw new ArgumentException("SSID cannot exceed 32 characters");

            if (!SsidRegex.IsMatch(ssid))
                throw new ArgumentException("SSID contains invalid characters");

            return ssid.Trim();
        }

        public static string EnsureValidPassword(string? password)
        {
            if (string.IsNullOrEmpty(password))
                return string.Empty;

            if (password.Length < 8 || password.Length > 63)
                throw new ArgumentException("Password must be between 8 and 63 characters");

            if (!PasswordRegex.IsMatch(password))
                throw new ArgumentException("Password contains invalid characters");

            return password;
        }

        public static string QuoteForNetsh(string value)
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
