using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.UserExperience
{
    public class ThemeManager : IThemeManager
    {
        private readonly ILoggingService _logger;
        private readonly IConfigurationService _configService;
        private readonly Dictionary<string, Dictionary<string, object>> _themes;
        private string _currentTheme;

        public event EventHandler<ThemeChangedEventArgs> ThemeChanged;

        public string CurrentTheme => _currentTheme;
        public List<string> AvailableThemes => _themes.Keys.ToList();

        public ThemeManager(ILoggingService logger, IConfigurationService configService)
        {
            _logger = logger;
            _configService = configService;
            _themes = new Dictionary<string, Dictionary<string, object>>();
            
            InitializeBuiltInThemes();
            LoadCustomThemes();
            
            _currentTheme = _configService.GetValue("UI:Theme", "System");
            if (!_themes.ContainsKey(_currentTheme))
            {
                _currentTheme = "Light";
            }
        }

        public async Task ApplyThemeAsync(string themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName))
                throw new ArgumentException("Theme name cannot be empty", nameof(themeName));
            
            if (!_themes.ContainsKey(themeName))
                throw new ArgumentException($"Theme '{themeName}' not found", nameof(themeName));
            
            try
            {
                var oldTheme = _currentTheme;
                var themeResources = _themes[themeName];
                
                // Apply theme to WPF application
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var app = Application.Current;
                    
                    // Clear existing theme resources
                    var keysToRemove = new List<object>();
                    foreach (var key in app.Resources.Keys)
                    {
                        if (key.ToString().StartsWith("Theme."))
                        {
                            keysToRemove.Add(key);
                        }
                    }
                    
                    foreach (var key in keysToRemove)
                    {
                        app.Resources.Remove(key);
                    }
                    
                    // Apply new theme resources
                    foreach (var resource in themeResources)
                    {
                        var resourceKey = $"Theme.{resource.Key}";
                        
                        if (resource.Value is string colorString)
                        {
                            try
                            {
                                var color = (Color)ColorConverter.ConvertFromString(colorString);
                                app.Resources[resourceKey] = new SolidColorBrush(color);
                            }
                            catch
                            {
                                app.Resources[resourceKey] = resource.Value;
                            }
                        }
                        else
                        {
                            app.Resources[resourceKey] = resource.Value;
                        }
                    }
                    
                    // Update system-specific resources
                    UpdateSystemResources(themeName);
                });
                
                _currentTheme = themeName;
                await _configService.SetValueAsync("UI:Theme", themeName);
                
                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs
                {
                    OldTheme = oldTheme,
                    NewTheme = themeName,
                    Timestamp = DateTime.UtcNow
                });
                
                _logger.LogInfo($"Theme applied: {themeName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to apply theme: {themeName}", ex);
                throw;
            }
        }

        public async Task<Dictionary<string, object>> GetThemeResourcesAsync(string themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName))
                throw new ArgumentException("Theme name cannot be empty", nameof(themeName));
            
            if (!_themes.ContainsKey(themeName))
                throw new ArgumentException($"Theme '{themeName}' not found", nameof(themeName));
            
            return await Task.FromResult(new Dictionary<string, object>(_themes[themeName]));
        }

        public async Task RegisterCustomThemeAsync(string name, Dictionary<string, object> resources)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Theme name cannot be empty", nameof(name));
            
            if (resources == null)
                throw new ArgumentNullException(nameof(resources));
            
            _themes[name] = new Dictionary<string, object>(resources);
            await SaveCustomThemeAsync(name, resources);
            
            _logger.LogInfo($"Custom theme registered: {name}");
        }

        private void InitializeBuiltInThemes()
        {
            // Light Theme
            _themes["Light"] = new Dictionary<string, object>
            {
                ["Background"] = "#FFFFFF",
                ["Surface"] = "#F5F5F5",
                ["Primary"] = "#2196F3",
                ["PrimaryVariant"] = "#1976D2",
                ["Secondary"] = "#FFC107",
                ["SecondaryVariant"] = "#F57F17",
                ["Error"] = "#F44336",
                ["OnBackground"] = "#000000",
                ["OnSurface"] = "#000000",
                ["OnPrimary"] = "#FFFFFF",
                ["OnSecondary"] = "#000000",
                ["OnError"] = "#FFFFFF",
                ["TextPrimary"] = "#212121",
                ["TextSecondary"] = "#757575",
                ["Divider"] = "#E0E0E0",
                ["Success"] = "#4CAF50",
                ["Warning"] = "#FF9800",
                ["Info"] = "#2196F3"
            };
            
            // Dark Theme
            _themes["Dark"] = new Dictionary<string, object>
            {
                ["Background"] = "#121212",
                ["Surface"] = "#1E1E1E",
                ["Primary"] = "#BB86FC",
                ["PrimaryVariant"] = "#3700B3",
                ["Secondary"] = "#03DAC6",
                ["SecondaryVariant"] = "#018786",
                ["Error"] = "#CF6679",
                ["OnBackground"] = "#FFFFFF",
                ["OnSurface"] = "#FFFFFF",
                ["OnPrimary"] = "#000000",
                ["OnSecondary"] = "#000000",
                ["OnError"] = "#000000",
                ["TextPrimary"] = "#FFFFFF",
                ["TextSecondary"] = "#B3FFFFFF",
                ["Divider"] = "#1F1F1F",
                ["Success"] = "#4CAF50",
                ["Warning"] = "#FF9800",
                ["Info"] = "#2196F3"
            };
            
            // System Theme (follows OS theme)
            _themes["System"] = GetSystemThemeColors();
            
            // High Contrast Theme
            _themes["HighContrast"] = new Dictionary<string, object>
            {
                ["Background"] = "#000000",
                ["Surface"] = "#1A1A1A",
                ["Primary"] = "#FFFF00",
                ["PrimaryVariant"] = "#CCCC00",
                ["Secondary"] = "#00FFFF",
                ["SecondaryVariant"] = "#00CCCC",
                ["Error"] = "#FF0000",
                ["OnBackground"] = "#FFFFFF",
                ["OnSurface"] = "#FFFFFF",
                ["OnPrimary"] = "#000000",
                ["OnSecondary"] = "#000000",
                ["OnError"] = "#FFFFFF",
                ["TextPrimary"] = "#FFFFFF",
                ["TextSecondary"] = "#FFFF00",
                ["Divider"] = "#FFFFFF",
                ["Success"] = "#00FF00",
                ["Warning"] = "#FFFF00",
                ["Info"] = "#00FFFF"
            };
            
            // Blue Theme
            _themes["Blue"] = new Dictionary<string, object>
            {
                ["Background"] = "#F3F9FF",
                ["Surface"] = "#E3F2FD",
                ["Primary"] = "#1976D2",
                ["PrimaryVariant"] = "#1565C0",
                ["Secondary"] = "#FFC107",
                ["SecondaryVariant"] = "#FF8F00",
                ["Error"] = "#D32F2F",
                ["OnBackground"] = "#0D47A1",
                ["OnSurface"] = "#0D47A1",
                ["OnPrimary"] = "#FFFFFF",
                ["OnSecondary"] = "#000000",
                ["OnError"] = "#FFFFFF",
                ["TextPrimary"] = "#0D47A1",
                ["TextSecondary"] = "#1976D2",
                ["Divider"] = "#BBDEFB",
                ["Success"] = "#2E7D32",
                ["Warning"] = "#F57C00",
                ["Info"] = "#1976D2"
            };
        }

        private Dictionary<string, object> GetSystemThemeColors()
        {
            try
            {
                // Detect Windows theme
                var isLightTheme = IsWindowsLightTheme();
                return isLightTheme ? _themes["Light"] : _themes["Dark"];
            }
            catch
            {
                // Fallback to light theme
                return new Dictionary<string, object>(_themes["Light"]);
            }
        }

        private bool IsWindowsLightTheme()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                return value != null && (int)value == 1;
            }
            catch
            {
                return true; // Default to light theme
            }
        }

        private void UpdateSystemResources(string themeName)
        {
            try
            {
                var app = Application.Current;
                var resources = _themes[themeName];
                
                // Update window chrome colors if available
                if (resources.TryGetValue("Primary", out var primaryColor))
                {
                    app.Resources["SystemAccentColor"] = primaryColor;
                }
                
                // Update selection colors
                if (resources.TryGetValue("Primary", out var selectionColor))
                {
                    app.Resources["SelectionHighlightColor"] = selectionColor;
                }
                
                // Update focus visual colors
                if (resources.TryGetValue("Primary", out var focusColor))
                {
                    app.Resources["FocusVisualColor"] = focusColor;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to update system resources", ex);
            }
        }

        private void LoadCustomThemes()
        {
            try
            {
                var themesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MurtiWifiConnecter",
                    "Themes"
                );
                
                if (Directory.Exists(themesPath))
                {
                    var themeFiles = Directory.GetFiles(themesPath, "*.json");
                    
                    foreach (var themeFile in themeFiles)
                    {
                        try
                        {
                            var themeName = Path.GetFileNameWithoutExtension(themeFile);
                            var json = File.ReadAllText(themeFile);
                            var themeData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                            
                            if (themeData != null)
                            {
                                _themes[themeName] = themeData;
                                _logger.LogDebug($"Loaded custom theme: {themeName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to load theme file: {themeFile}", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to load custom themes", ex);
            }
        }

        private async Task SaveCustomThemeAsync(string name, Dictionary<string, object> resources)
        {
            try
            {
                var themesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MurtiWifiConnecter",
                    "Themes"
                );
                
                Directory.CreateDirectory(themesPath);
                
                var themeFile = Path.Combine(themesPath, $"{name}.json");
                var json = JsonSerializer.Serialize(resources, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(themeFile, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save custom theme: {name}", ex);
                throw;
            }
        }
    }
}