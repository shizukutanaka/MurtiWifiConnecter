using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.UserExperience
{
    public class LocalizationManager : ILocalizationManager
    {
        private readonly ILoggingService _logger;
        private readonly IConfigurationService _configService;
        private readonly Dictionary<string, Dictionary<string, string>> _resources;
        private readonly Dictionary<string, CultureInfo> _supportedCultures;
        private string _currentCulture;
        private CultureInfo _currentCultureInfo;

        public event EventHandler<CultureChangedEventArgs> CultureChanged;

        public string CurrentCulture => _currentCulture;
        public List<string> SupportedCultures => _supportedCultures.Keys.ToList();

        public LocalizationManager(ILoggingService logger, IConfigurationService configService)
        {
            _logger = logger;
            _configService = configService;
            _resources = new Dictionary<string, Dictionary<string, string>>();
            _supportedCultures = new Dictionary<string, CultureInfo>();
            
            InitializeSupportedCultures();
            LoadResources();
            
            // Set initial culture
            var savedCulture = _configService.GetValue("UI:Language", CultureInfo.CurrentUICulture.Name);
            if (_supportedCultures.ContainsKey(savedCulture))
            {
                _currentCulture = savedCulture;
            }
            else
            {
                _currentCulture = GetBestMatchCulture(savedCulture);
            }
            
            _currentCultureInfo = _supportedCultures[_currentCulture];
            
            // Set application culture
            CultureInfo.CurrentUICulture = _currentCultureInfo;
            CultureInfo.CurrentCulture = _currentCultureInfo;
        }

        public string GetString(string key, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;
            
            try
            {
                // Try current culture first
                if (_resources.TryGetValue(_currentCulture, out var currentResources) &&
                    currentResources.TryGetValue(key, out var value))
                {
                    return args.Length > 0 ? string.Format(value, args) : value;
                }
                
                // Try fallback to English
                if (_currentCulture != "en-US" &&
                    _resources.TryGetValue("en-US", out var englishResources) &&
                    englishResources.TryGetValue(key, out var englishValue))
                {
                    return args.Length > 0 ? string.Format(englishValue, args) : englishValue;
                }
                
                // Return key if not found
                _logger.LogWarning($"Localization key not found: {key}");
                return $"[{key}]";
            }
            catch (FormatException ex)
            {
                _logger.LogError($"String format error for key '{key}': {ex.Message}");
                return _resources[_currentCulture][key]; // Return unformatted string
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to get localized string for key '{key}'", ex);
                return $"[{key}]";
            }
        }

        public async Task SetCultureAsync(string cultureName)
        {
            if (string.IsNullOrWhiteSpace(cultureName))
                throw new ArgumentException("Culture name cannot be empty", nameof(cultureName));
            
            if (!_supportedCultures.ContainsKey(cultureName))
                throw new ArgumentException($"Culture '{cultureName}' is not supported", nameof(cultureName));
            
            if (_currentCulture == cultureName)
                return;
            
            try
            {
                var oldCulture = _currentCulture;
                _currentCulture = cultureName;
                _currentCultureInfo = _supportedCultures[cultureName];
                
                // Update application culture
                CultureInfo.CurrentUICulture = _currentCultureInfo;
                CultureInfo.CurrentCulture = _currentCultureInfo;
                
                // Load resources if not already loaded
                if (!_resources.ContainsKey(cultureName))
                {
                    await LoadResourcesAsync(cultureName);
                }
                
                // Save to configuration
                await _configService.SetValueAsync("UI:Language", cultureName);
                
                // Notify listeners
                CultureChanged?.Invoke(this, new CultureChangedEventArgs
                {
                    OldCulture = oldCulture,
                    NewCulture = cultureName,
                    Timestamp = DateTime.UtcNow
                });
                
                _logger.LogInfo($"Culture changed from '{oldCulture}' to '{cultureName}'");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to set culture to '{cultureName}'", ex);
                throw;
            }
        }

        public async Task LoadResourcesAsync(string cultureName)
        {
            if (string.IsNullOrWhiteSpace(cultureName))
                throw new ArgumentException("Culture name cannot be empty", nameof(cultureName));
            
            try
            {
                var resources = new Dictionary<string, string>();
                
                // Load embedded resources first
                await LoadEmbeddedResourcesAsync(cultureName, resources);
                
                // Load external resource files (can override embedded)
                await LoadExternalResourcesAsync(cultureName, resources);
                
                _resources[cultureName] = resources;
                
                _logger.LogDebug($"Loaded {resources.Count} resources for culture '{cultureName}'");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load resources for culture '{cultureName}'", ex);
                throw;
            }
        }

        private void InitializeSupportedCultures()
        {
            _supportedCultures["en-US"] = new CultureInfo("en-US");
            _supportedCultures["ja-JP"] = new CultureInfo("ja-JP");
            _supportedCultures["es-ES"] = new CultureInfo("es-ES");
            _supportedCultures["fr-FR"] = new CultureInfo("fr-FR");
            _supportedCultures["de-DE"] = new CultureInfo("de-DE");
            _supportedCultures["it-IT"] = new CultureInfo("it-IT");
            _supportedCultures["pt-BR"] = new CultureInfo("pt-BR");
            _supportedCultures["ru-RU"] = new CultureInfo("ru-RU");
            _supportedCultures["zh-CN"] = new CultureInfo("zh-CN");
            _supportedCultures["ko-KR"] = new CultureInfo("ko-KR");
        }

        private void LoadResources()
        {
            try
            {
                // Load default English resources
                var englishResources = GetDefaultEnglishResources();
                _resources["en-US"] = englishResources;
                
                // Load other embedded resources
                foreach (var culture in _supportedCultures.Keys.Where(c => c != "en-US"))
                {
                    _ = LoadResourcesSafelyAsync(culture);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to load default resources", ex);
            }
        }

        private async Task LoadResourcesSafelyAsync(string culture)
        {
            try
            {
                await LoadResourcesAsync(culture);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load resources for {culture}", ex);
            }
        }

        private async Task LoadEmbeddedResourcesAsync(string cultureName, Dictionary<string, string> resources)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = $"MurtiWifiConnecter.Resources.Strings.{cultureName}.json";
                
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var json = await reader.ReadToEndAsync();
                    var embeddedResources = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    
                    if (embeddedResources != null)
                    {
                        foreach (var kvp in embeddedResources)
                        {
                            resources[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"No embedded resources found for {cultureName}: {ex.Message}");
            }
        }

        private async Task LoadExternalResourcesAsync(string cultureName, Dictionary<string, string> resources)
        {
            try
            {
                var resourcesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MurtiWifiConnecter",
                    "Localization"
                );
                
                var resourceFile = Path.Combine(resourcesPath, $"{cultureName}.json");
                
                if (File.Exists(resourceFile))
                {
                    var json = await File.ReadAllTextAsync(resourceFile);
                    var externalResources = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    
                    if (externalResources != null)
                    {
                        foreach (var kvp in externalResources)
                        {
                            resources[kvp.Key] = kvp.Value; // Override embedded resources
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"No external resources found for {cultureName}: {ex.Message}");
            }
        }

        private string GetBestMatchCulture(string requestedCulture)
        {
            try
            {
                var culture = new CultureInfo(requestedCulture);
                
                // Try exact match first
                if (_supportedCultures.ContainsKey(culture.Name))
                    return culture.Name;
                
                // Try language match (e.g., "en" for "en-GB")
                var languageMatch = _supportedCultures.Keys
                    .FirstOrDefault(c => c.StartsWith(culture.TwoLetterISOLanguageName + "-"));
                
                if (languageMatch != null)
                    return languageMatch;
                
                // Fallback to English
                return "en-US";
            }
            catch
            {
                return "en-US";
            }
        }

        private Dictionary<string, string> GetDefaultEnglishResources()
        {
            return new Dictionary<string, string>
            {
                // Application
                ["App.Title"] = "Murti WiFi Connector",
                ["App.Description"] = "Fast and reliable WiFi connection manager",
                ["App.Version"] = "Version {0}",
                
                // Main Window
                ["MainWindow.Title"] = "WiFi Networks",
                ["MainWindow.Connect"] = "Connect",
                ["MainWindow.Disconnect"] = "Disconnect",
                ["MainWindow.Refresh"] = "Refresh",
                ["MainWindow.Settings"] = "Settings",
                ["MainWindow.Exit"] = "Exit",
                
                // Connection
                ["Connection.Connecting"] = "Connecting to {0}...",
                ["Connection.Connected"] = "Connected to {0}",
                ["Connection.Disconnected"] = "Disconnected",
                ["Connection.Failed"] = "Connection failed: {0}",
                ["Connection.Password"] = "Password",
                ["Connection.Connect"] = "Connect",
                ["Connection.Cancel"] = "Cancel",
                ["Connection.RememberPassword"] = "Remember this password",
                
                // Network List
                ["Networks.Scanning"] = "Scanning for networks...",
                ["Networks.NoNetworks"] = "No networks found",
                ["Networks.SignalStrength"] = "Signal: {0}%",
                ["Networks.Security"] = "Security: {0}",
                ["Networks.Connected"] = "Connected",
                ["Networks.Saved"] = "Saved",
                
                // Settings
                ["Settings.Title"] = "Settings",
                ["Settings.General"] = "General",
                ["Settings.Network"] = "Network",
                ["Settings.Security"] = "Security",
                ["Settings.Appearance"] = "Appearance",
                ["Settings.Advanced"] = "Advanced",
                ["Settings.About"] = "About",
                ["Settings.Apply"] = "Apply",
                ["Settings.Cancel"] = "Cancel",
                ["Settings.Reset"] = "Reset to Defaults",
                
                // General Settings
                ["Settings.AutoConnect"] = "Auto-connect to known networks",
                ["Settings.StartWithWindows"] = "Start with Windows",
                ["Settings.MinimizeToTray"] = "Minimize to system tray",
                ["Settings.ShowNotifications"] = "Show notifications",
                ["Settings.Language"] = "Language",
                ["Settings.Theme"] = "Theme",
                
                // Network Settings
                ["Settings.ScanInterval"] = "Scan interval (seconds)",
                ["Settings.ConnectionTimeout"] = "Connection timeout (seconds)",
                ["Settings.MaxRetries"] = "Maximum retry attempts",
                ["Settings.PreferredBand"] = "Preferred frequency band",
                
                // Security Settings
                ["Settings.SavePasswords"] = "Save network passwords",
                ["Settings.EncryptPasswords"] = "Encrypt saved passwords",
                ["Settings.ClearPasswords"] = "Clear all saved passwords",
                
                // Themes
                ["Theme.Light"] = "Light",
                ["Theme.Dark"] = "Dark",
                ["Theme.System"] = "System",
                ["Theme.HighContrast"] = "High Contrast",
                ["Theme.Blue"] = "Blue",
                
                // Notifications
                ["Notification.Connected"] = "Connected to {0}",
                ["Notification.Disconnected"] = "Disconnected from WiFi",
                ["Notification.ConnectionFailed"] = "Failed to connect to {0}",
                ["Notification.NetworkFound"] = "Found {0} networks",
                ["Notification.PasswordIncorrect"] = "Incorrect password for {0}",
                
                // Errors
                ["Error.Generic"] = "An error occurred: {0}",
                ["Error.NetworkNotFound"] = "Network not found",
                ["Error.InvalidPassword"] = "Invalid password",
                ["Error.ConnectionTimeout"] = "Connection timeout",
                ["Error.AccessDenied"] = "Access denied. Run as administrator.",
                ["Error.NetworkUnavailable"] = "Network service unavailable",
                
                // Status
                ["Status.Ready"] = "Ready",
                ["Status.Scanning"] = "Scanning...",
                ["Status.Connecting"] = "Connecting...",
                ["Status.Connected"] = "Connected",
                ["Status.Disconnected"] = "Disconnected",
                ["Status.Error"] = "Error",
                
                // Context Menu
                ["ContextMenu.Connect"] = "Connect",
                ["ContextMenu.Disconnect"] = "Disconnect",
                ["ContextMenu.Forget"] = "Forget Network",
                ["ContextMenu.Properties"] = "Properties",
                ["ContextMenu.Copy"] = "Copy SSID",
                
                // Accessibility
                ["Accessibility.HighContrast"] = "High contrast mode enabled",
                ["Accessibility.ScreenReader"] = "Screen reader support active",
                ["Accessibility.TextScale"] = "Text scaling: {0}%",
                
                // Units and Formats
                ["Units.Seconds"] = "{0} seconds",
                ["Units.Minutes"] = "{0} minutes",
                ["Units.Hours"] = "{0} hours",
                ["Units.Bytes"] = "{0} bytes",
                ["Units.KB"] = "{0} KB",
                ["Units.MB"] = "{0} MB",
                ["Units.GB"] = "{0} GB"
            };
        }
    }
}