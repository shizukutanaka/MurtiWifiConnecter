using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly string _configPath;
        private readonly ILoggingService _logger;
        private Dictionary<string, object> _configuration;
        private readonly SemaphoreSlim _configLock = new SemaphoreSlim(1, 1);

        public event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;

        public ConfigurationService(ILoggingService logger)
        {
            _logger = logger;
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MurtiWifiConnecter",
                "config.json"
            );
            
            _configuration = new Dictionary<string, object>();
            LoadConfiguration();
        }

        public T GetValue<T>(string key, T defaultValue = default)
        {
            try
            {
                if (_configuration.TryGetValue(key, out var value))
                {
                    if (value is JsonElement jsonElement)
                    {
                        return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
                    }
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to get configuration value for key: {key}", ex);
                return defaultValue;
            }
        }

        public async Task SetValueAsync<T>(string key, T value)
        {
            await _configLock.WaitAsync();
            try
            {
                var oldValue = _configuration.ContainsKey(key) ? _configuration[key] : null;
                _configuration[key] = value;
                
                await SaveConfigurationAsync();
                
                ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs
                {
                    Key = key,
                    OldValue = oldValue,
                    NewValue = value
                });
                
                _logger.LogInfo($"Configuration updated: {key}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to set configuration value for key: {key}", ex);
                throw;
            }
            finally
            {
                _configLock.Release();
            }
        }

        public async Task<Dictionary<string, object>> GetAllAsync()
        {
            await _configLock.WaitAsync();
            try
            {
                return new Dictionary<string, object>(_configuration);
            }
            finally
            {
                _configLock.Release();
            }
        }

        public async Task ReloadAsync()
        {
            await _configLock.WaitAsync();
            try
            {
                LoadConfiguration();
                _logger.LogInfo("Configuration reloaded");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to reload configuration", ex);
                throw;
            }
            finally
            {
                _configLock.Release();
            }
        }

        public async Task ResetToDefaultsAsync()
        {
            await _configLock.WaitAsync();
            try
            {
                _configuration = GetDefaultConfiguration();
                await SaveConfigurationAsync();
                
                ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs
                {
                    Key = "*",
                    OldValue = null,
                    NewValue = _configuration
                });
                
                _logger.LogInfo("Configuration reset to defaults");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to reset configuration", ex);
                throw;
            }
            finally
            {
                _configLock.Release();
            }
        }

        public bool ContainsKey(string key)
        {
            return _configuration.ContainsKey(key);
        }

        public async Task RemoveKeyAsync(string key)
        {
            await _configLock.WaitAsync();
            try
            {
                if (_configuration.ContainsKey(key))
                {
                    var oldValue = _configuration[key];
                    _configuration.Remove(key);
                    
                    await SaveConfigurationAsync();
                    
                    ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs
                    {
                        Key = key,
                        OldValue = oldValue,
                        NewValue = null
                    });
                    
                    _logger.LogInfo($"Configuration key removed: {key}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to remove configuration key: {key}", ex);
                throw;
            }
            finally
            {
                _configLock.Release();
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _configuration = JsonSerializer.Deserialize<Dictionary<string, object>>(json) 
                        ?? GetDefaultConfiguration();
                }
                else
                {
                    _configuration = GetDefaultConfiguration();
                    Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
                    SaveConfigurationAsync().Wait();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to load configuration, using defaults", ex);
                _configuration = GetDefaultConfiguration();
            }
        }

        private async Task SaveConfigurationAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_configuration, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(_configPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to save configuration", ex);
                throw;
            }
        }

        private Dictionary<string, object> GetDefaultConfiguration()
        {
            return new Dictionary<string, object>
            {
                ["Connection:AutoConnect"] = true,
                ["Connection:MaxRetries"] = 3,
                ["Connection:RetryDelay"] = 2000,
                ["Connection:ConnectionTimeout"] = 30000,
                ["Connection:PreferredNetworks"] = new List<string>(),
                
                ["Network:ScanInterval"] = 30000,
                ["Network:ConnectivityTestHosts"] = new[] { "8.8.8.8", "1.1.1.1", "google.com" },
                ["Network:MinSignalStrength"] = -80,
                
                ["UI:Theme"] = "System",
                ["UI:Language"] = "en-US",
                ["UI:ShowNotifications"] = true,
                ["UI:MinimizeToTray"] = true,
                ["UI:StartMinimized"] = false,
                
                ["Security:StorePasswords"] = true,
                ["Security:EncryptionEnabled"] = true,
                ["Security:RequireAdminMode"] = false,
                
                ["Logging:Level"] = "Information",
                ["Logging:MaxFileSize"] = 10485760,
                ["Logging:MaxFiles"] = 5,
                ["Logging:EnableFileLogging"] = true,
                
                ["Performance:EnableHardwareAcceleration"] = true,
                ["Performance:MaxConcurrentScans"] = 3,
                ["Performance:CacheTimeout"] = 300000,
                
                ["Updates:CheckForUpdates"] = true,
                ["Updates:AutoDownload"] = false,
                ["Updates:UpdateChannel"] = "stable"
            };
        }
    }

    public class ConfigurationChangedEventArgs : EventArgs
    {
        public string Key { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
    }
}