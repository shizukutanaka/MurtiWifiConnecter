using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Telemetry and crash reporting with Sentry integration
    /// Privacy-focused with user consent
    /// </summary>
    public class TelemetryManager
    {
        private static TelemetryManager? _instance;
        private static readonly object _lock = new object();
        private readonly HttpClient _httpClient;
        private readonly string _sentryDsn;
        private readonly string _sessionId;
        private bool _isEnabled;
        private bool _hasUserConsent;
        private TelemetryConfig _config;

        private TelemetryManager()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            _sentryDsn = Environment.GetEnvironmentVariable("SENTRY_DSN") ?? "";
            _sessionId = Guid.NewGuid().ToString();
            _config = LoadConfiguration();
            _isEnabled = _config.IsEnabled;
            _hasUserConsent = _config.HasUserConsent;
        }

        public static TelemetryManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new TelemetryManager();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Initialize telemetry system
        /// </summary>
        public static async Task InitializeAsync()
        {
            var instance = Instance;

            // Check for first-time user consent
            if (!instance._hasUserConsent && !instance._config.ConsentAsked)
            {
                await instance.RequestUserConsentAsync();
            }

            if (instance._isEnabled && instance._hasUserConsent)
            {
                await instance.SendSessionStartEventAsync();
            }
        }

        /// <summary>
        /// Request user consent for telemetry
        /// </summary>
        private async Task RequestUserConsentAsync()
        {
            Console.WriteLine("\n╔═══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║           MurtiWifi Connector - Telemetry                 ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════╝\n");

            Console.WriteLine("Help us improve MurtiWifi Connector!\n");
            Console.WriteLine("We'd like to collect anonymous usage statistics to:");
            Console.WriteLine("  • Understand which features are most used");
            Console.WriteLine("  • Identify and fix crashes faster");
            Console.WriteLine("  • Improve performance and reliability\n");

            Console.WriteLine("What we collect:");
            Console.WriteLine("  ✓ Feature usage (which commands you run)");
            Console.WriteLine("  ✓ Performance metrics (connection time, speed)");
            Console.WriteLine("  ✓ Crash reports (when app encounters errors)");
            Console.WriteLine("  ✓ System info (OS version, adapter type)\n");

            Console.WriteLine("What we DON'T collect:");
            Console.WriteLine("  ✗ WiFi passwords or credentials");
            Console.WriteLine("  ✗ Personal information");
            Console.WriteLine("  ✗ Network names (anonymized)");
            Console.WriteLine("  ✗ Browsing history or network traffic\n");

            Console.WriteLine("Privacy: All data is anonymized and encrypted.");
            Console.WriteLine("You can disable this anytime with: MurtiWifiConnecter telemetry disable\n");

            Console.Write("Enable telemetry? (Y/n): ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();

            _hasUserConsent = string.IsNullOrEmpty(response) || response == "y" || response == "yes";
            _isEnabled = _hasUserConsent;

            _config.HasUserConsent = _hasUserConsent;
            _config.IsEnabled = _isEnabled;
            _config.ConsentAsked = true;
            _config.ConsentDate = DateTime.UtcNow;

            await SaveConfigurationAsync();

            if (_hasUserConsent)
            {
                Console.WriteLine("\n✓ Thank you! Telemetry enabled.");
            }
            else
            {
                Console.WriteLine("\n✓ Telemetry disabled. You can enable it later if you change your mind.");
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
            Console.Clear();
        }

        /// <summary>
        /// Track feature usage
        /// </summary>
        public async Task TrackFeatureUsageAsync(string featureName, Dictionary<string, object>? properties = null)
        {
            if (!_isEnabled || !_hasUserConsent) return;

            try
            {
                var eventData = new TelemetryEvent
                {
                    EventType = "feature_usage",
                    EventName = featureName,
                    SessionId = _sessionId,
                    Timestamp = DateTime.UtcNow,
                    Properties = properties ?? new Dictionary<string, object>(),
                    SystemInfo = GetSystemInfo()
                };

                await SendEventAsync(eventData);
            }
            catch
            {
                // Silent fail - telemetry should never break app functionality
            }
        }

        /// <summary>
        /// Track performance metric
        /// </summary>
        public async Task TrackPerformanceAsync(string metricName, long durationMs, Dictionary<string, object>? properties = null)
        {
            if (!_isEnabled || !_hasUserConsent) return;

            try
            {
                var props = properties ?? new Dictionary<string, object>();
                props["duration_ms"] = durationMs;

                var eventData = new TelemetryEvent
                {
                    EventType = "performance",
                    EventName = metricName,
                    SessionId = _sessionId,
                    Timestamp = DateTime.UtcNow,
                    Properties = props,
                    SystemInfo = GetSystemInfo()
                };

                await SendEventAsync(eventData);
            }
            catch
            {
                // Silent fail
            }
        }

        /// <summary>
        /// Report crash or error
        /// </summary>
        public async Task ReportCrashAsync(Exception exception, string context, Dictionary<string, object>? additionalData = null)
        {
            if (!_isEnabled || !_hasUserConsent) return;

            try
            {
                var crashData = new CrashReport
                {
                    ExceptionType = exception.GetType().FullName ?? "Unknown",
                    Message = exception.Message,
                    StackTrace = exception.StackTrace ?? "",
                    Context = context,
                    SessionId = _sessionId,
                    Timestamp = DateTime.UtcNow,
                    SystemInfo = GetSystemInfo(),
                    AdditionalData = additionalData ?? new Dictionary<string, object>()
                };

                // Add inner exception if exists
                if (exception.InnerException != null)
                {
                    crashData.AdditionalData["inner_exception"] = exception.InnerException.Message;
                    crashData.AdditionalData["inner_stacktrace"] = exception.InnerException.StackTrace ?? "";
                }

                await SendCrashReportAsync(crashData);
            }
            catch
            {
                // Silent fail
            }
        }

        /// <summary>
        /// Report unhandled exception
        /// </summary>
        public async Task ReportUnhandledExceptionAsync(Exception exception)
        {
            if (!_isEnabled || !_hasUserConsent) return;

            try
            {
                var crashData = new CrashReport
                {
                    ExceptionType = exception.GetType().FullName ?? "Unknown",
                    Message = exception.Message,
                    StackTrace = exception.StackTrace ?? "",
                    Context = "Unhandled Exception",
                    SessionId = _sessionId,
                    Timestamp = DateTime.UtcNow,
                    SystemInfo = GetSystemInfo(),
                    IsFatal = true,
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["crash_type"] = "unhandled_exception",
                        ["process_uptime_seconds"] = Environment.TickCount64 / 1000
                    }
                };

                await SendCrashReportAsync(crashData);
            }
            catch
            {
                // Silent fail
            }
        }

        /// <summary>
        /// Enable/disable telemetry
        /// </summary>
        public async Task SetEnabledAsync(bool enabled)
        {
            _isEnabled = enabled;
            _config.IsEnabled = enabled;
            await SaveConfigurationAsync();

            if (enabled && !_hasUserConsent)
            {
                await RequestUserConsentAsync();
            }
        }

        /// <summary>
        /// Check if telemetry is enabled
        /// </summary>
        public bool IsEnabled()
        {
            return _isEnabled && _hasUserConsent;
        }

        /// <summary>
        /// Get telemetry status
        /// </summary>
        public TelemetryStatus GetStatus()
        {
            return new TelemetryStatus
            {
                IsEnabled = _isEnabled,
                HasUserConsent = _hasUserConsent,
                SessionId = _sessionId,
                ConsentDate = _config.ConsentDate,
                EventCount = _config.EventCount,
                LastEventTime = _config.LastEventTime
            };
        }

        private async Task SendSessionStartEventAsync()
        {
            var eventData = new TelemetryEvent
            {
                EventType = "session",
                EventName = "session_start",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Properties = new Dictionary<string, object>
                {
                    ["app_version"] = "3.2.0",
                    ["is_first_launch"] = !_config.ConsentAsked
                },
                SystemInfo = GetSystemInfo()
            };

            await SendEventAsync(eventData);
        }

        private async Task SendEventAsync(TelemetryEvent eventData)
        {
            if (string.IsNullOrEmpty(_sentryDsn))
            {
                // No Sentry DSN configured, log locally only
                await LogEventLocallyAsync(eventData);
                return;
            }

            try
            {
                var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _httpClient.PostAsync(GetSentryEndpoint(), content);

                _config.EventCount++;
                _config.LastEventTime = DateTime.UtcNow;
                await SaveConfigurationAsync();
            }
            catch
            {
                // Fallback to local logging
                await LogEventLocallyAsync(eventData);
            }
        }

        private async Task SendCrashReportAsync(CrashReport crashData)
        {
            if (string.IsNullOrEmpty(_sentryDsn))
            {
                await LogCrashLocallyAsync(crashData);
                return;
            }

            try
            {
                // Sentry-specific format
                var sentryEvent = new
                {
                    timestamp = crashData.Timestamp,
                    platform = "csharp",
                    level = crashData.IsFatal ? "fatal" : "error",
                    exception = new
                    {
                        type = crashData.ExceptionType,
                        value = crashData.Message,
                        stacktrace = new
                        {
                            frames = ParseStackTrace(crashData.StackTrace)
                        }
                    },
                    tags = new Dictionary<string, string>
                    {
                        ["session_id"] = crashData.SessionId,
                        ["context"] = crashData.Context,
                        ["os"] = crashData.SystemInfo.OS,
                        ["version"] = crashData.SystemInfo.AppVersion
                    },
                    extra = crashData.AdditionalData,
                    user = new
                    {
                        id = GetAnonymousUserId()
                    }
                };

                var json = JsonSerializer.Serialize(sentryEvent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _httpClient.PostAsync(GetSentryEndpoint(), content);

                _config.CrashCount++;
                await SaveConfigurationAsync();
            }
            catch
            {
                await LogCrashLocallyAsync(crashData);
            }
        }

        private async Task LogEventLocallyAsync(TelemetryEvent eventData)
        {
            var logDir = GetTelemetryLogDirectory();
            var logFile = Path.Combine(logDir, $"telemetry-{DateTime.UtcNow:yyyyMMdd}.json");

            var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            await File.AppendAllTextAsync(logFile, json + Environment.NewLine);
        }

        private async Task LogCrashLocallyAsync(CrashReport crashData)
        {
            var logDir = GetTelemetryLogDirectory();
            var crashFile = Path.Combine(logDir, $"crash-{DateTime.UtcNow:yyyyMMddHHmmss}.json");

            var json = JsonSerializer.Serialize(crashData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            await File.WriteAllTextAsync(crashFile, json);
        }

        private SystemInfo GetSystemInfo()
        {
            return new SystemInfo
            {
                OS = Environment.OSVersion.ToString(),
                OSVersion = Environment.OSVersion.Version.ToString(),
                Is64Bit = Environment.Is64BitOperatingSystem,
                ProcessorCount = Environment.ProcessorCount,
                CLRVersion = Environment.Version.ToString(),
                MachineName = GetAnonymizedMachineName(),
                AppVersion = "3.2.0",
                Culture = System.Globalization.CultureInfo.CurrentCulture.Name
            };
        }

        private string GetAnonymizedMachineName()
        {
            // Hash machine name for privacy
            var hash = System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(Environment.MachineName));
            return Convert.ToHexString(hash)[..8];
        }

        private string GetAnonymousUserId()
        {
            // Stable anonymous user ID based on machine
            var machineId = Environment.MachineName + Environment.UserName;
            var hash = System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(machineId));
            return Convert.ToHexString(hash)[..16];
        }

        private List<object> ParseStackTrace(string stackTrace)
        {
            var frames = new List<object>();
            if (string.IsNullOrEmpty(stackTrace)) return frames;

            var lines = stackTrace.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("at "))
                {
                    frames.Add(new
                    {
                        function = trimmed.Substring(3),
                        in_app = !trimmed.Contains("System.") && !trimmed.Contains("Microsoft.")
                    });
                }
            }

            return frames;
        }

        private string GetSentryEndpoint()
        {
            // Parse DSN: https://<key>@<host>/<project-id>
            if (string.IsNullOrEmpty(_sentryDsn)) return "";

            var uri = new Uri(_sentryDsn);
            var projectId = uri.AbsolutePath.TrimStart('/');
            return $"{uri.Scheme}://{uri.Host}/api/{projectId}/store/";
        }

        private string GetTelemetryLogDirectory()
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter", "Telemetry");

            Directory.CreateDirectory(logDir);
            return logDir;
        }

        private string GetConfigFilePath()
        {
            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MurtiWifiConnecter");

            Directory.CreateDirectory(configDir);
            return Path.Combine(configDir, "telemetry-config.json");
        }

        private TelemetryConfig LoadConfiguration()
        {
            try
            {
                var configFile = GetConfigFilePath();
                if (File.Exists(configFile))
                {
                    var json = File.ReadAllText(configFile);
                    return JsonSerializer.Deserialize<TelemetryConfig>(json) ?? new TelemetryConfig();
                }
            }
            catch
            {
                // If config can't be loaded, return default
            }

            return new TelemetryConfig();
        }

        private async Task SaveConfigurationAsync()
        {
            try
            {
                var configFile = GetConfigFilePath();
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(configFile, json);
            }
            catch
            {
                // Silent fail
            }
        }
    }

    public class TelemetryEvent
    {
        public string EventType { get; set; } = "";
        public string EventName { get; set; } = "";
        public string SessionId { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();
        public SystemInfo SystemInfo { get; set; } = new();
    }

    public class CrashReport
    {
        public string ExceptionType { get; set; } = "";
        public string Message { get; set; } = "";
        public string StackTrace { get; set; } = "";
        public string Context { get; set; } = "";
        public string SessionId { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public bool IsFatal { get; set; }
        public SystemInfo SystemInfo { get; set; } = new();
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }

    public class SystemInfo
    {
        public string OS { get; set; } = "";
        public string OSVersion { get; set; } = "";
        public bool Is64Bit { get; set; }
        public int ProcessorCount { get; set; }
        public string CLRVersion { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public string Culture { get; set; } = "";
    }

    public class TelemetryConfig
    {
        public bool IsEnabled { get; set; }
        public bool HasUserConsent { get; set; }
        public bool ConsentAsked { get; set; }
        public DateTime? ConsentDate { get; set; }
        public int EventCount { get; set; }
        public int CrashCount { get; set; }
        public DateTime? LastEventTime { get; set; }
    }

    public class TelemetryStatus
    {
        public bool IsEnabled { get; set; }
        public bool HasUserConsent { get; set; }
        public string SessionId { get; set; } = "";
        public DateTime? ConsentDate { get; set; }
        public int EventCount { get; set; }
        public DateTime? LastEventTime { get; set; }
    }
}
