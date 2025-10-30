using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// Configuration Manager - Centralized configuration management for the application
    /// </summary>
    public class ConfigurationManager
    {
        private static ConfigurationManager _instance;
        private JObject _config;
        private readonly string _configFilePath;

        // Configuration property backing fields
        private bool _autoConnect = true;
        private int _scanInterval = 30;
        private int _connectionTimeout = 30;
        private int _retryAttempts = 3;
        private bool _enableNotifications = true;
        private bool _showSignalBars = true;
        private bool _verboseOutput = false;
        private int _cacheDuration = 30;
        private int _autoCleanupInterval = 60;
        private string _defaultSecurityType = "WPA2PSK";
        private bool _requireAdminPrivileges = true;
        private string _logLevel = "Info";
        private int _maxHistoryEntries = 10;
        private List<string> _preferredNetworks = new List<string>();
        private bool _enableTelemetry = true;
        private bool _performanceMonitoring = true;
        private string _complianceMode = "Standard";
        private int _auditLogRetention = 90;
        private string _defaultLanguage = "en";
        private bool _autoDetectLanguage = true;
        private bool _debugMode = false;
        private bool _developerOptions = false;

        private ConfigurationManager()
        {
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            LoadConfiguration();
        }

        public static ConfigurationManager Instance => _instance ??= new ConfigurationManager();

        #region Configuration Properties

        // Basic Settings
        public bool AutoConnect
        {
            get => _autoConnect;
            set { _autoConnect = value; SaveConfiguration(); }
        }

        public int ScanInterval
        {
            get => _scanInterval;
            set { _scanInterval = Math.Max(10, Math.Min(300, value)); _scanInterval = value; SaveConfiguration(); }
        }

        public int ConnectionTimeout
        {
            get => _connectionTimeout;
            set { _connectionTimeout = Math.Max(10, Math.Min(120, value)); _connectionTimeout = value; SaveConfiguration(); }
        }

        public int RetryAttempts
        {
            get => _retryAttempts;
            set { _retryAttempts = Math.Max(1, Math.Min(10, value)); _retryAttempts = value; SaveConfiguration(); }
        }

        // UI Settings
        public bool EnableNotifications
        {
            get => _enableNotifications;
            set { _enableNotifications = value; SaveConfiguration(); }
        }

        public bool ShowSignalBars
        {
            get => _showSignalBars;
            set { _showSignalBars = value; SaveConfiguration(); }
        }

        public bool VerboseOutput
        {
            get => _verboseOutput;
            set { _verboseOutput = value; SaveConfiguration(); }
        }

        // Performance Settings
        public int CacheDuration
        {
            get => _cacheDuration;
            set { _cacheDuration = Math.Max(10, Math.Min(300, value)); _cacheDuration = value; SaveConfiguration(); }
        }

        public int AutoCleanupInterval
        {
            get => _autoCleanupInterval;
            set { _autoCleanupInterval = Math.Max(30, Math.Min(1440, value)); _autoCleanupInterval = value; SaveConfiguration(); }
        }

        // Security Settings
        public string DefaultSecurityType
        {
            get => _defaultSecurityType;
            set { _defaultSecurityType = value; SaveConfiguration(); }
        }

        public bool RequireAdminPrivileges
        {
            get => _requireAdminPrivileges;
            set { _requireAdminPrivileges = value; SaveConfiguration(); }
        }

        // Logging Settings
        public string LogLevel
        {
            get => _logLevel;
            set { _logLevel = value; SaveConfiguration(); }
        }

        public int MaxHistoryEntries
        {
            get => _maxHistoryEntries;
            set { _maxHistoryEntries = Math.Max(5, Math.Min(50, value)); _maxHistoryEntries = value; SaveConfiguration(); }
        }

        // Network Settings
        public List<string> PreferredNetworks
        {
            get => _preferredNetworks;
            set { _preferredNetworks = value ?? new List<string>(); SaveConfiguration(); }
        }

        // Monitoring Settings
        public bool EnableTelemetry
        {
            get => _enableTelemetry;
            set { _enableTelemetry = value; SaveConfiguration(); }
        }

        public bool PerformanceMonitoring
        {
            get => _performanceMonitoring;
            set { _performanceMonitoring = value; SaveConfiguration(); }
        }

        // Compliance Settings
        public string ComplianceMode
        {
            get => _complianceMode;
            set { _complianceMode = value; SaveConfiguration(); }
        }

        public int AuditLogRetention
        {
            get => _auditLogRetention;
            set { _auditLogRetention = Math.Max(30, Math.Min(365, value)); _auditLogRetention = value; SaveConfiguration(); }
        }

        // Internationalization Settings
        public string DefaultLanguage
        {
            get => _defaultLanguage;
            set { _defaultLanguage = value; SaveConfiguration(); }
        }

        public bool AutoDetectLanguage
        {
            get => _autoDetectLanguage;
            set { _autoDetectLanguage = value; SaveConfiguration(); }
        }

        // Debug Settings
        public bool DebugMode
        {
            get => _debugMode;
            set { _debugMode = value; SaveConfiguration(); }
        }

        public bool DeveloperOptions
        {
            get => _developerOptions;
            set { _developerOptions = value; SaveConfiguration(); }
        }

        #endregion

        #region Configuration Management

        /// <summary>
        /// Load configuration from file
        /// </summary>
        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string jsonContent = File.ReadAllText(_configFilePath);
                    _config = JObject.Parse(jsonContent);

                    // Load all configuration values with validation
                    _autoConnect = _config.Value<bool>("AutoConnect");
                    _scanInterval = Math.Max(10, Math.Min(300, _config.Value<int>("ScanInterval")));
                    _connectionTimeout = Math.Max(10, Math.Min(120, _config.Value<int>("ConnectionTimeout")));
                    _retryAttempts = Math.Max(1, Math.Min(10, _config.Value<int>("RetryAttempts")));
                    _enableNotifications = _config.Value<bool>("EnableNotifications");
                    _showSignalBars = _config.Value<bool>("ShowSignalBars");
                    _verboseOutput = _config.Value<bool>("VerboseOutput");
                    _cacheDuration = Math.Max(10, Math.Min(300, _config.Value<int>("CacheDuration")));
                    _autoCleanupInterval = Math.Max(30, Math.Min(1440, _config.Value<int>("AutoCleanupInterval")));
                    _defaultSecurityType = _config.Value<string>("DefaultSecurityType") ?? "WPA2PSK";
                    _requireAdminPrivileges = _config.Value<bool>("RequireAdminPrivileges");
                    _logLevel = _config.Value<string>("LogLevel") ?? "Info";
                    _maxHistoryEntries = Math.Max(5, Math.Min(50, _config.Value<int>("MaxHistoryEntries")));
                    _preferredNetworks = _config["PreferredNetworks"]?.ToObject<List<string>>() ?? new List<string>();
                    _enableTelemetry = _config.Value<bool>("EnableTelemetry");
                    _performanceMonitoring = _config.Value<bool>("PerformanceMonitoring");
                    _complianceMode = _config.Value<string>("ComplianceMode") ?? "Standard";
                    _auditLogRetention = Math.Max(30, Math.Min(365, _config.Value<int>("AuditLogRetention")));
                    _defaultLanguage = _config.Value<string>("DefaultLanguage") ?? "en";
                    _autoDetectLanguage = _config.Value<bool>("AutoDetectLanguage");
                    _debugMode = _config.Value<bool>("DebugMode");
                    _developerOptions = _config.Value<bool>("DeveloperOptions");
                }
                else
                {
                    // Create default configuration file
                    SaveConfiguration();
                }
            }
            catch (Exception ex)
            {
                // If loading fails, use defaults and try to save
                System.Diagnostics.Debug.WriteLine($"Configuration loading error: {ex.Message}");
                SaveConfiguration();
            }
        }

        /// <summary>
        /// Save configuration to file
        /// </summary>
        private void SaveConfiguration()
        {
            try
            {
                _config = new JObject
                {
                    // Basic Settings
                    ["AutoConnect"] = _autoConnect,
                    ["ScanInterval"] = _scanInterval,
                    ["ConnectionTimeout"] = _connectionTimeout,
                    ["RetryAttempts"] = _retryAttempts,

                    // UI Settings
                    ["EnableNotifications"] = _enableNotifications,
                    ["ShowSignalBars"] = _showSignalBars,
                    ["VerboseOutput"] = _verboseOutput,

                    // Performance Settings
                    ["CacheDuration"] = _cacheDuration,
                    ["AutoCleanupInterval"] = _autoCleanupInterval,

                    // Security Settings
                    ["DefaultSecurityType"] = _defaultSecurityType,
                    ["RequireAdminPrivileges"] = _requireAdminPrivileges,

                    // Logging Settings
                    ["LogLevel"] = _logLevel,
                    ["MaxHistoryEntries"] = _maxHistoryEntries,

                    // Network Settings
                    ["PreferredNetworks"] = new JArray(_preferredNetworks),

                    // Monitoring Settings
                    ["EnableTelemetry"] = _enableTelemetry,
                    ["PerformanceMonitoring"] = _performanceMonitoring,

                    // Compliance Settings
                    ["ComplianceMode"] = _complianceMode,
                    ["AuditLogRetention"] = _auditLogRetention,

                    // Internationalization Settings
                    ["DefaultLanguage"] = _defaultLanguage,
                    ["AutoDetectLanguage"] = _autoDetectLanguage,

                    // Debug Settings
                    ["DebugMode"] = _debugMode,
                    ["DeveloperOptions"] = _developerOptions
                };

                string formattedJson = _config.ToString(Formatting.Indented);
                File.WriteAllText(_configFilePath, formattedJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Configuration saving error: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Reset all settings to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            _autoConnect = true;
            _scanInterval = 30;
            _connectionTimeout = 30;
            _retryAttempts = 3;
            _enableNotifications = true;
            _showSignalBars = true;
            _verboseOutput = false;
            _cacheDuration = 30;
            _autoCleanupInterval = 60;
            _defaultSecurityType = "WPA2PSK";
            _requireAdminPrivileges = true;
            _logLevel = "Info";
            _maxHistoryEntries = 10;
            _preferredNetworks = new List<string>();
            _enableTelemetry = true;
            _performanceMonitoring = true;
            _complianceMode = "Standard";
            _auditLogRetention = 90;
            _defaultLanguage = "en";
            _autoDetectLanguage = true;
            _debugMode = false;
            _developerOptions = false;

            SaveConfiguration();
        }

        /// <summary>
        /// Get configuration as dictionary for UI binding
        /// </summary>
        public Dictionary<string, object> GetConfigurationDictionary()
        {
            return new Dictionary<string, object>
            {
                ["AutoConnect"] = _autoConnect,
                ["ScanInterval"] = _scanInterval,
                ["ConnectionTimeout"] = _connectionTimeout,
                ["RetryAttempts"] = _retryAttempts,
                ["EnableNotifications"] = _enableNotifications,
                ["ShowSignalBars"] = _showSignalBars,
                ["VerboseOutput"] = _verboseOutput,
                ["CacheDuration"] = _cacheDuration,
                ["AutoCleanupInterval"] = _autoCleanupInterval,
                ["DefaultSecurityType"] = _defaultSecurityType,
                ["RequireAdminPrivileges"] = _requireAdminPrivileges,
                ["LogLevel"] = _logLevel,
                ["MaxHistoryEntries"] = _maxHistoryEntries,
                ["PreferredNetworks"] = _preferredNetworks,
                ["EnableTelemetry"] = _enableTelemetry,
                ["PerformanceMonitoring"] = _performanceMonitoring,
                ["ComplianceMode"] = _complianceMode,
                ["AuditLogRetention"] = _auditLogRetention,
                ["DefaultLanguage"] = _defaultLanguage,
                ["AutoDetectLanguage"] = _autoDetectLanguage,
                ["DebugMode"] = _debugMode,
                ["DeveloperOptions"] = _developerOptions
            };
        }

        /// <summary>
        /// Load configuration from dictionary (for UI updates)
        /// </summary>
        public void LoadFromDictionary(Dictionary<string, object> configDict)
        {
            foreach (var kvp in configDict)
            {
                switch (kvp.Key)
                {
                    case "AutoConnect": _autoConnect = (bool)kvp.Value; break;
                    case "ScanInterval": _scanInterval = (int)kvp.Value; break;
                    case "ConnectionTimeout": _connectionTimeout = (int)kvp.Value; break;
                    case "RetryAttempts": _retryAttempts = (int)kvp.Value; break;
                    case "EnableNotifications": _enableNotifications = (bool)kvp.Value; break;
                    case "ShowSignalBars": _showSignalBars = (bool)kvp.Value; break;
                    case "VerboseOutput": _verboseOutput = (bool)kvp.Value; break;
                    case "CacheDuration": _cacheDuration = (int)kvp.Value; break;
                    case "AutoCleanupInterval": _autoCleanupInterval = (int)kvp.Value; break;
                    case "DefaultSecurityType": _defaultSecurityType = (string)kvp.Value; break;
                    case "RequireAdminPrivileges": _requireAdminPrivileges = (bool)kvp.Value; break;
                    case "LogLevel": _logLevel = (string)kvp.Value; break;
                    case "MaxHistoryEntries": _maxHistoryEntries = (int)kvp.Value; break;
                    case "PreferredNetworks": _preferredNetworks = (List<string>)kvp.Value; break;
                    case "EnableTelemetry": _enableTelemetry = (bool)kvp.Value; break;
                    case "PerformanceMonitoring": _performanceMonitoring = (bool)kvp.Value; break;
                    case "ComplianceMode": _complianceMode = (string)kvp.Value; break;
                    case "AuditLogRetention": _auditLogRetention = (int)kvp.Value; break;
                    case "DefaultLanguage": _defaultLanguage = (string)kvp.Value; break;
                    case "AutoDetectLanguage": _autoDetectLanguage = (bool)kvp.Value; break;
                    case "DebugMode": _debugMode = (bool)kvp.Value; break;
                    case "DeveloperOptions": _developerOptions = (bool)kvp.Value; break;
                }
            }
            SaveConfiguration();
        }

        #endregion
    }
}
