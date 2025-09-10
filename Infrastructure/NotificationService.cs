using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MurtiWifiConnecter.Infrastructure
{
    /// <summary>
    /// 通知サービス
    /// </summary>
    public interface INotificationService
    {
        void ShowToast(string title, string message, ToastDuration duration = ToastDuration.Short);
        void ShowBalloon(string title, string message, BalloonIcon icon = BalloonIcon.Info);
        Task<bool> ShowConfirmationAsync(string message, string title = "確認");
        void ShowError(string message, string title = "エラー");
        void ShowWarning(string message, string title = "警告");
        void ShowSuccess(string message, string title = "成功");
        void ShowInfo(string message, string title = "情報");
    }

    /// <summary>
    /// Windows通知サービスの実装
    /// </summary>
    public class WindowsNotificationService : INotificationService
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Queue<NotificationHistory> _history;
        private readonly int _maxHistorySize;

        public WindowsNotificationService()
        {
            _history = new Queue<NotificationHistory>();
            _maxHistorySize = 100;
            
            _notifyIcon = new NotifyIcon
            {
                Visible = false,
                Icon = System.Drawing.SystemIcons.Information
            };
        }

        public void ShowToast(string title, string message, ToastDuration duration = ToastDuration.Short)
        {
            try
            {
                _notifyIcon.Visible = true;
                _notifyIcon.BalloonTipTitle = title;
                _notifyIcon.BalloonTipText = message;
                _notifyIcon.ShowBalloonTip((int)duration);
                
                AddToHistory(title, message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Toast notification failed: {ex.Message}");
            }
        }

        public void ShowBalloon(string title, string message, BalloonIcon icon = BalloonIcon.Info)
        {
            try
            {
                _notifyIcon.Visible = true;
                _notifyIcon.BalloonTipTitle = title;
                _notifyIcon.BalloonTipText = message;
                _notifyIcon.BalloonTipIcon = ConvertToToolTipIcon(icon);
                _notifyIcon.ShowBalloonTip(5000);
                
                AddToHistory(title, message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Balloon notification failed: {ex.Message}");
            }
        }

        public async Task<bool> ShowConfirmationAsync(string message, string title = "確認")
        {
            return await Task.Run(() =>
            {
                var result = MessageBox.Show(
                    message,
                    title,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );
                return result == DialogResult.Yes;
            });
        }

        public void ShowError(string message, string title = "エラー")
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            AddToHistory(title, message);
        }

        public void ShowWarning(string message, string title = "警告")
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            AddToHistory(title, message);
        }

        public void ShowSuccess(string message, string title = "成功")
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            AddToHistory(title, message);
        }

        public void ShowInfo(string message, string title = "情報")
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            AddToHistory(title, message);
        }

        private void AddToHistory(string title, string message)
        {
            var entry = new NotificationHistory
            {
                Id = Guid.NewGuid().ToString(),
                Title = title,
                Message = message,
                Timestamp = DateTime.Now,
                WasRead = false,
                WasClicked = false
            };

            _history.Enqueue(entry);
            
            while (_history.Count > _maxHistorySize)
            {
                _history.Dequeue();
            }
        }

        private ToolTipIcon ConvertToToolTipIcon(BalloonIcon icon)
        {
            return icon switch
            {
                BalloonIcon.Info => ToolTipIcon.Info,
                BalloonIcon.Warning => ToolTipIcon.Warning,
                BalloonIcon.Error => ToolTipIcon.Error,
                _ => ToolTipIcon.None
            };
        }

        public void Dispose()
        {
            _notifyIcon?.Dispose();
        }
    }
}