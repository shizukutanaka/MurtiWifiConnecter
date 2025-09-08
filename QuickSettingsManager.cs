using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    // 統合設定管理 - 軽量・高速・実用的
    public static class QuickSettingsManager
    {
        private static readonly Dictionary<string, object> _quickSettings = new(25);
        private static readonly Dictionary<string, SettingValidator> _validators = new();
        private static readonly string _settingsFilePath;
        private static readonly string _backupSettingsFilePath;
        private static readonly object _lockObject = new object();
        private static DateTime _lastBackupTime = DateTime.MinValue;
        
        // 設定変更通知イベント
        public static event EventHandler<SettingChangedEventArgs>? SettingChanged;
        
        // AppSettings互換の静的プロパティ
        public static bool IsPortableMode { get; private set; }
        public static string AppDataPath { get; private set; } = string.Empty;
        
        // 設定定数（ConfigurationConstants統合）
        public static class Constants
        {
            // WiFi接続設定
            public static int QuickTimeoutMs => GetSetting("quick_timeout_ms", 2500);
            public static int NormalTimeoutMs => GetSetting("normal_timeout_ms", 8000);
            public static int ExtendedTimeoutMs => GetSetting("extended_timeout_ms", 12000);
            public static int ConnectionDelayMs => GetSetting("connection_delay_ms", 400);
            public static int MaxRetryAttempts => GetSetting("max_retry_attempts", 3);
            
            // パフォーマンス設定
            public static int MemoryOptIntervalMinutes => GetSetting("memory_opt_interval", 1);
            public static int SystemMonitoringIntervalMs => GetSetting("system_monitoring_interval", 60000);
            
            // キャッシュ設定
            public static int ProfileCacheValidityMinutes => GetSetting("cache_validity_minutes", 3);
            public static int MaxProfileCacheSize => GetSetting("max_cache_size", 20);
            
            // ログ設定
            public static int MaxLogFileSizeMB => GetSetting("max_log_file_mb", 5);
            public static int MaxLogFiles => GetSetting("max_log_files", 5);
            public static int LogFlushIntervalMs => GetSetting("log_flush_interval", 5000);
            
            // 復旧設定
            public static int BaseRetryDelayMs => GetSetting("base_retry_delay", 1000);
            public static int MaxRetryDelayMs => GetSetting("max_retry_delay", 15000);
            public static int NetworkResetDelayMs => GetSetting("network_reset_delay", 800);
            
            // 起動設定
            public static int StartupDelayMs => GetSetting("startup_delay", 500); // 短縮した起動遅延
        }
        
        static QuickSettingsManager()
        {
            DetectPortableMode();
            AppDataPath = GetAppDataPath();
            Directory.CreateDirectory(AppDataPath);
            _settingsFilePath = Path.Combine(AppDataPath, "quick_settings.json");
            _backupSettingsFilePath = Path.Combine(AppDataPath, "quick_settings_backup.json");
            
            InitializeValidators();
            LoadDefaultSettings();
            _ = LoadSettingsAsync(); // 非同期で設定読み込み
        }
        
        private static void InitializeValidators()
        {
            // タイムアウト設定の検証
            _validators["quick_timeout_ms"] = new SettingValidator(
                value => value is int ms && ms >= 1000 && ms <= 10000,
                "クイックタイムアウトは1000-10000ms の範囲で設定してください。");
                
            _validators["normal_timeout_ms"] = new SettingValidator(
                value => value is int ms && ms >= 5000 && ms <= 30000,
                "通常タイムアウトは5000-30000ms の範囲で設定してください。");
                
            _validators["extended_timeout_ms"] = new SettingValidator(
                value => value is int ms && ms >= 10000 && ms <= 60000,
                "拡張タイムアウトは10000-60000ms の範囲で設定してください。");
            
            // リフレッシュ間隔の検証
            _validators["refresh_interval_seconds"] = new SettingValidator(
                value => value is int sec && sec >= 5 && sec <= 300,
                "リフレッシュ間隔は5-300秒 の範囲で設定してください。");
                
            _validators["scan_interval_seconds"] = new SettingValidator(
                value => value is int sec && sec >= 5 && sec <= 300,
                "スキャン間隔は5-300秒 の範囲で設定してください。");
            
            // 信号強度の検証
            _validators["min_signal_threshold"] = new SettingValidator(
                value => value is int threshold && threshold >= 0 && threshold <= 100,
                "最小信号強度は0-100% の範囲で設定してください。");
                
            _validators["auto_switch_threshold_percent"] = new SettingValidator(
                value => value is int threshold && threshold >= 5 && threshold <= 50,
                "自動切替閾値は5-50% の範囲で設定してください。");
            
            // ファイルサイズの検証
            _validators["max_log_file_mb"] = new SettingValidator(
                value => value is int mb && mb >= 1 && mb <= 100,
                "最大ログファイルサイズは1-100MB の範囲で設定してください。");
                
            _validators["max_log_files"] = new SettingValidator(
                value => value is int files && files >= 1 && files <= 20,
                "最大ログファイル数は1-20個 の範囲で設定してください。");
            
            // ウィンドウサイズの検証
            _validators["window_width"] = new SettingValidator(
                value => value is double width && width >= 600 && width <= 2000,
                "ウィンドウ幅は600-2000px の範囲で設定してください。");
                
            _validators["window_height"] = new SettingValidator(
                value => value is double height && height >= 400 && height <= 1500,
                "ウィンドウ高さは400-1500px の範囲で設定してください。");
            
            // 言語設定の検証
            _validators["preferred_language"] = new SettingValidator(
                value => value is string lang && (lang == "en" || lang == "ja"),
                "対応言語は en または ja のみです。");
        }
        
        private static void DetectPortableMode()
        {
            try
            {
                var execPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var execDir = Path.GetDirectoryName(execPath) ?? ".";
                var portableFile = Path.Combine(execDir, "portable.txt");
                
                var args = Environment.GetCommandLineArgs();
                var hasPortableArg = args.Any(arg => arg.Equals("--portable", StringComparison.OrdinalIgnoreCase));
                
                IsPortableMode = File.Exists(portableFile) || hasPortableArg;
            }
            catch
            {
                IsPortableMode = false;
            }
        }
        
        private static string GetAppDataPath()
        {
            if (IsPortableMode)
            {
                return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            }
            else
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appDataPath, "MurtiWifiConnecter");
            }
        }
        
        public static void EnablePortableMode()
        {
            try
            {
                var execPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var execDir = Path.GetDirectoryName(execPath) ?? ".";
                var portableFile = Path.Combine(execDir, "portable.txt");
                
                File.WriteAllText(portableFile, $"Murti WiFi Connector Portable Mode\nCreated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                IsPortableMode = true;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("QuickSettingsManager.EnablePortableMode", ex);
            }
        }
        
        public static void DisablePortableMode()
        {
            try
            {
                var execPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var execDir = Path.GetDirectoryName(execPath) ?? ".";
                var portableFile = Path.Combine(execDir, "portable.txt");
                
                if (File.Exists(portableFile))
                {
                    File.Delete(portableFile);
                }
                IsPortableMode = false;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("QuickSettingsManager.DisablePortableMode", ex);
            }
        }
        
        private static void LoadDefaultSettings()
        {
            var defaults = new Dictionary<string, object>
            {
                // WiFi接続設定
                ["auto_connect_timeout"] = 15,
                ["scan_interval_seconds"] = 15,
                ["max_retry_attempts"] = 3,
                ["enable_signal_filter"] = true,
                ["min_signal_threshold"] = 30,
                ["prefer_5ghz"] = true,
                ["auto_cleanup_profiles"] = true,
                ["detailed_logging"] = false,
                ["tray_notifications"] = true,
                ["quick_connect_enabled"] = true,
                
                // タイムアウト設定
                ["quick_timeout_ms"] = 3000,
                ["normal_timeout_ms"] = 10000,
                ["extended_timeout_ms"] = 15000,
                ["connection_delay_ms"] = 500,
                ["connection_timeout_seconds"] = 15,
                ["scan_timeout_seconds"] = 10,
                
                // パフォーマンス設定
                ["memory_opt_interval"] = 1,
                ["system_monitoring_interval"] = 60000,
                ["perf_opt_interval"] = 5,
                ["cache_validity_minutes"] = 3,
                ["max_cache_size"] = 20,
                
                // ログ設定
                ["max_log_file_mb"] = 5,
                ["max_log_files"] = 5,
                ["log_flush_interval"] = 5000,
                ["log_retention_days"] = 7,
                ["enable_connection_logging"] = true,
                
                // 復旧設定
                ["base_retry_delay"] = 1000,
                ["max_retry_delay"] = 15000,
                ["network_reset_delay"] = 800,
                ["min_tuning_interval_hours"] = 6,
                ["startup_delay_ms"] = 2000,
                
                // UI設定
                ["refresh_throttle_ms"] = 100,
                ["ui_update_delay_ms"] = 50,
                ["refresh_interval_seconds"] = 15,
                ["max_displayed_networks"] = 50,
                ["minimize_to_tray"] = true,
                ["start_minimized"] = false,
                ["show_password_strength"] = true,
                ["show_balloon_notifications"] = true,
                ["enable_network_monitoring"] = true,
                ["enable_auto_switch"] = false,
                ["auto_switch_threshold_percent"] = 20,
                
                // アプリケーション設定
                ["max_profile_history"] = 30,
                ["preferred_language"] = "en",
                ["portable_mode_enabled"] = false,
                
                // ウィンドウ設定
                ["window_state"] = "Normal",
                ["window_width"] = 900.0,
                ["window_height"] = 500.0,
                ["window_left"] = -1.0,
                ["window_top"] = -1.0
            };
            
            lock (_lockObject)
            {
                foreach (var setting in defaults)
                {
                    if (!_quickSettings.ContainsKey(setting.Key))
                    {
                        _quickSettings[setting.Key] = setting.Value;
                    }
                }
            }
        }
        
        public static T GetSetting<T>(string key, T defaultValue = default)
        {
            return _quickSettings.TryGetValue(key, out var value) && value is T ? (T)value : defaultValue;
        }
        
        public static void SetSetting<T>(string key, T value)
        {
            // バリデーション実行
            if (_validators.TryGetValue(key, out var validator) && !validator.IsValid(value))
            {
                throw new ArgumentException(validator.ErrorMessage, nameof(value));
            }
            
            object? oldValue = null;
            bool hasOldValue = false;
            
            lock (_lockObject)
            {
                if (_quickSettings.TryGetValue(key, out var existing))
                {
                    oldValue = existing;
                    hasOldValue = true;
                }
                
                _quickSettings[key] = value;
            }
            
            // 設定変更通知
            try
            {
                var args = new SettingChangedEventArgs
                {
                    Key = key,
                    NewValue = value,
                    OldValue = oldValue,
                    HasOldValue = hasOldValue
                };
                
                SettingChanged?.Invoke(null, args);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("QuickSettingsManager.SetSetting.Notification", ex);
            }
        }
        
        public static bool ValidateSetting<T>(string key, T value, out string? errorMessage)
        {
            errorMessage = null;
            if (_validators.TryGetValue(key, out var validator) && !validator.IsValid(value))
            {
                errorMessage = validator.ErrorMessage;
                return false;
            }
            return true;
        }
        
        // 実用的なプリセット設定
        public static void ApplyFastModeSettings()
        {
            SetSetting("auto_connect_timeout", 10);
            SetSetting("scan_interval_seconds", 10);
            SetSetting("max_retry_attempts", 2);
            SetSetting("quick_timeout_ms", 2000);
            SetSetting("connection_delay_ms", 300);
            _ = SaveSettingsAsync();
        }
        
        public static void ApplyBatteryModeSettings()
        {
            SetSetting("scan_interval_seconds", 30);
            SetSetting("detailed_logging", false);
            SetSetting("tray_notifications", false);
            SetSetting("memory_opt_interval", 2);
            SetSetting("perf_opt_interval", 10);
            _ = SaveSettingsAsync();
        }
        
        public static void ApplyCompatibilityModeSettings()
        {
            SetSetting("auto_connect_timeout", 20);
            SetSetting("max_retry_attempts", 5);
            SetSetting("prefer_5ghz", false);
            SetSetting("extended_timeout_ms", 20000);
            SetSetting("connection_delay_ms", 800);
            _ = SaveSettingsAsync();
        }
        
        public static void ApplyStabilityModeSettings()
        {
            SetSetting("auto_connect_timeout", 25);
            SetSetting("scan_interval_seconds", 20);
            SetSetting("max_retry_attempts", 4);
            SetSetting("normal_timeout_ms", 12000);
            SetSetting("base_retry_delay", 1500);
            SetSetting("detailed_logging", true);
            _ = SaveSettingsAsync();
        }
        
        public static void ApplyOfficeNetworkSettings()
        {
            SetSetting("prefer_5ghz", true);
            SetSetting("enable_signal_filter", true);
            SetSetting("min_signal_threshold", 40);
            SetSetting("auto_cleanup_profiles", true);
            SetSetting("quick_connect_enabled", true);
            _ = SaveSettingsAsync();
        }
        
        public static void ApplyPublicWifiSettings()
        {
            SetSetting("prefer_5ghz", false);
            SetSetting("enable_signal_filter", false);
            SetSetting("min_signal_threshold", 20);
            SetSetting("max_retry_attempts", 2);
            SetSetting("detailed_logging", true);
            _ = SaveSettingsAsync();
        }
        
        public static Dictionary<string, Action> GetAvailablePresets()
        {
            return new Dictionary<string, Action>(8)
            {
                ["高速モード"] = ApplyFastModeSettings,
                ["省電力モード"] = ApplyBatteryModeSettings,
                ["互換性モード"] = ApplyCompatibilityModeSettings,
                ["安定性重視モード"] = ApplyStabilityModeSettings,
                ["オフィスネットワーク用"] = ApplyOfficeNetworkSettings,
                ["公共WiFi用"] = ApplyPublicWifiSettings
            };
        }
        
        public static Dictionary<string, object> GetAllSettings()
        {
            return new Dictionary<string, object>(_quickSettings);
        }
        
        public static void ResetToDefaults()
        {
            lock (_lockObject)
            {
                _quickSettings.Clear();
                LoadDefaultSettings();
                _ = SaveSettingsAsync();
            }
        }
        
        private static async Task LoadSettingsAsync()
        {
            var loadSuccessful = false;
            
            // メイン設定ファイルからの読み込み
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var loadedSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        if (loadedSettings != null && loadedSettings.Count > 0)
                        {
                            var validSettingsCount = 0;
                            lock (_lockObject)
                            {
                                foreach (var setting in loadedSettings)
                                {
                                    try
                                    {
                                        // バリデーション実行（読み込み時は警告のみ）
                                        if (_validators.TryGetValue(setting.Key, out var validator) && 
                                            !validator.IsValid(setting.Value))
                                        {
                                            ErrorHandler.LogError("QuickSettingsManager.LoadSettings.Validation", 
                                                new ArgumentException($"設定項目 '{setting.Key}' の値が無効です: {validator.ErrorMessage}"));
                                            continue;
                                        }
                                        
                                        _quickSettings[setting.Key] = setting.Value;
                                        validSettingsCount++;
                                    }
                                    catch (Exception settingEx)
                                    {
                                        ErrorHandler.LogError($"QuickSettingsManager.LoadSettings.Setting.{setting.Key}", settingEx);
                                    }
                                }
                            }
                            
                            if (validSettingsCount > 0)
                            {
                                loadSuccessful = true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("QuickSettingsManager.LoadSettings.Main", ex);
            }
            
            // メイン設定ファイルが破損している場合、バックアップから復旧を試行
            if (!loadSuccessful && File.Exists(_backupSettingsFilePath))
            {
                try
                {
                    var backupJson = await File.ReadAllTextAsync(_backupSettingsFilePath);
                    if (!string.IsNullOrWhiteSpace(backupJson))
                    {
                        var backupSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(backupJson);
                        if (backupSettings != null && backupSettings.Count > 0)
                        {
                            lock (_lockObject)
                            {
                                foreach (var setting in backupSettings)
                                {
                                    try
                                    {
                                        if (!_validators.TryGetValue(setting.Key, out var validator) || 
                                            validator.IsValid(setting.Value))
                                        {
                                            _quickSettings[setting.Key] = setting.Value;
                                        }
                                    }
                                    catch (Exception settingEx)
                                    {
                                        ErrorHandler.LogError($"QuickSettingsManager.LoadSettings.Backup.{setting.Key}", settingEx);
                                    }
                                }
                            }
                            
                            // バックアップからの復旧が成功した場合、メインファイルを復旧
                            try
                            {
                                await File.WriteAllTextAsync(_settingsFilePath, backupJson);
                                ErrorHandler.LogError("QuickSettingsManager.LoadSettings.Recovery", 
                                    new InvalidOperationException("設定ファイルをバックアップから復旧しました"));
                            }
                            catch (Exception recoveryEx)
                            {
                                ErrorHandler.LogError("QuickSettingsManager.LoadSettings.RecoveryWrite", recoveryEx);
                            }
                        }
                    }
                }
                catch (Exception backupEx)
                {
                    ErrorHandler.LogError("QuickSettingsManager.LoadSettings.Backup", backupEx);
                }
            }
        }
        
        public static async Task SaveSettingsAsync()
        {
            try
            {
                Dictionary<string, object> settingsToSave;
                lock (_lockObject)
                {
                    settingsToSave = new Dictionary<string, object>(_quickSettings);
                }
                
                var json = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                // 自動バックアップ（24時間ごと）
                if (DateTime.Now - _lastBackupTime > TimeSpan.FromHours(24))
                {
                    try
                    {
                        if (File.Exists(_settingsFilePath))
                        {
                            await File.WriteAllTextAsync(_backupSettingsFilePath, 
                                await File.ReadAllTextAsync(_settingsFilePath));
                        }
                        _lastBackupTime = DateTime.Now;
                    }
                    catch (Exception backupEx)
                    {
                        ErrorHandler.LogError("QuickSettingsManager.AutoBackup", backupEx);
                    }
                }
                
                // 設定ファイル保存
                await File.WriteAllTextAsync(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("QuickSettingsManager.SaveSettings", ex);
            }
        }
        
        public static void SetSettingAndSave<T>(string key, T value)
        {
            SetSetting(key, value);
            _ = SaveSettingsAsync();
        }
        
        // 手動バックアップ
        public static async Task<bool> CreateBackupAsync()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    await File.WriteAllTextAsync(_backupSettingsFilePath, json);
                    _lastBackupTime = DateTime.Now;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("QuickSettingsManager.CreateBackup", ex);
                return false;
            }
        }
        
        // バックアップから復旧
        public static async Task<bool> RestoreFromBackupAsync()
        {
            try
            {
                if (File.Exists(_backupSettingsFilePath))
                {
                    var backupJson = await File.ReadAllTextAsync(_backupSettingsFilePath);
                    var backupSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(backupJson);
                    
                    if (backupSettings != null)
                    {
                        lock (_lockObject)
                        {
                            _quickSettings.Clear();
                            LoadDefaultSettings();
                            
                            foreach (var setting in backupSettings)
                            {
                                if (!_validators.TryGetValue(setting.Key, out var validator) || 
                                    validator.IsValid(setting.Value))
                                {
                                    _quickSettings[setting.Key] = setting.Value;
                                }
                            }
                        }
                        
                        await SaveSettingsAsync();
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("QuickSettingsManager.RestoreFromBackup", ex);
                return false;
            }
        }
        
        // 設定統計の取得
        public static SettingsStatistics GetStatistics()
        {
            lock (_lockObject)
            {
                return new SettingsStatistics
                {
                    TotalSettings = _quickSettings.Count,
                    ValidatedSettings = _quickSettings.Count(kvp => _validators.ContainsKey(kvp.Key)),
                    LastBackupTime = _lastBackupTime,
                    HasBackup = File.Exists(_backupSettingsFilePath),
                    ConfigurationPath = _settingsFilePath,
                    BackupPath = _backupSettingsFilePath,
                    IsPortableMode = IsPortableMode,
                    AppDataPath = AppDataPath
                };
            }
        }
        
        // 設定のエクスポート
        public static async Task<bool> ExportSettingsAsync(string filePath)
        {
            try
            {
                Dictionary<string, object> settingsToExport;
                lock (_lockObject)
                {
                    settingsToExport = new Dictionary<string, object>(_quickSettings);
                }
                
                var exportData = new
                {
                    ExportedAt = DateTime.Now,
                    Version = "1.0",
                    IsPortableMode = IsPortableMode,
                    Settings = settingsToExport
                };
                
                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await File.WriteAllTextAsync(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("QuickSettingsManager.ExportSettings", ex);
                return false;
            }
        }
        
        // 設定のインポート
        public static async Task<bool> ImportSettingsAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                
                var json = await File.ReadAllTextAsync(filePath);
                var importData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (importData?.TryGetValue("Settings", out var settingsObj) == true)
                {
                    var importedSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        settingsObj.ToString() ?? "{}");
                        
                    if (importedSettings != null)
                    {
                        var validImports = 0;
                        lock (_lockObject)
                        {
                            foreach (var setting in importedSettings)
                            {
                                try
                                {
                                    if (!_validators.TryGetValue(setting.Key, out var validator) || 
                                        validator.IsValid(setting.Value))
                                    {
                                        _quickSettings[setting.Key] = setting.Value;
                                        validImports++;
                                    }
                                }
                                catch (Exception settingEx)
                                {
                                    ErrorHandler.LogError($"QuickSettingsManager.ImportSettings.{setting.Key}", settingEx);
                                }
                            }
                        }
                        
                        if (validImports > 0)
                        {
                            await SaveSettingsAsync();
                            return true;
                        }
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("QuickSettingsManager.ImportSettings", ex);
                return false;
            }
        }
        
        // 設定の整合性チェック
        public static async Task<SettingsValidationResult> ValidateAllSettingsAsync()
        {
            var result = new SettingsValidationResult
            {
                CheckedAt = DateTime.Now
            };
            
            lock (_lockObject)
            {
                foreach (var setting in _quickSettings)
                {
                    if (_validators.TryGetValue(setting.Key, out var validator))
                    {
                        if (!validator.IsValid(setting.Value))
                        {
                            result.InvalidSettings.Add(new InvalidSetting
                            {
                                Key = setting.Key,
                                Value = setting.Value,
                                ErrorMessage = validator.ErrorMessage
                            });
                        }
                        result.ValidatedCount++;
                    }
                    result.TotalCount++;
                }
            }
            
            result.IsValid = result.InvalidSettings.Count == 0;
            return result;
        }
        
        // 不正な設定の自動修正
        public static async Task<int> FixInvalidSettingsAsync()
        {
            var fixedCount = 0;
            var toRemove = new List<string>();
            
            lock (_lockObject)
            {
                foreach (var setting in _quickSettings.ToList())
                {
                    if (_validators.TryGetValue(setting.Key, out var validator) && 
                        !validator.IsValid(setting.Value))
                    {
                        toRemove.Add(setting.Key);
                        fixedCount++;
                    }
                }
                
                // 無効な設定を削除（デフォルト値が自動的に使用される）
                foreach (var key in toRemove)
                {
                    _quickSettings.Remove(key);
                }
            }
            
            if (fixedCount > 0)
            {
                LoadDefaultSettings(); // デフォルト値を再適用
                await SaveSettingsAsync();
            }
            
            return fixedCount;
        }
    }
    
    #region Data Classes
    
    /// <summary>
    /// 設定値の検証を行うクラス
    /// </summary>
    public class SettingValidator
    {
        public Func<object?, bool> IsValid { get; }
        public string ErrorMessage { get; }
        
        public SettingValidator(Func<object?, bool> isValid, string errorMessage)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }
    }
    
    /// <summary>
    /// 設定変更イベントの引数
    /// </summary>
    public class SettingChangedEventArgs : EventArgs
    {
        public string Key { get; set; } = "";
        public object? NewValue { get; set; }
        public object? OldValue { get; set; }
        public bool HasOldValue { get; set; }
        
        public bool IsValueChanged => !Equals(NewValue, OldValue);
    }
    
    /// <summary>
    /// 設定統計情報
    /// </summary>
    public class SettingsStatistics
    {
        public int TotalSettings { get; set; }
        public int ValidatedSettings { get; set; }
        public DateTime LastBackupTime { get; set; }
        public bool HasBackup { get; set; }
        public string ConfigurationPath { get; set; } = "";
        public string BackupPath { get; set; } = "";
        public bool IsPortableMode { get; set; }
        public string AppDataPath { get; set; } = "";
        
        public double ValidationCoverage => TotalSettings > 0 ? (double)ValidatedSettings / TotalSettings * 100 : 0;
        public bool IsBackupCurrent => HasBackup && (DateTime.Now - LastBackupTime).TotalHours < 24;
    }
    
    /// <summary>
    /// 設定検証結果
    /// </summary>
    public class SettingsValidationResult
    {
        public DateTime CheckedAt { get; set; }
        public bool IsValid { get; set; }
        public int TotalCount { get; set; }
        public int ValidatedCount { get; set; }
        public List<InvalidSetting> InvalidSettings { get; set; } = new();
        
        public double ValidationCoverage => TotalCount > 0 ? (double)ValidatedCount / TotalCount * 100 : 0;
        public bool HasIssues => InvalidSettings.Count > 0;
        
        public string GetSummary()
        {
            if (IsValid)
                return $"すべての設定が有効です（{ValidatedCount}/{TotalCount} 項目を検証）";
            
            return $"{InvalidSettings.Count} 個の無効な設定が見つかりました（{ValidatedCount}/{TotalCount} 項目を検証）";
        }
    }
    
    /// <summary>
    /// 無効な設定情報
    /// </summary>
    public class InvalidSetting
    {
        public string Key { get; set; } = "";
        public object? Value { get; set; }
        public string ErrorMessage { get; set; } = "";
        
        public override string ToString()
        {
            return $"{Key}: {Value} - {ErrorMessage}";
        }
    }
    
    #endregion
}