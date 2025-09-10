using System;
using System.Drawing;
using System.Windows.Forms;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// UI作成およびスタイリングのヘルパークラス
    /// </summary>
    public static class UIHelper
    {
        private static readonly Color PrimaryColor = Color.FromArgb(52, 152, 219);
        private static readonly Color SuccessColor = Color.FromArgb(46, 204, 113);
        private static readonly Color WarningColor = Color.FromArgb(241, 196, 15);
        private static readonly Color DangerColor = Color.FromArgb(231, 76, 60);

        /// <summary>
        /// スタイル付きボタンを作成
        /// </summary>
        public static Button CreateStyledButton(string text, ButtonStyle style = ButtonStyle.Primary)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Size = new Size(100, 35),
                Cursor = Cursors.Hand
            };

            ApplyButtonStyle(button, style);
            return button;
        }

        /// <summary>
        /// ボタンにスタイルを適用
        /// </summary>
        public static void ApplyButtonStyle(Button button, ButtonStyle style)
        {
            switch (style)
            {
                case ButtonStyle.Primary:
                    button.BackColor = PrimaryColor;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = PrimaryColor;
                    break;
                case ButtonStyle.Success:
                    button.BackColor = SuccessColor;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = SuccessColor;
                    break;
                case ButtonStyle.Warning:
                    button.BackColor = WarningColor;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = WarningColor;
                    break;
                case ButtonStyle.Danger:
                    button.BackColor = DangerColor;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = DangerColor;
                    break;
            }

            button.FlatAppearance.BorderSize = 0;
        }

        /// <summary>
        /// パネルを作成
        /// </summary>
        public static Panel CreatePanel(int x, int y, int width, int height)
        {
            return new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
        }

        /// <summary>
        /// ラベルを作成
        /// </summary>
        public static Label CreateLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
        }

        /// <summary>
        /// テキストボックスを作成
        /// </summary>
        public static TextBox CreateTextBox(int x, int y, int width)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(width, 23),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
        }

        /// <summary>
        /// コンボボックスを作成
        /// </summary>
        public static ComboBox CreateComboBox(int x, int y, int width)
        {
            return new ComboBox
            {
                Location = new Point(x, y),
                Size = new Size(width, 23),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
        }

        /// <summary>
        /// リストビューを作成
        /// </summary>
        public static ListView CreateListView(int x, int y, int width, int height)
        {
            return new ListView
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
        }

        /// <summary>
        /// プログレスバーを作成
        /// </summary>
        public static ProgressBar CreateProgressBar(int x, int y, int width)
        {
            return new ProgressBar
            {
                Location = new Point(x, y),
                Size = new Size(width, 23),
                Style = ProgressBarStyle.Continuous
            };
        }

        /// <summary>
        /// グループボックスを作成
        /// </summary>
        public static GroupBox CreateGroupBox(string text, int x, int y, int width, int height)
        {
            return new GroupBox
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
        }

        /// <summary>
        /// チェックボックスを作成
        /// </summary>
        public static CheckBox CreateCheckBox(string text, int x, int y)
        {
            return new CheckBox
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
        }

        /// <summary>
        /// メッセージボックスを表示
        /// </summary>
        public static DialogResult ShowMessage(string message, string title, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information)
        {
            return MessageBox.Show(message, title, buttons, icon);
        }

        /// <summary>
        /// エラーメッセージを表示
        /// </summary>
        public static void ShowError(string message, string title = "エラー")
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// 警告メッセージを表示
        /// </summary>
        public static void ShowWarning(string message, string title = "警告")
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// 成功メッセージを表示
        /// </summary>
        public static void ShowSuccess(string message, string title = "成功")
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 確認ダイアログを表示
        /// </summary>
        public static bool ShowConfirmation(string message, string title = "確認")
        {
            return MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
    }

    /// <summary>
    /// ボタンスタイル
    /// </summary>
    public enum ButtonStyle
    {
        Primary,
        Success,
        Warning,
        Danger
    }
}