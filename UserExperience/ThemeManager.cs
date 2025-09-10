using System;
using System.Drawing;
using System.Collections.Generic;

namespace MurtiWifiConnecter.UserExperience
{
    /// <summary>
    /// テーマ管理クラス
    /// </summary>
    public class ThemeManager
    {
        private static ThemeManager _instance;
        private Theme _currentTheme;
        private readonly Dictionary<string, Theme> _themes;

        public static ThemeManager Instance => _instance ??= new ThemeManager();

        public Theme CurrentTheme => _currentTheme;

        private ThemeManager()
        {
            _themes = new Dictionary<string, Theme>();
            InitializeDefaultThemes();
            _currentTheme = _themes["Light"];
        }

        /// <summary>
        /// デフォルトテーマを初期化
        /// </summary>
        private void InitializeDefaultThemes()
        {
            // ライトテーマ
            _themes["Light"] = new Theme
            {
                Name = "Light",
                BackgroundColor = Color.White,
                ForegroundColor = Color.Black,
                PrimaryColor = Color.FromArgb(52, 152, 219),
                SecondaryColor = Color.FromArgb(149, 165, 166),
                AccentColor = Color.FromArgb(46, 204, 113),
                ErrorColor = Color.FromArgb(231, 76, 60),
                WarningColor = Color.FromArgb(241, 196, 15),
                SuccessColor = Color.FromArgb(46, 204, 113),
                BorderColor = Color.FromArgb(220, 220, 220),
                HoverColor = Color.FromArgb(245, 245, 245)
            };

            // ダークテーマ
            _themes["Dark"] = new Theme
            {
                Name = "Dark",
                BackgroundColor = Color.FromArgb(30, 30, 30),
                ForegroundColor = Color.FromArgb(240, 240, 240),
                PrimaryColor = Color.FromArgb(41, 128, 185),
                SecondaryColor = Color.FromArgb(127, 140, 141),
                AccentColor = Color.FromArgb(39, 174, 96),
                ErrorColor = Color.FromArgb(192, 57, 43),
                WarningColor = Color.FromArgb(243, 156, 18),
                SuccessColor = Color.FromArgb(39, 174, 96),
                BorderColor = Color.FromArgb(60, 60, 60),
                HoverColor = Color.FromArgb(45, 45, 45)
            };

            // ハイコントラストテーマ
            _themes["HighContrast"] = new Theme
            {
                Name = "HighContrast",
                BackgroundColor = Color.Black,
                ForegroundColor = Color.White,
                PrimaryColor = Color.Yellow,
                SecondaryColor = Color.Cyan,
                AccentColor = Color.Lime,
                ErrorColor = Color.Red,
                WarningColor = Color.Orange,
                SuccessColor = Color.Lime,
                BorderColor = Color.White,
                HoverColor = Color.FromArgb(40, 40, 40)
            };
        }

        /// <summary>
        /// テーマを変更
        /// </summary>
        public void SetTheme(string themeName)
        {
            if (_themes.ContainsKey(themeName))
            {
                _currentTheme = _themes[themeName];
                OnThemeChanged?.Invoke(_currentTheme);
            }
        }

        /// <summary>
        /// カスタムテーマを追加
        /// </summary>
        public void AddCustomTheme(Theme theme)
        {
            if (!string.IsNullOrEmpty(theme.Name))
            {
                _themes[theme.Name] = theme;
            }
        }

        /// <summary>
        /// 利用可能なテーマ名を取得
        /// </summary>
        public IEnumerable<string> GetAvailableThemes()
        {
            return _themes.Keys;
        }

        /// <summary>
        /// テーマ変更イベント
        /// </summary>
        public event Action<Theme> OnThemeChanged;
    }

    /// <summary>
    /// テーマ定義
    /// </summary>
    public class Theme
    {
        public string Name { get; set; }
        public Color BackgroundColor { get; set; }
        public Color ForegroundColor { get; set; }
        public Color PrimaryColor { get; set; }
        public Color SecondaryColor { get; set; }
        public Color AccentColor { get; set; }
        public Color ErrorColor { get; set; }
        public Color WarningColor { get; set; }
        public Color SuccessColor { get; set; }
        public Color BorderColor { get; set; }
        public Color HoverColor { get; set; }

        /// <summary>
        /// フォント設定
        /// </summary>
        public FontSettings FontSettings { get; set; } = new FontSettings();
    }

    /// <summary>
    /// フォント設定
    /// </summary>
    public class FontSettings
    {
        public string FontFamily { get; set; } = "Segoe UI";
        public float DefaultSize { get; set; } = 9.0f;
        public float HeaderSize { get; set; } = 14.0f;
        public float SubHeaderSize { get; set; } = 11.0f;
        public float SmallSize { get; set; } = 8.0f;
    }
}