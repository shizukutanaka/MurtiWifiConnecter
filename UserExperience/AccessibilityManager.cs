using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.UserExperience
{
    public class AccessibilityManager : IAccessibilityManager
    {
        private readonly ILoggingService _logger;
        private readonly IConfigurationService _configService;
        private AccessibilitySettings _currentSettings;
        private bool _systemHighContrast;
        private bool _systemScreenReader;
        private double _systemTextScale;

        public event EventHandler<AccessibilitySettingsChangedEventArgs> SettingsChanged;

        public bool IsHighContrastEnabled => _currentSettings?.HighContrastEnabled ?? _systemHighContrast;
        public bool IsScreenReaderActive => _currentSettings?.ScreenReaderActive ?? _systemScreenReader;
        
        public double TextScaleFactor
        {
            get => _currentSettings?.TextScaleFactor ?? _systemTextScale;
            set => SetTextScaleFactorAsync(value);
        }

        public AccessibilityManager(ILoggingService logger, IConfigurationService configService)
        {
            _logger = logger;
            _configService = configService;
            
            InitializeSystemSettings();
            LoadUserSettings();
            
            // Monitor system changes
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }

        public async Task ApplyAccessibilitySettingsAsync(AccessibilitySettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            
            try
            {
                var oldSettings = _currentSettings;
                _currentSettings = new AccessibilitySettings
                {
                    HighContrastEnabled = settings.HighContrastEnabled,
                    ScreenReaderActive = settings.ScreenReaderActive,
                    TextScaleFactor = Math.Max(0.5, Math.Min(3.0, settings.TextScaleFactor)),
                    ReducedMotion = settings.ReducedMotion,
                    KeyboardNavigationOnly = settings.KeyboardNavigationOnly,
                    CustomSettings = new System.Collections.Generic.Dictionary<string, object>(settings.CustomSettings ?? new System.Collections.Generic.Dictionary<string, object>())
                };
                
                await ApplySettingsToUIAsync();
                await SaveUserSettingsAsync();
                
                SettingsChanged?.Invoke(this, new AccessibilitySettingsChangedEventArgs
                {
                    OldSettings = oldSettings,
                    NewSettings = _currentSettings,
                    Timestamp = DateTime.UtcNow
                });
                
                _logger.LogInfo("Accessibility settings applied successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to apply accessibility settings", ex);
                throw;
            }
        }

        public async Task<AccessibilitySettings> GetCurrentSettingsAsync()
        {
            return await Task.FromResult(new AccessibilitySettings
            {
                HighContrastEnabled = IsHighContrastEnabled,
                ScreenReaderActive = IsScreenReaderActive,
                TextScaleFactor = TextScaleFactor,
                ReducedMotion = _currentSettings?.ReducedMotion ?? false,
                KeyboardNavigationOnly = _currentSettings?.KeyboardNavigationOnly ?? false,
                CustomSettings = _currentSettings?.CustomSettings ?? new System.Collections.Generic.Dictionary<string, object>()
            });
        }

        private void InitializeSystemSettings()
        {
            try
            {
                _systemHighContrast = SystemParameters.HighContrast;
                _systemScreenReader = IsScreenReaderRunning();
                _systemTextScale = GetSystemTextScale();
                
                _logger.LogDebug($"System accessibility: HighContrast={_systemHighContrast}, ScreenReader={_systemScreenReader}, TextScale={_systemTextScale}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to initialize system accessibility settings", ex);
                _systemHighContrast = false;
                _systemScreenReader = false;
                _systemTextScale = 1.0;
            }
        }

        private void LoadUserSettings()
        {
            try
            {
                _currentSettings = new AccessibilitySettings
                {
                    HighContrastEnabled = _configService.GetValue("Accessibility:HighContrast", _systemHighContrast),
                    ScreenReaderActive = _configService.GetValue("Accessibility:ScreenReader", _systemScreenReader),
                    TextScaleFactor = _configService.GetValue("Accessibility:TextScale", _systemTextScale),
                    ReducedMotion = _configService.GetValue("Accessibility:ReducedMotion", false),
                    KeyboardNavigationOnly = _configService.GetValue("Accessibility:KeyboardOnly", false),
                    CustomSettings = _configService.GetValue<System.Collections.Generic.Dictionary<string, object>>("Accessibility:Custom") ?? 
                                   new System.Collections.Generic.Dictionary<string, object>()
                };
                
                // Apply settings on startup
                _ = ApplySettingsToUIAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to load user accessibility settings", ex);
                _currentSettings = new AccessibilitySettings
                {
                    HighContrastEnabled = _systemHighContrast,
                    ScreenReaderActive = _systemScreenReader,
                    TextScaleFactor = _systemTextScale
                };
            }
        }

        private async Task SaveUserSettingsAsync()
        {
            try
            {
                await _configService.SetValueAsync("Accessibility:HighContrast", _currentSettings.HighContrastEnabled);
                await _configService.SetValueAsync("Accessibility:ScreenReader", _currentSettings.ScreenReaderActive);
                await _configService.SetValueAsync("Accessibility:TextScale", _currentSettings.TextScaleFactor);
                await _configService.SetValueAsync("Accessibility:ReducedMotion", _currentSettings.ReducedMotion);
                await _configService.SetValueAsync("Accessibility:KeyboardOnly", _currentSettings.KeyboardNavigationOnly);
                await _configService.SetValueAsync("Accessibility:Custom", _currentSettings.CustomSettings);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to save accessibility settings", ex);
            }
        }

        private async Task ApplySettingsToUIAsync()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var app = Application.Current;
                    
                    // Apply text scaling
                    var textScale = _currentSettings.TextScaleFactor;
                    app.Resources["AccessibilityTextScale"] = textScale;
                    
                    // Apply high contrast settings
                    if (_currentSettings.HighContrastEnabled)
                    {
                        ApplyHighContrastTheme();
                    }
                    
                    // Apply reduced motion settings
                    if (_currentSettings.ReducedMotion)
                    {
                        app.Resources["AccessibilityAnimationDuration"] = TimeSpan.Zero;
                        app.Resources["AccessibilityTransitionDuration"] = TimeSpan.Zero;
                    }
                    else
                    {
                        app.Resources["AccessibilityAnimationDuration"] = TimeSpan.FromMilliseconds(300);
                        app.Resources["AccessibilityTransitionDuration"] = TimeSpan.FromMilliseconds(200);
                    }
                    
                    // Apply keyboard navigation settings
                    if (_currentSettings.KeyboardNavigationOnly)
                    {
                        app.Resources["AccessibilityFocusVisualStyle"] = CreateEnhancedFocusVisual();
                    }
                    
                    // Apply custom settings
                    foreach (var setting in _currentSettings.CustomSettings ?? new System.Collections.Generic.Dictionary<string, object>())
                    {
                        try
                        {
                            app.Resources[$"Accessibility.{setting.Key}"] = setting.Value;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to apply custom accessibility setting: {setting.Key}", ex);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to apply accessibility settings to UI", ex);
            }
        }

        private void ApplyHighContrastTheme()
        {
            try
            {
                var app = Application.Current;
                
                // High contrast colors
                app.Resources["HighContrastBackground"] = new SolidColorBrush(Colors.Black);
                app.Resources["HighContrastForeground"] = new SolidColorBrush(Colors.White);
                app.Resources["HighContrastAccent"] = new SolidColorBrush(Colors.Yellow);
                app.Resources["HighContrastBorder"] = new SolidColorBrush(Colors.White);
                app.Resources["HighContrastSelection"] = new SolidColorBrush(Colors.Blue);
                
                // Override default brushes
                app.Resources[SystemColors.WindowBrushKey] = app.Resources["HighContrastBackground"];
                app.Resources[SystemColors.WindowTextBrushKey] = app.Resources["HighContrastForeground"];
                app.Resources[SystemColors.HighlightBrushKey] = app.Resources["HighContrastSelection"];
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to apply high contrast theme", ex);
            }
        }

        private Style CreateEnhancedFocusVisual()
        {
            var style = new Style();
            
            var setter = new Setter
            {
                Property = Control.TemplateProperty,
                Value = new ControlTemplate
                {
                    TargetType = typeof(Control)
                }
            };
            
            // Enhanced focus rectangle
            var factory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Rectangle));
            factory.SetValue(System.Windows.Shapes.Rectangle.StrokeProperty, new SolidColorBrush(Colors.Yellow));
            factory.SetValue(System.Windows.Shapes.Rectangle.StrokeThicknessProperty, 3.0);
            factory.SetValue(System.Windows.Shapes.Rectangle.StrokeDashArrayProperty, new DoubleCollection { 2, 2 });
            
            ((ControlTemplate)setter.Value).VisualTree = factory;
            style.Setters.Add(setter);
            
            return style;
        }

        private bool IsScreenReaderRunning()
        {
            try
            {
                // Check for common screen readers
                var processes = System.Diagnostics.Process.GetProcesses();
                var screenReaderProcesses = new[] { "nvda", "jaws", "sapi", "narrator", "windowseyes", "zoomtext" };
                
                foreach (var process in processes)
                {
                    try
                    {
                        var processName = process.ProcessName.ToLowerInvariant();
                        if (Array.Exists(screenReaderProcesses, sr => processName.Contains(sr)))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Ignore access denied errors
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to detect screen reader", ex);
                return false;
            }
        }

        private double GetSystemTextScale()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Accessibility");
                var value = key?.GetValue("TextScaleFactor");
                
                if (value is int intValue)
                {
                    return intValue / 100.0; // Convert from percentage
                }
                
                return 1.0; // Default scale
            }
            catch
            {
                return 1.0; // Default scale
            }
        }

        private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Accessibility)
            {
                _ = HandleAccessibilityChangeAsync();
            }
        }

        private async Task HandleAccessibilityChangeAsync()
        {
            try
            {
                var oldHighContrast = _systemHighContrast;
                var oldScreenReader = _systemScreenReader;
                var oldTextScale = _systemTextScale;
                
                InitializeSystemSettings();
                
                // If user hasn't overridden system settings, update them
                bool settingsChanged = false;
                
                if (_currentSettings.HighContrastEnabled == oldHighContrast && _systemHighContrast != oldHighContrast)
                {
                    _currentSettings.HighContrastEnabled = _systemHighContrast;
                    settingsChanged = true;
                }
                
                if (_currentSettings.ScreenReaderActive == oldScreenReader && _systemScreenReader != oldScreenReader)
                {
                    _currentSettings.ScreenReaderActive = _systemScreenReader;
                    settingsChanged = true;
                }
                
                if (Math.Abs(_currentSettings.TextScaleFactor - oldTextScale) < 0.01 && Math.Abs(_systemTextScale - oldTextScale) > 0.01)
                {
                    _currentSettings.TextScaleFactor = _systemTextScale;
                    settingsChanged = true;
                }
                
                if (settingsChanged)
                {
                    await ApplySettingsToUIAsync();
                    await SaveUserSettingsAsync();
                    
                    SettingsChanged?.Invoke(this, new AccessibilitySettingsChangedEventArgs
                    {
                        OldSettings = new AccessibilitySettings
                        {
                            HighContrastEnabled = oldHighContrast,
                            ScreenReaderActive = oldScreenReader,
                            TextScaleFactor = oldTextScale
                        },
                        NewSettings = _currentSettings,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to handle system preference change", ex);
            }
        }

        private async void SetTextScaleFactorAsync(double value)
        {
            var clampedValue = Math.Max(0.5, Math.Min(3.0, value));
            
            if (Math.Abs(_currentSettings.TextScaleFactor - clampedValue) < 0.01)
                return;
            
            var oldSettings = await GetCurrentSettingsAsync();
            _currentSettings.TextScaleFactor = clampedValue;
            
            await ApplySettingsToUIAsync();
            await SaveUserSettingsAsync();
            
            SettingsChanged?.Invoke(this, new AccessibilitySettingsChangedEventArgs
            {
                OldSettings = oldSettings,
                NewSettings = _currentSettings,
                Timestamp = DateTime.UtcNow
            });
        }
    }
}