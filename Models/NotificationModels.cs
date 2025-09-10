using System;
using System.Collections.Generic;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// トースト通知の表示時間
    /// </summary>
    public enum ToastDuration
    {
        Short = 2000,
        Medium = 3500,
        Long = 5000
    }

    /// <summary>
    /// バルーン通知のアイコン
    /// </summary>
    public enum BalloonIcon
    {
        None,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// 通知オプション
    /// </summary>
    public class NotificationOptions
    {
        public bool IsTransient { get; set; } = false;
        public int DurationMs { get; set; } = 5000;
        public string IconPath { get; set; }
        public List<NotificationAction> Actions { get; set; } = new();
        public string Tag { get; set; }
        public string Group { get; set; }
        public bool RequireInteraction { get; set; } = false;
        public bool Silent { get; set; } = false;
    }

    /// <summary>
    /// 通知アクション
    /// </summary>
    public class NotificationAction
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string IconPath { get; set; }
    }

    /// <summary>
    /// 通知履歴
    /// </summary>
    public class NotificationHistory
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Type { get; set; }
        public bool WasClicked { get; set; }
        public string ActionTaken { get; set; }
    }

    /// <summary>
    /// 通知イベント引数
    /// </summary>
    public class NotificationEventArgs : EventArgs
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public string Type { get; set; }
    }

    /// <summary>
    /// 通知アクションイベント引数
    /// </summary>
    public class NotificationActionEventArgs : EventArgs
    {
        public string NotificationId { get; set; }
        public string ActionId { get; set; }
        public string ActionTitle { get; set; }
        public DateTime Timestamp { get; set; }
    }
}