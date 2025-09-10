using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace MurtiWifiConnecter.Infrastructure.Configuration
{
    /// <summary>
    /// 設定管理インターフェース
    /// </summary>
    public interface IConfigurationManager
    {
        T GetValue<T>(string key, T defaultValue = default);
        void SetValue<T>(string key, T value);
        bool HasKey(string key);
        void RemoveKey(string key);
        Task SaveAsync();
        Task LoadAsync();
        void RegisterChangeCallback(Action<string, object> callback);
        void UnregisterChangeCallback(Action<string, object> callback);
        Task<bool> ValidateConfigurationAsync();
        void ResetToDefaults();
        T GetSection<T>(string sectionName) where T : class, new();
        void SetSection<T>(string sectionName, T section) where T : class;
    }

    /// <summary>
    /// 設定管理クラス
    /// </summary>
    public class ConfigurationManager : IConfigurationManager
    {
        private readonly Dictionary<string, object> _configuration;
        private readonly string _configFilePath;
        private readonly List<Action<string, object>> _changeCallbacks;
        private readonly object _lockObject = new object();
        private DateTime _lastModified;

        public ConfigurationManager(string configFilePath = "config.json")
        {
            _configuration = new Dictionary<string, object>();
            _configFilePath = configFilePath;
            _changeCallbacks = new List<Action<string, object>>();
            _lastModified = DateTime.MinValue;

            InitializeDefaults();
        }

        /// <summary>
        /// 設定値を取得
        /// </summary>
        public T GetValue<T>(string key, T defaultValue = default)
        {
            lock (_lockObject)
            {
                if (_configuration.TryGetValue(key, out var value))
                {
                    try
                    {
                        if (value is JsonElement jsonElement)
                        {
                            return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
                        }
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    catch
                    {
                        return defaultValue;
                    }
                }
                return defaultValue;
            }
        }

        /// <summary>
        /// 設定値を設定
        /// </summary>
        public void SetValue<T>(string key, T value)
        {
            lock (_lockObject)
            {
                var oldValue = _configuration.GetValueOrDefault(key);
                _configuration[key] = value;

                // 変更通知
                NotifyChange(key, value);
            }
        }

        /// <summary>
        /// キーが存在するかチェック
        /// </summary>
        public bool HasKey(string key)
        {
            lock (_lockObject)
            {
                return _configuration.ContainsKey(key);
            }
        }

        /// <summary>
        /// キーを削除
        /// </summary>
        public void RemoveKey(string key)
        {
            lock (_lockObject)
            {
                if (_configuration.Remove(key))
                {
                    NotifyChange(key, null);
                }
            }
        }

        /// <summary>
        /// 設定をファイルに保存
        /// </summary>
        public async Task SaveAsync()
        {
            Dictionary<string, object> configCopy;
            
            lock (_lockObject)
            {
                configCopy = new Dictionary<string, object>(_configuration);
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(configCopy, options);
                await File.WriteAllTextAsync(_configFilePath, json);
                _lastModified = File.GetLastWriteTime(_configFilePath);
            }
            catch (Exception ex)
            {
                throw new ConfigurationException($"Failed to save configuration: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 設定をファイルから読み込み
        /// </summary>
        public async Task LoadAsync()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = await File.ReadAllTextAsync(_configFilePath);
                    var loadedConfig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                    lock (_lockObject)
                    {
                        _configuration.Clear();
                        foreach (var kvp in loadedConfig)
                        {
                            _configuration[kvp.Key] = kvp.Value;
                        }
                    }

                    _lastModified = File.GetLastWriteTime(_configFilePath);
                }
                else
                {
                    // デフォルト設定で初期化
                    InitializeDefaults();
                    await SaveAsync();
                }
            }
            catch (Exception ex)
            {
                throw new ConfigurationException($"Failed to load configuration: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 変更コールバックを登録
        /// </summary>
        public void RegisterChangeCallback(Action<string, object> callback)
        {
            if (callback != null)
            {
                lock (_lockObject)
                {
                    _changeCallbacks.Add(callback);
                }
            }
        }

        /// <summary>
        /// 変更コールバックの登録を解除
        /// </summary>
        public void UnregisterChangeCallback(Action<string, object> callback)
        {
            if (callback != null)
            {
                lock (_lockObject)
                {
                    _changeCallbacks.Remove(callback);
                }
            }
        }

        /// <summary>
        /// 設定を検証
        /// </summary>
        public async Task<bool> ValidateConfigurationAsync()
        {
            try
            {
                var appConfig = GetSection<ApplicationConfiguration>("Application");
                var networkConfig = GetSection<NetworkConfiguration>("Network");
                var uiConfig = GetSection<UIConfiguration>("UI");

                var validationResults = new List<ValidationResult>();
                var context = new ValidationContext(appConfig);

                // Application設定の検証
                if (!Validator.TryValidateObject(appConfig, context, validationResults, true))
                {
                    return false;
                }

                // Network設定の検証
                context = new ValidationContext(networkConfig);
                validationResults.Clear();
                if (!Validator.TryValidateObject(networkConfig, context, validationResults, true))
                {
                    return false;
                }

                // UI設定の検証
                context = new ValidationContext(uiConfig);
                validationResults.Clear();
                if (!Validator.TryValidateObject(uiConfig, context, validationResults, true))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// デフォルト設定にリセット
        /// </summary>
        public void ResetToDefaults()
        {
            lock (_lockObject)
            {
                _configuration.Clear();
                InitializeDefaults();
            }
        }

        /// <summary>
        /// セクションを取得
        /// </summary>
        public T GetSection<T>(string sectionName) where T : class, new()
        {
            var section = GetValue<T>(sectionName);
            return section ?? new T();
        }

        /// <summary>
        /// セクションを設定
        /// </summary>
        public void SetSection<T>(string sectionName, T section) where T : class
        {
            SetValue(sectionName, section);
        }

        /// <summary>
        /// デフォルト設定を初期化
        /// </summary>
        private void InitializeDefaults()
        {
            // アプリケーション設定
            SetSection("Application", new ApplicationConfiguration
            {
                AutoStart = false,
                MinimizeToTray = true,
                CheckForUpdates = true,
                LogLevel = "Information",
                Language = "ja-JP",
                Theme = "Light"
            });

            // ネットワーク設定
            SetSection("Network", new NetworkConfiguration
            {
                AutoConnect = true,
                ConnectionTimeout = 30,
                ScanInterval = 30,
                MaxRetries = 3,
                PreferredBand = "Auto"
            });

            // UI設定
            SetSection("UI", new UIConfiguration
            {
                WindowWidth = 800,
                WindowHeight = 600,
                ShowSignalStrength = true,
                ShowSecurityInfo = true,
                RefreshInterval = 5,
                NotificationDuration = 3000
            });

            // セキュリティ設定
            SetSection("Security", new SecurityConfiguration
            {
                SavePasswords = true,
                EncryptPasswords = true,
                RequireConfirmation = true,
                ShowPasswordByDefault = false
            });

            // 詳細設定
            SetSection("Advanced", new AdvancedConfiguration
            {
                EnableTelemetry = true,
                DebugMode = false,
                MaxLogSize = 10,
                BackupCount = 5,
                CacheSize = 100
            });
        }

        /// <summary>
        /// 変更通知
        /// </summary>
        private void NotifyChange(string key, object value)
        {
            var callbacks = new List<Action<string, object>>();
            
            lock (_lockObject)
            {
                callbacks.AddRange(_changeCallbacks);
            }

            foreach (var callback in callbacks)
            {
                try
                {
                    callback(key, value);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Configuration change callback failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// ファイル変更監視（外部変更検出用）
        /// </summary>
        public async Task<bool> CheckForExternalChangesAsync()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var currentModified = File.GetLastWriteTime(_configFilePath);
                    if (currentModified > _lastModified)
                    {
                        await LoadAsync();
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// アプリケーション設定
    /// </summary>
    public class ApplicationConfiguration
    {
        [Required]
        public bool AutoStart { get; set; }

        [Required]
        public bool MinimizeToTray { get; set; }

        [Required]
        public bool CheckForUpdates { get; set; }

        [Required]
        [RegularExpression("^(Debug|Information|Warning|Error|Critical)$")]
        public string LogLevel { get; set; }

        [Required]
        [RegularExpression("^(ja-JP|en-US)$")]
        public string Language { get; set; }

        [Required]
        [RegularExpression("^(Light|Dark|HighContrast)$")]
        public string Theme { get; set; }
    }

    /// <summary>
    /// ネットワーク設定
    /// </summary>
    public class NetworkConfiguration
    {
        [Required]
        public bool AutoConnect { get; set; }

        [Required]
        [Range(5, 300)]
        public int ConnectionTimeout { get; set; }

        [Required]
        [Range(5, 300)]
        public int ScanInterval { get; set; }

        [Required]
        [Range(1, 10)]
        public int MaxRetries { get; set; }

        [Required]
        [RegularExpression("^(Auto|2.4GHz|5GHz)$")]
        public string PreferredBand { get; set; }
    }

    /// <summary>
    /// UI設定
    /// </summary>
    public class UIConfiguration
    {
        [Required]
        [Range(600, 2000)]
        public int WindowWidth { get; set; }

        [Required]
        [Range(400, 1200)]
        public int WindowHeight { get; set; }

        [Required]
        public bool ShowSignalStrength { get; set; }

        [Required]
        public bool ShowSecurityInfo { get; set; }

        [Required]
        [Range(1, 60)]
        public int RefreshInterval { get; set; }

        [Required]
        [Range(1000, 10000)]
        public int NotificationDuration { get; set; }
    }

    /// <summary>
    /// セキュリティ設定
    /// </summary>
    public class SecurityConfiguration
    {
        [Required]
        public bool SavePasswords { get; set; }

        [Required]
        public bool EncryptPasswords { get; set; }

        [Required]
        public bool RequireConfirmation { get; set; }

        [Required]
        public bool ShowPasswordByDefault { get; set; }
    }

    /// <summary>
    /// 詳細設定
    /// </summary>
    public class AdvancedConfiguration
    {
        [Required]
        public bool EnableTelemetry { get; set; }

        [Required]
        public bool DebugMode { get; set; }

        [Required]
        [Range(1, 100)]
        public int MaxLogSize { get; set; }

        [Required]
        [Range(1, 20)]
        public int BackupCount { get; set; }

        [Required]
        [Range(50, 1000)]
        public int CacheSize { get; set; }
    }

    /// <summary>
    /// 設定例外
    /// </summary>
    public class ConfigurationException : Exception
    {
        public ConfigurationException(string message) : base(message) { }
        public ConfigurationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// 設定ヘルパー
    /// </summary>
    public static class ConfigurationHelper
    {
        /// <summary>
        /// 設定をバックアップ
        /// </summary>
        public static async Task BackupConfigurationAsync(string configFilePath, string backupDirectory = "Backups")
        {
            try
            {
                if (File.Exists(configFilePath))
                {
                    Directory.CreateDirectory(backupDirectory);
                    var backupFileName = $"config_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                    var backupPath = Path.Combine(backupDirectory, backupFileName);
                    await File.CopyAsync(configFilePath, backupPath);
                }
            }
            catch (Exception ex)
            {
                throw new ConfigurationException($"Failed to backup configuration: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 設定を復元
        /// </summary>
        public static async Task RestoreConfigurationAsync(string backupFilePath, string configFilePath)
        {
            try
            {
                if (File.Exists(backupFilePath))
                {
                    await File.CopyAsync(backupFilePath, configFilePath);
                }
                else
                {
                    throw new FileNotFoundException($"Backup file not found: {backupFilePath}");
                }
            }
            catch (Exception ex)
            {
                throw new ConfigurationException($"Failed to restore configuration: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 設定をマージ
        /// </summary>
        public static async Task<Dictionary<string, object>> MergeConfigurationsAsync(string primaryConfigPath, string secondaryConfigPath)
        {
            var primaryConfig = new Dictionary<string, object>();
            var secondaryConfig = new Dictionary<string, object>();

            if (File.Exists(primaryConfigPath))
            {
                var json = await File.ReadAllTextAsync(primaryConfigPath);
                primaryConfig = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            }

            if (File.Exists(secondaryConfigPath))
            {
                var json = await File.ReadAllTextAsync(secondaryConfigPath);
                secondaryConfig = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            }

            // プライマリ設定をベースにセカンダリ設定をマージ
            foreach (var kvp in secondaryConfig)
            {
                if (!primaryConfig.ContainsKey(kvp.Key))
                {
                    primaryConfig[kvp.Key] = kvp.Value;
                }
            }

            return primaryConfig;
        }
    }
}