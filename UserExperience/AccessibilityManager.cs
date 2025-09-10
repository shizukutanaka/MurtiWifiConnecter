using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MurtiWifiConnecter.UserExperience
{
    /// <summary>
    /// アクセシビリティ管理クラス
    /// </summary>
    public class AccessibilityManager
    {
        private static AccessibilityManager _instance;
        private AccessibilitySettings _settings;
        private readonly List<Control> _registeredControls;

        public static AccessibilityManager Instance => _instance ??= new AccessibilityManager();

        public AccessibilitySettings Settings => _settings;

        private AccessibilityManager()
        {
            _registeredControls = new List<Control>();
            _settings = new AccessibilitySettings
            {
                HighContrastEnabled = false,
                ScreenReaderActive = false,
                TextScaleFactor = 1.0,
                ReducedMotion = false,
                KeyboardNavigationOnly = false,
                CustomSettings = new Dictionary<string, object>()
            };
        }

        /// <summary>
        /// アクセシビリティ設定を適用
        /// </summary>
        public void ApplySettings(AccessibilitySettings settings)
        {
            _settings = settings;

            // ハイコントラストモードの適用
            if (settings.HighContrastEnabled)
            {
                ThemeManager.Instance.SetTheme("HighContrast");
            }

            // テキストスケールの適用
            foreach (var control in _registeredControls)
            {
                ApplyTextScaling(control, settings.TextScaleFactor);
            }

            // キーボードナビゲーションの設定
            if (settings.KeyboardNavigationOnly)
            {
                EnableKeyboardNavigation();
            }

            OnSettingsChanged?.Invoke(settings);
        }

        /// <summary>
        /// コントロールを登録
        /// </summary>
        public void RegisterControl(Control control)
        {
            if (!_registeredControls.Contains(control))
            {
                _registeredControls.Add(control);
                ApplyAccessibilityToControl(control);
            }
        }

        /// <summary>
        /// コントロールの登録解除
        /// </summary>
        public void UnregisterControl(Control control)
        {
            _registeredControls.Remove(control);
        }

        /// <summary>
        /// コントロールにアクセシビリティ設定を適用
        /// </summary>
        private void ApplyAccessibilityToControl(Control control)
        {
            // スクリーンリーダー用の説明を設定
            if (_settings.ScreenReaderActive)
            {
                SetScreenReaderProperties(control);
            }

            // テキストスケーリングを適用
            ApplyTextScaling(control, _settings.TextScaleFactor);

            // タブ順序を設定
            SetTabOrder(control);

            // キーボードショートカットを設定
            if (_settings.KeyboardNavigationOnly)
            {
                SetKeyboardShortcuts(control);
            }
        }

        /// <summary>
        /// スクリーンリーダー用のプロパティを設定
        /// </summary>
        private void SetScreenReaderProperties(Control control)
        {
            if (control is Button button)
            {
                button.AccessibleName = button.Text;
                button.AccessibleDescription = $"ボタン: {button.Text}を押す";
                button.AccessibleRole = AccessibleRole.PushButton;
            }
            else if (control is TextBox textBox)
            {
                textBox.AccessibleName = textBox.Name;
                textBox.AccessibleDescription = "テキスト入力フィールド";
                textBox.AccessibleRole = AccessibleRole.Text;
            }
            else if (control is Label label)
            {
                label.AccessibleName = label.Text;
                label.AccessibleRole = AccessibleRole.StaticText;
            }
            else if (control is ComboBox comboBox)
            {
                comboBox.AccessibleName = comboBox.Name;
                comboBox.AccessibleDescription = "ドロップダウンリスト";
                comboBox.AccessibleRole = AccessibleRole.ComboBox;
            }
        }

        /// <summary>
        /// テキストスケーリングを適用
        /// </summary>
        private void ApplyTextScaling(Control control, double scaleFactor)
        {
            if (control.Font != null)
            {
                var newSize = (float)(control.Font.Size * scaleFactor);
                control.Font = new System.Drawing.Font(
                    control.Font.FontFamily,
                    newSize,
                    control.Font.Style
                );
            }

            // 子コントロールにも適用
            foreach (Control child in control.Controls)
            {
                ApplyTextScaling(child, scaleFactor);
            }
        }

        /// <summary>
        /// タブ順序を設定
        /// </summary>
        private void SetTabOrder(Control control)
        {
            int tabIndex = 0;
            foreach (Control child in control.Controls)
            {
                if (child.CanFocus)
                {
                    child.TabIndex = tabIndex++;
                    child.TabStop = true;
                }
            }
        }

        /// <summary>
        /// キーボードショートカットを設定
        /// </summary>
        private void SetKeyboardShortcuts(Control control)
        {
            control.KeyDown += (sender, e) =>
            {
                // Escキーでフォーカスを親コントロールに移動
                if (e.KeyCode == Keys.Escape)
                {
                    control.Parent?.Focus();
                }
                // Tab/Shift+Tabでナビゲーション
                else if (e.KeyCode == Keys.Tab)
                {
                    SelectNextControl(control, !e.Shift, true, true, true);
                }
            };
        }

        /// <summary>
        /// キーボードナビゲーションを有効化
        /// </summary>
        private void EnableKeyboardNavigation()
        {
            foreach (var control in _registeredControls)
            {
                control.TabStop = true;
                SetKeyboardShortcuts(control);
            }
        }

        /// <summary>
        /// 次のコントロールを選択
        /// </summary>
        private bool SelectNextControl(Control control, bool forward, bool tabStopOnly, bool nested, bool wrap)
        {
            if (control.Parent != null)
            {
                return control.Parent.SelectNextControl(control, forward, tabStopOnly, nested, wrap);
            }
            return false;
        }

        /// <summary>
        /// 設定変更イベント
        /// </summary>
        public event Action<AccessibilitySettings> OnSettingsChanged;

        /// <summary>
        /// アクセシビリティチェックを実行
        /// </summary>
        public List<AccessibilityIssue> CheckAccessibility(Control control)
        {
            var issues = new List<AccessibilityIssue>();

            // コントラスト比のチェック
            CheckColorContrast(control, issues);

            // フォントサイズのチェック
            CheckFontSize(control, issues);

            // キーボードアクセシビリティのチェック
            CheckKeyboardAccessibility(control, issues);

            // スクリーンリーダー対応のチェック
            CheckScreenReaderSupport(control, issues);

            return issues;
        }

        private void CheckColorContrast(Control control, List<AccessibilityIssue> issues)
        {
            if (control.BackColor != null && control.ForeColor != null)
            {
                double contrastRatio = CalculateContrastRatio(control.BackColor, control.ForeColor);
                if (contrastRatio < 4.5) // WCAG AA基準
                {
                    issues.Add(new AccessibilityIssue
                    {
                        ControlName = control.Name,
                        IssueType = "Color Contrast",
                        Description = $"コントラスト比が不十分です: {contrastRatio:F2}",
                        Severity = IssueSeverity.Warning
                    });
                }
            }
        }

        private void CheckFontSize(Control control, List<AccessibilityIssue> issues)
        {
            if (control.Font != null && control.Font.Size < 8)
            {
                issues.Add(new AccessibilityIssue
                {
                    ControlName = control.Name,
                    IssueType = "Font Size",
                    Description = "フォントサイズが小さすぎます",
                    Severity = IssueSeverity.Warning
                });
            }
        }

        private void CheckKeyboardAccessibility(Control control, List<AccessibilityIssue> issues)
        {
            if (control.CanFocus && !control.TabStop)
            {
                issues.Add(new AccessibilityIssue
                {
                    ControlName = control.Name,
                    IssueType = "Keyboard Navigation",
                    Description = "キーボードでアクセスできません",
                    Severity = IssueSeverity.Error
                });
            }
        }

        private void CheckScreenReaderSupport(Control control, List<AccessibilityIssue> issues)
        {
            if (string.IsNullOrEmpty(control.AccessibleName))
            {
                issues.Add(new AccessibilityIssue
                {
                    ControlName = control.Name,
                    IssueType = "Screen Reader",
                    Description = "スクリーンリーダー用の名前が設定されていません",
                    Severity = IssueSeverity.Warning
                });
            }
        }

        private double CalculateContrastRatio(System.Drawing.Color bg, System.Drawing.Color fg)
        {
            double bgLuminance = GetRelativeLuminance(bg);
            double fgLuminance = GetRelativeLuminance(fg);

            double lighter = Math.Max(bgLuminance, fgLuminance);
            double darker = Math.Min(bgLuminance, fgLuminance);

            return (lighter + 0.05) / (darker + 0.05);
        }

        private double GetRelativeLuminance(System.Drawing.Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }
    }

    /// <summary>
    /// アクセシビリティの問題
    /// </summary>
    public class AccessibilityIssue
    {
        public string ControlName { get; set; }
        public string IssueType { get; set; }
        public string Description { get; set; }
        public IssueSeverity Severity { get; set; }
    }

    /// <summary>
    /// 問題の重要度
    /// </summary>
    public enum IssueSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }
}