using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace MurtiWifiConnecter.UserExperience
{
    /// <summary>
    /// 多言語化管理クラス
    /// </summary>
    public class LocalizationManager
    {
        private static LocalizationManager _instance;
        private Dictionary<string, Dictionary<string, string>> _translations;
        private CultureInfo _currentCulture;
        private readonly string _defaultLanguage = "ja-JP";

        public static LocalizationManager Instance => _instance ??= new LocalizationManager();

        public CultureInfo CurrentCulture => _currentCulture;

        public event Action<CultureInfo> LanguageChanged;

        private LocalizationManager()
        {
            _translations = new Dictionary<string, Dictionary<string, string>>();
            _currentCulture = CultureInfo.CurrentCulture;
            LoadDefaultTranslations();
        }

        /// <summary>
        /// 言語を設定
        /// </summary>
        public void SetLanguage(string languageCode)
        {
            try
            {
                var culture = new CultureInfo(languageCode);
                _currentCulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                
                LanguageChanged?.Invoke(culture);
            }
            catch (CultureNotFoundException)
            {
                // フォールバックとしてデフォルト言語を使用
                SetLanguage(_defaultLanguage);
            }
        }

        /// <summary>
        /// 翻訳されたテキストを取得
        /// </summary>
        public string GetText(string key, params object[] args)
        {
            var languageCode = _currentCulture.Name;
            
            // 現在の言語での翻訳を試行
            if (_translations.TryGetValue(languageCode, out var translations) && 
                translations.TryGetValue(key, out var translation))
            {
                return args.Length > 0 ? string.Format(translation, args) : translation;
            }

            // フォールバック: 日本語
            if (languageCode != _defaultLanguage && 
                _translations.TryGetValue(_defaultLanguage, out var defaultTranslations) &&
                defaultTranslations.TryGetValue(key, out var defaultTranslation))
            {
                return args.Length > 0 ? string.Format(defaultTranslation, args) : defaultTranslation;
            }

            // フォールバック: 英語
            if (_translations.TryGetValue("en-US", out var enTranslations) &&
                enTranslations.TryGetValue(key, out var enTranslation))
            {
                return args.Length > 0 ? string.Format(enTranslation, args) : enTranslation;
            }

            // 最終フォールバック: キー自体を返す
            return key;
        }

        /// <summary>
        /// 翻訳辞書を追加
        /// </summary>
        public void AddTranslations(string languageCode, Dictionary<string, string> translations)
        {
            if (!_translations.ContainsKey(languageCode))
            {
                _translations[languageCode] = new Dictionary<string, string>();
            }

            foreach (var kvp in translations)
            {
                _translations[languageCode][kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// 翻訳ファイルを読み込み
        /// </summary>
        public void LoadTranslationsFromFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var jsonContent = File.ReadAllText(filePath);
                    var languageData = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(jsonContent);
                    
                    foreach (var language in languageData)
                    {
                        AddTranslations(language.Key, language.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"翻訳ファイルの読み込みに失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 利用可能な言語一覧を取得
        /// </summary>
        public List<LanguageInfo> GetAvailableLanguages()
        {
            var languages = new List<LanguageInfo>();
            
            foreach (var languageCode in _translations.Keys)
            {
                try
                {
                    var culture = new CultureInfo(languageCode);
                    languages.Add(new LanguageInfo
                    {
                        Code = languageCode,
                        Name = culture.DisplayName,
                        NativeName = culture.NativeName,
                        IsRightToLeft = culture.TextInfo.IsRightToLeft
                    });
                }
                catch (CultureNotFoundException)
                {
                    // 無効なカルチャコードは無視
                }
            }

            return languages;
        }

        /// <summary>
        /// 数値のローカライズ
        /// </summary>
        public string FormatNumber(double number, int decimalPlaces = 2)
        {
            return number.ToString($"N{decimalPlaces}", _currentCulture);
        }

        /// <summary>
        /// 日付のローカライズ
        /// </summary>
        public string FormatDate(DateTime date, string format = "d")
        {
            return date.ToString(format, _currentCulture);
        }

        /// <summary>
        /// 通貨のローカライズ
        /// </summary>
        public string FormatCurrency(decimal amount)
        {
            return amount.ToString("C", _currentCulture);
        }

        /// <summary>
        /// デフォルトの翻訳を読み込み
        /// </summary>
        private void LoadDefaultTranslations()
        {
            // 日本語翻訳
            var japaneseTranslations = new Dictionary<string, string>
            {
                // 一般的なUI要素
                { "OK", "OK" },
                { "Cancel", "キャンセル" },
                { "Yes", "はい" },
                { "No", "いいえ" },
                { "Save", "保存" },
                { "Load", "読み込み" },
                { "Delete", "削除" },
                { "Edit", "編集" },
                { "Add", "追加" },
                { "Remove", "削除" },
                { "Close", "閉じる" },
                { "Exit", "終了" },
                { "Help", "ヘルプ" },
                { "Settings", "設定" },
                
                // WiFi関連
                { "WiFi.Scan", "スキャン" },
                { "WiFi.Connect", "接続" },
                { "WiFi.Disconnect", "切断" },
                { "WiFi.Refresh", "更新" },
                { "WiFi.SSID", "SSID" },
                { "WiFi.Password", "パスワード" },
                { "WiFi.SignalStrength", "信号強度" },
                { "WiFi.Security", "セキュリティ" },
                { "WiFi.Status", "状態" },
                { "WiFi.Connected", "接続済み" },
                { "WiFi.Disconnected", "未接続" },
                { "WiFi.Connecting", "接続中..." },
                
                // メッセージ
                { "Message.Success", "成功" },
                { "Message.Error", "エラー" },
                { "Message.Warning", "警告" },
                { "Message.Info", "情報" },
                { "Message.ConnectionSuccess", "WiFiに正常に接続しました" },
                { "Message.ConnectionFailed", "WiFi接続に失敗しました" },
                { "Message.ScanCompleted", "ネットワークスキャンが完了しました" },
                { "Message.InvalidPassword", "パスワードが正しくありません" },
                { "Message.NetworkNotFound", "指定されたネットワークが見つかりません" },
                
                // エラーメッセージ
                { "Error.NetworkAdapter", "ネットワークアダプターが見つかりません" },
                { "Error.NoNetworks", "利用可能なネットワークがありません" },
                { "Error.ConnectionTimeout", "接続がタイムアウトしました" },
                { "Error.AuthenticationFailed", "認証に失敗しました" },
                { "Error.UnknownError", "不明なエラーが発生しました" },
                
                // 設定
                { "Settings.General", "全般" },
                { "Settings.Network", "ネットワーク" },
                { "Settings.Security", "セキュリティ" },
                { "Settings.Advanced", "詳細設定" },
                { "Settings.Language", "言語" },
                { "Settings.Theme", "テーマ" },
                { "Settings.Notifications", "通知" },
                
                // アクセシビリティ
                { "Accessibility.HighContrast", "ハイコントラスト" },
                { "Accessibility.LargeText", "大きなテキスト" },
                { "Accessibility.ScreenReader", "スクリーンリーダー" },
                { "Accessibility.KeyboardNavigation", "キーボードナビゲーション" }
            };

            // 英語翻訳
            var englishTranslations = new Dictionary<string, string>
            {
                // 一般的なUI要素
                { "OK", "OK" },
                { "Cancel", "Cancel" },
                { "Yes", "Yes" },
                { "No", "No" },
                { "Save", "Save" },
                { "Load", "Load" },
                { "Delete", "Delete" },
                { "Edit", "Edit" },
                { "Add", "Add" },
                { "Remove", "Remove" },
                { "Close", "Close" },
                { "Exit", "Exit" },
                { "Help", "Help" },
                { "Settings", "Settings" },
                
                // WiFi関連
                { "WiFi.Scan", "Scan" },
                { "WiFi.Connect", "Connect" },
                { "WiFi.Disconnect", "Disconnect" },
                { "WiFi.Refresh", "Refresh" },
                { "WiFi.SSID", "SSID" },
                { "WiFi.Password", "Password" },
                { "WiFi.SignalStrength", "Signal Strength" },
                { "WiFi.Security", "Security" },
                { "WiFi.Status", "Status" },
                { "WiFi.Connected", "Connected" },
                { "WiFi.Disconnected", "Disconnected" },
                { "WiFi.Connecting", "Connecting..." },
                
                // メッセージ
                { "Message.Success", "Success" },
                { "Message.Error", "Error" },
                { "Message.Warning", "Warning" },
                { "Message.Info", "Information" },
                { "Message.ConnectionSuccess", "Successfully connected to WiFi" },
                { "Message.ConnectionFailed", "Failed to connect to WiFi" },
                { "Message.ScanCompleted", "Network scan completed" },
                { "Message.InvalidPassword", "Invalid password" },
                { "Message.NetworkNotFound", "Network not found" },
                
                // エラーメッセージ
                { "Error.NetworkAdapter", "Network adapter not found" },
                { "Error.NoNetworks", "No networks available" },
                { "Error.ConnectionTimeout", "Connection timeout" },
                { "Error.AuthenticationFailed", "Authentication failed" },
                { "Error.UnknownError", "Unknown error occurred" },
                
                // 設定
                { "Settings.General", "General" },
                { "Settings.Network", "Network" },
                { "Settings.Security", "Security" },
                { "Settings.Advanced", "Advanced" },
                { "Settings.Language", "Language" },
                { "Settings.Theme", "Theme" },
                { "Settings.Notifications", "Notifications" },
                
                // アクセシビリティ
                { "Accessibility.HighContrast", "High Contrast" },
                { "Accessibility.LargeText", "Large Text" },
                { "Accessibility.ScreenReader", "Screen Reader" },
                { "Accessibility.KeyboardNavigation", "Keyboard Navigation" }
            };

            AddTranslations("ja-JP", japaneseTranslations);
            AddTranslations("en-US", englishTranslations);
        }

        /// <summary>
        /// 言語設定をファイルに保存
        /// </summary>
        public void SaveLanguagePreference()
        {
            try
            {
                var settings = new { Language = _currentCulture.Name };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText("language_settings.json", json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"言語設定の保存に失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 言語設定をファイルから読み込み
        /// </summary>
        public void LoadLanguagePreference()
        {
            try
            {
                if (File.Exists("language_settings.json"))
                {
                    var json = File.ReadAllText("language_settings.json");
                    var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    
                    if (settings.TryGetValue("Language", out var languageObj) && 
                        languageObj is JsonElement element && 
                        element.ValueKind == JsonValueKind.String)
                    {
                        SetLanguage(element.GetString());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"言語設定の読み込みに失敗: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 言語情報
    /// </summary>
    public class LanguageInfo
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string NativeName { get; set; }
        public bool IsRightToLeft { get; set; }
    }

    /// <summary>
    /// 多言語化拡張メソッド
    /// </summary>
    public static class LocalizationExtensions
    {
        /// <summary>
        /// 文字列の多言語化拡張メソッド
        /// </summary>
        public static string Localize(this string key, params object[] args)
        {
            return LocalizationManager.Instance.GetText(key, args);
        }

        /// <summary>
        /// 数値のローカライズ拡張メソッド
        /// </summary>
        public static string ToLocalizedString(this double number, int decimalPlaces = 2)
        {
            return LocalizationManager.Instance.FormatNumber(number, decimalPlaces);
        }

        /// <summary>
        /// 日付のローカライズ拡張メソッド
        /// </summary>
        public static string ToLocalizedString(this DateTime date, string format = "d")
        {
            return LocalizationManager.Instance.FormatDate(date, format);
        }

        /// <summary>
        /// 通貨のローカライズ拡張メソッド
        /// </summary>
        public static string ToLocalizedCurrency(this decimal amount)
        {
            return LocalizationManager.Instance.FormatCurrency(amount);
        }
    }
}