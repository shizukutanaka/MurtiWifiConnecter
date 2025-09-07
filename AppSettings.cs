using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MurtiWifiConnecter
{
    public class AppSettings
    {
        private const string SettingsFileName = "appsettings.json";
        private readonly string _settingsFilePath;
        private AppConfiguration _configuration;
        
        public static bool IsPortableMode { get; private set; }
        
        public AppSettings()
        {
            DetectPortableMode();
            
            string appFolder;
            if (IsPortableMode)
            {
                // 実行ファイルと同じフォルダに設定を保存
                appFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            }
            else
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                appFolder = Path.Combine(appDataPath, "MurtiWifiConnecter");
            }
            
            Directory.CreateDirectory(appFolder);
            _settingsFilePath = Path.Combine(appFolder, SettingsFileName);
            _configuration = LoadSettings();
        }
        
        private static void DetectPortableMode()
        {
            try
            {
                // portable.txtファイルが存在するか、コマンドライン引数でポータブルモードが指定されているか確認
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
                ErrorHandler.LogError("AppSettings.EnablePortableMode", ex);
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
                ErrorHandler.LogError("AppSettings.DisablePortableMode", ex);
            }
        }

        public AppConfiguration Configuration => _configuration;

        public void UpdateSetting<T>(string key, T value)
        {
            try
            {
                var property = typeof(AppConfiguration).GetProperty(key);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(_configuration, value);
                    SaveSettings();
                }
            }
            catch { }
        }

        public T GetSetting<T>(string key, T defaultValue = default)
        {
            try
            {
                var property = typeof(AppConfiguration).GetProperty(key);
                if (property != null && property.CanRead)
                {
                    var value = property.GetValue(_configuration);
                    return value is T result ? result : defaultValue;
                }
            }
            catch { }
            return defaultValue;
        }

        private AppConfiguration LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    return JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
                }
            }
            catch { }
            
            return new AppConfiguration();
        }

        private void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_configuration, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch { }
        }

        public void ResetToDefault()
        {
            _configuration = new AppConfiguration();
            SaveSettings();
        }

        public string ExportSettings()
        {
            try
            {
                return JsonSerializer.Serialize(_configuration, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool ImportSettings(string json)
        {
            try
            {
                var imported = JsonSerializer.Deserialize<AppConfiguration>(json);
                if (imported != null)
                {
                    _configuration = imported;
                    SaveSettings();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public bool ExportToFile(string filePath)
        {
            try
            {
                var json = ExportSettings();
                if (!string.IsNullOrEmpty(json))
                {
                    File.WriteAllText(filePath, json);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public bool ImportFromFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    return ImportSettings(json);
                }
            }
            catch { }
            return false;
        }
    }

    public class AppConfiguration
    {
        public int RefreshIntervalSeconds { get; set; } = 15;
        public int MaxDisplayedNetworks { get; set; } = 50;
        public int MaxProfileHistory { get; set; } = 30;
        public bool MinimizeToTray { get; set; } = true;
        public bool StartMinimized { get; set; } = false;
        public bool ShowPasswordStrength { get; set; } = true;
        public bool EnableQuickConnect { get; set; } = true;
        public bool ShowBalloonNotifications { get; set; } = true;
        public string PreferredLanguage { get; set; } = "en";
        public bool EnableNetworkMonitoring { get; set; } = true;
        public int ConnectionTimeoutSeconds { get; set; } = 15;
        public bool AutoCleanupProfiles { get; set; } = true;
        public int ScanTimeoutSeconds { get; set; } = 10;
        public bool PortableModeEnabled { get; set; } = false;
        public bool EnableDetailedLogging { get; set; } = false;
        public bool EnableAutoSwitch { get; set; } = false;
        public int AutoSwitchThresholdPercent { get; set; } = 20;
        public bool EnableConnectionLogging { get; set; } = true;
        public int LogRetentionDays { get; set; } = 7;
        
        public string WindowState { get; set; } = "Normal";
        public double WindowWidth { get; set; } = 900;
        public double WindowHeight { get; set; } = 500;
        public double WindowLeft { get; set; } = -1;
        public double WindowTop { get; set; } = -1;
    }
}