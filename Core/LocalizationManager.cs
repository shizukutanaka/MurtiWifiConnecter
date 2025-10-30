using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net.Http;
using System.Text;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// 国際化・多言語対応マネージャー（50言語対応強化版）
    /// エンタープライズレベルのローカライゼーション管理
    /// </summary>
    public static class LocalizationManager
    {
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _localizations = new();
        private static readonly ConcurrentDictionary<string, LanguagePackInfo> _languagePackInfo = new();
        private static readonly string _localizationDirectory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Languages");

        private static string _currentLanguage = "en";
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly Timer _translationUpdateTimer;

        // 50言語対応 - 主要言語 + 地域言語
        private static readonly HashSet<string> _supportedLanguages = new()
        {
            // 北米・欧州主要言語
            "en", "en-US", "en-GB", "es", "es-ES", "es-MX", "fr", "fr-FR", "fr-CA",
            "de", "de-DE", "de-AT", "it", "it-IT", "pt", "pt-BR", "pt-PT",
            "ru", "ru-RU", "nl", "nl-NL", "sv", "sv-SE", "da", "da-DK",
            "no", "no-NO", "fi", "fi-FI", "pl", "pl-PL", "cs", "cs-CZ",

            // アジア言語
            "ja", "ja-JP", "zh-CN", "zh-TW", "zh-HK", "ko", "ko-KR",
            "hi", "hi-IN", "bn", "bn-BD", "pa", "pa-IN", "ta", "ta-IN",
            "te", "te-IN", "mr", "mr-IN", "gu", "gu-IN", "ur", "ur-PK",
            "ar", "ar-SA", "ar-AE", "fa", "fa-IR", "tr", "tr-TR",

            // アフリカ・中東言語
            "sw", "sw-KE", "ha", "ha-NG", "yo", "yo-NG", "am", "am-ET",
            "he", "he-IL", "ar-EG", "ar-MA",

            // 南米言語
            "pt-BR",

            // 東南アジア言語
            "th", "th-TH", "ms", "ms-MY", "id", "id-ID", "tl", "tl-PH",
            "vi", "vi-VN", "my", "my-MM", "km", "km-KH", "lo", "lo-LA",

            // その他
            "ka", "ka-GE", "uk", "uk-UA", "ro", "ro-RO", "hu", "hu-HU",
            "sk", "sk-SK", "sl", "sl-SI", "hr", "hr-HR", "sr", "sr-RS",
            "bs", "bs-BA", "mk", "mk-MK", "bg", "bg-BG", "el", "el-GR"
        };

        // 言語グループ（翻訳の品質向上のため）
        private static readonly Dictionary<string, string[]> _languageGroups = new()
        {
            ["germanic"] = new[] { "en", "de", "nl", "sv", "da", "no" },
            ["romance"] = new[] { "es", "fr", "it", "pt", "ro" },
            ["slavic"] = new[] { "ru", "pl", "cs", "sk", "sl", "hr", "sr", "bs", "mk", "bg", "uk" },
            ["sino-tibetan"] = new[] { "zh-CN", "zh-TW", "zh-HK" },
            ["japonic"] = new[] { "ja" },
            ["korean"] = new[] { "ko" },
            ["indo-aryan"] = new[] { "hi", "bn", "pa", "ta", "te", "mr", "gu" },
            ["semitic"] = new[] { "ar", "he", "fa" },
            ["turkic"] = new[] { "tr" },
            ["tai-kadai"] = new[] { "th", "lo" },
            ["austronesian"] = new[] { "ms", "id", "tl" },
            ["austroasiatic"] = new[] { "vi", "km" }
        };

        static LocalizationManager()
        {
            // デフォルトの英語ローカライゼーションをロード
            LoadDefaultLocalization();

            // 翻訳更新タイマーの初期化（24時間ごとに更新）
            _translationUpdateTimer = new Timer(UpdateTranslations, null,
                TimeSpan.FromHours(24), TimeSpan.FromHours(24));

            // 同期的に言語を設定
            _ = SetLanguageAsync(CultureInfo.CurrentUICulture.Name);
        }

        /// <summary>
        /// 言語パック情報を取得
        /// </summary>
        public static LanguagePackInfo GetLanguagePackInfo(string languageCode)
        {
            return _languagePackInfo.GetOrAdd(languageCode, code => new LanguagePackInfo
            {
                LanguageCode = code,
                IsLoaded = _localizations.ContainsKey(code),
                LastUpdated = DateTime.UtcNow,
                Coverage = CalculateCoverage(code)
            });
        }

        /// <summary>
        /// 言語パックの適用範囲を計算
        /// </summary>
        private static double CalculateCoverage(string languageCode)
        {
            if (!_localizations.TryGetValue(languageCode, out var localization))
                return 0.0;

            var englishKeys = _localizations["en"].Keys;
            var translatedKeys = localization.Keys;
            var coverage = (double)translatedKeys.Intersect(englishKeys).Count() / englishKeys.Count();
            return Math.Round(coverage, 2);
        }

        /// <summary>
        /// 翻訳APIを使用して不足する翻訳を補完
        /// </summary>
        public static async Task AutoTranslateMissingKeysAsync(string languageCode)
        {
            if (!_localizations.TryGetValue(languageCode, out var localization))
                return;

            var englishLocalization = _localizations["en"];
            var missingKeys = englishLocalization.Keys.Except(localization.Keys).ToList();

            if (!missingKeys.Any())
                return;

            await Logger.LogInfo($"Auto-translating {missingKeys.Count} missing keys for {languageCode}", nameof(LocalizationManager));

            foreach (var key in missingKeys)
            {
                try
                {
                    var englishText = englishLocalization[key];
                    var translatedText = await TranslateTextAsync(englishText, "en", languageCode);

                    if (!string.IsNullOrEmpty(translatedText))
                    {
                        localization[key] = translatedText;
                        await Logger.LogDebug($"Translated '{englishText}' to '{translatedText}' for key '{key}'", nameof(LocalizationManager));
                    }
                }
                catch (Exception ex)
                {
                    await Logger.LogWarning($"Failed to translate key '{key}': {ex.Message}", nameof(LocalizationManager));
                }
            }

            // 更新されたローカライゼーションを保存
            await SaveLocalizationAsync(languageCode, localization);
        }

        /// <summary>
        /// テキストを翻訳（外部API使用）
        /// </summary>
        private static async Task<string> TranslateTextAsync(string text, string sourceLang, string targetLang)
        {
            try
            {
                // 簡易実装：実際にはGoogle Translate APIやAzure Translatorなどのサービスを使用
                // ここではモック実装として、基本的な置換のみを行う

                // 特殊キーワードの翻訳
                var translatedText = text
                    .Replace("WiFi", GetLocalizedWifiTerm(targetLang))
                    .Replace("network", GetLocalizedNetworkTerm(targetLang))
                    .Replace("connection", GetLocalizedConnectionTerm(targetLang));

                // より高度な翻訳が必要な場合は外部APIを呼び出し
                if (translatedText == text && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRANSLATION_API_KEY")))
                {
                    translatedText = await CallExternalTranslationAPI(text, sourceLang, targetLang);
                }

                return translatedText;
            }
            catch
            {
                return text; // 翻訳失敗時は原文を返す
            }
        }

        /// <summary>
        /// 言語固有の用語を取得
        /// </summary>
        private static string GetLocalizedWifiTerm(string languageCode)
        {
            return languageCode.ToLower() switch
            {
                "ja" or "ja-jp" => "Wi-Fi",
                "zh-cn" => "Wi-Fi",
                "zh-tw" => "Wi-Fi",
                "ko" or "ko-kr" => "Wi-Fi",
                "de" => "WLAN",
                "fr" => "Wi-Fi",
                "es" => "Wi-Fi",
                "it" => "Wi-Fi",
                "pt" => "Wi-Fi",
                "ru" => "Wi-Fi",
                "ar" => "واي فاي",
                "hi" => "वाई-फाई",
                _ => "WiFi"
            };
        }

        private static string GetLocalizedNetworkTerm(string languageCode)
        {
            return languageCode.ToLower() switch
            {
                "ja" or "ja-jp" => "ネットワーク",
                "zh-cn" => "网络",
                "zh-tw" => "網路",
                "ko" or "ko-kr" => "네트워크",
                "de" => "Netzwerk",
                "fr" => "réseau",
                "es" => "red",
                "it" => "rete",
                "pt" => "rede",
                "ru" => "сеть",
                "ar" => "شبكة",
                "hi" => "नेटवर्क",
                _ => "network"
            };
        }

        private static string GetLocalizedConnectionTerm(string languageCode)
        {
            return languageCode.ToLower() switch
            {
                "ja" or "ja-jp" => "接続",
                "zh-cn" => "连接",
                "zh-tw" => "連接",
                "ko" or "ko-kr" => "연결",
                "de" => "Verbindung",
                "fr" => "connexion",
                "es" => "conexión",
                "it" => "connessione",
                "pt" => "conexão",
                "ru" => "подключение",
                "ar" => "الاتصال",
                "hi" => "कनेक्शन",
                _ => "connection"
            };
        }

        /// <summary>
        /// 外部翻訳APIを呼び出し（モック実装）
        /// </summary>
        private static async Task<string> CallExternalTranslationAPI(string text, string sourceLang, string targetLang)
        {
            // 実際の実装ではGoogle Translate API、Azure Translator、DeepLなどを呼び出し
            // ここではモックとして簡易処理
            await Task.Delay(100); // API呼び出しのシミュレーション
            return text + $" ({targetLang})"; // 実際には翻訳されたテキストを返す
        }

        /// <summary>
        /// 翻訳を定期的に更新
        /// </summary>
        private static void UpdateTranslations(object state)
        {
            Task.Run(async () =>
            {
                try
                {
                    foreach (var languageCode in _supportedLanguages.Where(code => code != "en"))
                    {
                        var packInfo = GetLanguagePackInfo(languageCode);
                        if (packInfo.Coverage < 0.8) // 適用範囲が80%未満の場合
                        {
                            await AutoTranslateMissingKeysAsync(languageCode);
                            packInfo.LastUpdated = DateTime.UtcNow;
                            packInfo.Coverage = CalculateCoverage(languageCode);
                        }
                    }

                    await Logger.LogInfo("Translation updates completed", nameof(LocalizationManager));
                }
                catch (Exception ex)
                {
                    await Logger.LogError($"Translation update failed: {ex.Message}", nameof(LocalizationManager), null, ex);
                }
            });
        }

        /// <summary>
        /// ローカライゼーションをファイルに保存
        /// </summary>
        private static async Task SaveLocalizationAsync(string languageCode, Dictionary<string, string> localization)
        {
            try
            {
                if (!Directory.Exists(_localizationDirectory))
                {
                    Directory.CreateDirectory(_localizationDirectory);
                }

                var filePath = Path.Combine(_localizationDirectory, $"{languageCode}.json");
                var json = JsonSerializer.Serialize(localization, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
                await Logger.LogInfo($"Saved localization file: {languageCode}", nameof(LocalizationManager));
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to save localization: {languageCode}", nameof(LocalizationManager), null, ex);
            }
        }

        /// <summary>
        /// 言語パックの検証
        /// </summary>
        public static async Task<LanguagePackValidationResult> ValidateLanguagePackAsync(string languageCode)
        {
            var result = new LanguagePackValidationResult
            {
                LanguageCode = languageCode,
                IsValid = false,
                Issues = new List<string>()
            };

            try
            {
                if (!_localizations.TryGetValue(languageCode, out var localization))
                {
                    result.Issues.Add("Language pack not loaded");
                    return result;
                }

                var englishKeys = _localizations["en"].Keys;

                // 必須キーのチェック
                var requiredKeys = new[] { "app.name", "ok", "cancel", "error" };
                foreach (var key in requiredKeys)
                {
                    if (!localization.ContainsKey(key) || string.IsNullOrWhiteSpace(localization[key]))
                    {
                        result.Issues.Add($"Missing required key: {key}");
                    }
                }

                // エンコーディングのチェック
                foreach (var kvp in localization)
                {
                    if (string.IsNullOrEmpty(kvp.Value))
                    {
                        result.Issues.Add($"Empty value for key: {kvp.Key}");
                    }
                }

                result.Coverage = CalculateCoverage(languageCode);
                result.TotalKeys = localization.Count;
                result.MissingKeys = englishKeys.Except(localization.Keys).Count();

                result.IsValid = !result.Issues.Any() && result.Coverage > 0.5;
            }
            catch (Exception ex)
            {
                result.Issues.Add($"Validation failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 言語パックの最適化
        /// </summary>
        public static async Task OptimizeLanguagePackAsync(string languageCode)
        {
            try
            {
                if (!_localizations.TryGetValue(languageCode, out var localization))
                    return;

                // 重複キーの削除
                var uniqueKeys = localization
                    .GroupBy(kvp => kvp.Key.ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.First().Value);

                // 無効な値のクリーンアップ
                var cleanedKeys = uniqueKeys
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Trim());

                _localizations[languageCode] = cleanedKeys;

                // 最適化されたバージョンを保存
                await SaveLocalizationAsync(languageCode, cleanedKeys);

                await Logger.LogInfo($"Optimized language pack: {languageCode}", nameof(LocalizationManager));
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to optimize language pack: {languageCode}", nameof(LocalizationManager), null, ex);
            }
        }

        private static async Task SetLanguageAsync(string languageCode)
        {
            var normalizedCode = NormalizeLanguageCode(languageCode);
            if (_supportedLanguages.Contains(normalizedCode))
            {
                _currentLanguage = normalizedCode;
            }
        }

        /// <summary>
        /// サポートされている言語の一覧を取得
        /// </summary>
        public static IReadOnlyCollection<string> SupportedLanguages => _supportedLanguages.ToList();

        /// <summary>
        /// 現在の言語を取得
        /// </summary>
        public static string CurrentLanguage => _currentLanguage;

        /// <summary>
        /// 言語を設定
        /// </summary>
        public static async Task<bool> SetLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                return false;

            var normalizedCode = NormalizeLanguageCode(languageCode);
            if (!_supportedLanguages.Contains(normalizedCode))
            {
                await Logger.LogWarning($"Unsupported language: {languageCode}", "LocalizationManager");
                return false;
            }

            try
            {
                // 英語のデフォルトをベースに読み込み
                if (!_localizations.ContainsKey(normalizedCode))
                {
                    await LoadLocalizationAsync(normalizedCode);
                }

                _currentLanguage = normalizedCode;
                await Logger.LogInfo($"Language changed to: {normalizedCode}", "LocalizationManager");

                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to set language to {languageCode}", "LocalizationManager", null, ex);
                return false;
            }
        }

        /// <summary>
        /// テキストをローカライズ
        /// </summary>
        public static string Localize(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            var normalizedKey = key.ToLowerInvariant();

            // 現在の言語でテキストを取得
            if (_localizations.TryGetValue(_currentLanguage, out var currentLocalization) &&
                currentLocalization.TryGetValue(normalizedKey, out var localizedText))
            {
                return args.Length > 0 ? string.Format(localizedText, args) : localizedText;
            }

            // 英語のフォールバック
            if (_currentLanguage != "en-US" &&
                _localizations.TryGetValue("en-US", out var englishLocalization) &&
                englishLocalization.TryGetValue(normalizedKey, out var englishText))
            {
                return args.Length > 0 ? string.Format(englishText, args) : englishText;
            }

            // デフォルト値としてキーを返す
            return key;
        }

        /// <summary>
        /// 言語コードを正規化
        /// </summary>
        private static string NormalizeLanguageCode(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                return "en";

            var code = languageCode.ToLowerInvariant();

            // 地域情報付きのコードの場合（例: zh-CN -> zh-CN）
            if (code.Contains('-') && _supportedLanguages.Contains(code))
            {
                return code;
            }

            // 2文字の言語コードの場合
            if (code.Length == 2)
            {
                // 完全な言語コードを探す
                var fullCode = _supportedLanguages.FirstOrDefault(lang => lang.StartsWith(code + "-") || lang == code);
                if (fullCode != null)
                    return fullCode;
            }

            // 直接一致するかチェック
            if (_supportedLanguages.Contains(code))
                return code;

            // デフォルトは英語
            return "en";
        }

        /// <summary>
        /// デフォルトの英語ローカライゼーションをロード
        /// </summary>
        private static void LoadDefaultLocalization()
        {
            var defaultLocalization = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // 基本UI要素
                ["app.name"] = "MurtiWifiConnecter",
                ["app.version"] = "Version",
                ["app.description"] = "Enterprise Wi-Fi Operations Utility",

                // 共通のメッセージ
                ["ok"] = "[OK]",
                ["fail"] = "[FAIL]",
                ["error"] = "Error",
                ["warning"] = "Warning",
                ["info"] = "Info",
                ["success"] = "Success",
                ["failed"] = "Failed",
                ["cancel"] = "Cancel",
                ["save"] = "Save",
                ["delete"] = "Delete",
                ["refresh"] = "Refresh",
                ["close"] = "Close",
                ["help"] = "Help",

                // メニュー項目
                ["menu.file"] = "File",
                ["menu.edit"] = "Edit",
                ["menu.view"] = "View",
                ["menu.tools"] = "Tools",
                ["menu.settings"] = "Settings",
                ["menu.language"] = "Language",

                // コマンド関連
                ["command_unknown"] = "Unknown command: {0}",
                ["command_empty"] = "Command cannot be empty.",
                ["command_help"] = "Type 'help' to see available commands.",
                ["command_executing"] = "Executing command: {0}",
                ["command_completed"] = "Command completed successfully",
                ["command_failed"] = "Command failed: {0}",

                // 接続関連
                ["connecting"] = "Connecting to {0}...",
                ["connected"] = "Connected to {0}",
                ["disconnected"] = "Disconnected",
                ["connection_failed"] = "Connection failed",
                ["connection_timeout"] = "Connection timeout",
                ["connection_success"] = "Connection successful",
                ["connection_error"] = "Connection error: {0}",
                ["reconnecting"] = "Reconnecting...",
                ["disconnecting"] = "Disconnecting...",

                // スキャン関連
                ["scanning"] = "Scanning for networks",
                ["networks_found"] = "Found {0} networks:",
                ["no_networks"] = "No networks found",
                ["scan_completed"] = "Network scan completed",
                ["scan_failed"] = "Network scan failed",

                // セキュリティ関連
                ["security_notice"] = "SECURITY NOTICE",
                ["security_monitored"] = "This system is monitored and audited for security compliance",
                ["security_prohibited"] = "Unauthorized access is prohibited and will be reported",
                ["security_logged"] = "All activities are logged and may be reviewed by administrators",
                ["security_scan"] = "Running security scan...",
                ["security_vulnerabilities"] = "Found {0} security vulnerabilities",

                // 認証関連
                ["auth_required"] = "Authentication required",
                ["auth_success"] = "Authentication successful",
                ["auth_failed"] = "Authentication failed",
                ["auth_invalid"] = "Invalid credentials",
                ["auth_locked"] = "Account locked due to security policy",

                // エラー関連
                ["admin_required"] = "Administrator privileges required",
                ["permission_denied"] = "Permission denied",
                ["invalid_input"] = "Invalid input",
                ["operation_timeout"] = "Operation timed out",
                ["network_unavailable"] = "Network unavailable",
                ["service_unavailable"] = "Service unavailable",

                // 設定関連
                ["config_saved"] = "Configuration saved",
                ["config_loaded"] = "Configuration loaded",
                ["config_invalid"] = "Invalid configuration",
                ["config_reset"] = "Configuration reset to defaults",
                ["settings_general"] = "General Settings",
                ["settings_network"] = "Network Settings",
                ["settings_security"] = "Security Settings",

                // ヘルプ関連
                ["help_available"] = "Available commands:",
                ["help_connect"] = "Connect to a WiFi network",
                ["help_disconnect"] = "Disconnect from current WiFi network",
                ["help_scan"] = "Scan for available WiFi networks",
                ["help_status"] = "Show current connection status",
                ["help_preferred"] = "Manage preferred networks",
                ["help_config"] = "Configure application settings",

                // ステータス関連
                ["status_connected"] = "Status: Connected",
                ["status_disconnected"] = "Status: Disconnected",
                ["status_connecting"] = "Status: Connecting",
                ["status_error"] = "Status: Error",
                ["signal_strength"] = "Signal: {0}%",
                ["ip_address"] = "IP: {0}",
                ["network_ssid"] = "SSID: {0}",
                ["network_bssid"] = "BSSID: {0}",
                ["network_security"] = "Security: {0}",
                ["network_band"] = "Band: {0}",

                // ログ関連
                ["log_saved"] = "Log saved to {0}",
                ["log_cleared"] = "Log cleared",
                ["log_exported"] = "Log exported to {0}",
                ["log_error"] = "Log error: {0}",

                // 監査関連
                ["audit_enabled"] = "Audit logging enabled",
                ["audit_disabled"] = "Audit logging disabled",
                ["audit_event"] = "Audit event recorded: {0}",

                // パフォーマンス関連
                ["performance_good"] = "Performance: Good",
                ["performance_warning"] = "Performance: Warning",
                ["performance_critical"] = "Performance: Critical",
                ["memory_usage"] = "Memory usage: {0} MB",
                ["cpu_usage"] = "CPU usage: {0}%",

                // 言語関連
                ["language_changed"] = "Language changed to {0}",
                ["language_not_supported"] = "Language '{0}' is not supported",
                ["language_auto_detected"] = "Language auto-detected: {0}",

                // 診断関連
                ["diagnostics_running"] = "Running diagnostics...",
                ["diagnostics_completed"] = "Diagnostics completed",
                ["diagnostics_failed"] = "Diagnostics failed: {0}",
                ["diagnostics_report"] = "Diagnostics report saved to {0}",

                // アップデート関連
                ["update_checking"] = "Checking for updates...",
                ["update_available"] = "Update available: {0}",
                ["update_current"] = "Application is up to date",
                ["update_downloading"] = "Downloading update...",
                ["update_installing"] = "Installing update...",
                ["update_complete"] = "Update completed successfully",

                // 通知関連
                ["notification_enabled"] = "Notifications enabled",
                ["notification_disabled"] = "Notifications disabled",
                ["notification_test"] = "Test notification sent",

                // バックアップ関連
                ["backup_created"] = "Backup created: {0}",
                ["backup_restored"] = "Backup restored: {0}",
                ["backup_failed"] = "Backup failed: {0}",
                ["restore_failed"] = "Restore failed: {0}",

                // レート制限関連
                ["rate_limit_exceeded"] = "Rate limit exceeded. Please wait {0} seconds.",
                ["rate_limit_reset"] = "Rate limit reset",

                // 優先ネットワーク関連
                ["preferred_added"] = "Network '{0}' added to preferred list",
                ["preferred_removed"] = "Network '{0}' removed from preferred list",
                ["preferred_cleared"] = "Preferred networks list cleared",
                ["preferred_empty"] = "No preferred networks configured",

                // プロファイル関連
                ["profile_created"] = "WiFi profile created for '{0}'",
                ["profile_updated"] = "WiFi profile updated for '{0}'",
                ["profile_deleted"] = "WiFi profile deleted for '{0}'",
                ["profile_imported"] = "WiFi profile imported: {0}",
                ["profile_exported"] = "WiFi profile exported: {0}",

                // 統計情報関連
                ["stats_total_connections"] = "Total connections: {0}",
                ["stats_successful_connections"] = "Successful connections: {0}",
                ["stats_failed_connections"] = "Failed connections: {0}",
                ["stats_success_rate"] = "Success rate: {0}%",
                ["stats_average_duration"] = "Average connection duration: {0}",
                ["stats_uptime"] = "Current uptime: {0}"
            };

            _localizations["en"] = defaultLocalization;
        }

        /// <summary>
        /// 指定された言語のローカライゼーションファイルを非同期でロード
        /// </summary>
        private static async Task LoadLocalizationAsync(string languageCode)
        {
            try
            {
                var filePath = Path.Combine(_localizationDirectory, $"{languageCode}.json");
                if (!File.Exists(filePath))
                {
                    await Logger.LogWarning($"Localization file not found: {filePath}", "LocalizationManager");
                    return;
                }

                var jsonContent = await File.ReadAllTextAsync(filePath);
                var localization = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);

                if (localization != null)
                {
                    _localizations[languageCode] = localization;
                    await Logger.LogInfo($"Loaded localization: {languageCode}", "LocalizationManager");
                }
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Failed to load localization: {languageCode}", "LocalizationManager", null, ex);
            }
        }

        /// <summary>
        /// 利用可能な言語の自動検出
        /// </summary>
        public static async Task<List<string>> DetectAvailableLanguagesAsync()
        {
            var availableLanguages = new List<string>();

            try
            {
                if (!Directory.Exists(_localizationDirectory))
                {
                    Directory.CreateDirectory(_localizationDirectory);
                }

                var files = Directory.GetFiles(_localizationDirectory, "*.json");
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (_supportedLanguages.Contains(fileName))
                    {
                        availableLanguages.Add(fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogError("Failed to detect available languages", "LocalizationManager", null, ex);
            }

            return availableLanguages;
        }

        /// <summary>
        /// 言語設定をシステムのカルチャに基づいて自動設定
        /// </summary>
        public static async Task<bool> AutoSetLanguageAsync()
        {
            var systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return await SetLanguage(systemLanguage);
        }

        /// <summary>
        /// 現在のローカライゼーション設定をエクスポート
        /// </summary>
        public static async Task<string> ExportCurrentLocalizationAsync()
        {
            if (_localizations.TryGetValue(_currentLanguage, out var localization))
            {
                return System.Text.Json.JsonSerializer.Serialize(localization, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }

            return "{}";
        }
    }
}
