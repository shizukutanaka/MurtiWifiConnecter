using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Infrastructure
{
    public class NotificationService : INotificationService
    {
        private readonly IConfigurationService _configService;
        private readonly ILoggingService _logger;
        private readonly Queue<NotificationRequest> _notificationQueue;
        private readonly object _queueLock = new object();
        private bool _notificationsEnabled;

        public event EventHandler<NotificationEventArgs> NotificationSent;
        public event EventHandler<NotificationActionEventArgs> NotificationActionInvoked;

        public NotificationService(IConfigurationService configService, ILoggingService logger)
        {
            _configService = configService;
            _logger = logger;
            _notificationQueue = new Queue<NotificationRequest>();
            
            LoadConfiguration();
            _configService.ConfigurationChanged += OnConfigurationChanged;
        }

        public async Task ShowInfoAsync(string title, string message, NotificationOptions options = null)
        {
            await ShowNotificationAsync(NotificationType.Information, title, message, options);
        }

        public async Task ShowSuccessAsync(string title, string message, NotificationOptions options = null)
        {
            await ShowNotificationAsync(NotificationType.Success, title, message, options);
        }

        public async Task ShowWarningAsync(string title, string message, NotificationOptions options = null)
        {
            await ShowNotificationAsync(NotificationType.Warning, title, message, options);
        }

        public async Task ShowErrorAsync(string title, string message, NotificationOptions options = null)
        {
            await ShowNotificationAsync(NotificationType.Error, title, message, options);
        }

        public async Task ShowProgressAsync(string title, string message, int progress, NotificationOptions options = null)
        {
            if (!_notificationsEnabled)
                return;

            try
            {
                var request = new NotificationRequest
                {
                    Type = NotificationType.Progress,
                    Title = title,
                    Message = message,
                    Progress = progress,
                    Options = options ?? new NotificationOptions(),
                    Timestamp = DateTime.UtcNow
                };

                await DisplayNotificationAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to show progress notification: {title}", ex);
            }
        }

        public async Task ShowCustomAsync(string title, string message, string iconPath, NotificationOptions options = null)
        {
            if (!_notificationsEnabled)
                return;

            try
            {
                var request = new NotificationRequest
                {
                    Type = NotificationType.Custom,
                    Title = title,
                    Message = message,
                    IconPath = iconPath,
                    Options = options ?? new NotificationOptions(),
                    Timestamp = DateTime.UtcNow
                };

                await DisplayNotificationAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to show custom notification: {title}", ex);
            }
        }

        public void ShowToast(string message, ToastDuration duration = ToastDuration.Short)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Implementation would show a toast overlay in the application
                _logger.LogDebug($"Toast shown: {message} ({duration})");
            });
        }

        public void ShowBalloonTip(string title, string message, BalloonIcon icon = BalloonIcon.Info)
        {
            if (!_notificationsEnabled)
                return;

            try
            {
                // Windows notification implementation
                var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
                var textElements = toastXml.GetElementsByTagName("text");
                textElements[0].AppendChild(toastXml.CreateTextNode(title));
                textElements[1].AppendChild(toastXml.CreateTextNode(message));

                var toast = new ToastNotification(toastXml);
                ToastNotificationManager.CreateToastNotifier("MurtiWifiConnecter").Show(toast);
                
                _logger.LogDebug($"Balloon tip shown: {title}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to show balloon tip: {title}", ex);
            }
        }

        public async Task<List<NotificationHistory>> GetHistoryAsync(int count = 50)
        {
            var history = new List<NotificationHistory>();
            
            lock (_queueLock)
            {
                var items = _notificationQueue.ToArray();
                for (int i = Math.Max(0, items.Length - count); i < items.Length; i++)
                {
                    var item = items[i];
                    history.Add(new NotificationHistory
                    {
                        Title = item.Title,
                        Message = item.Message,
                        Type = item.Type,
                        Timestamp = item.Timestamp,
                        WasClicked = item.WasClicked,
                        WasDismissed = item.WasDismissed
                    });
                }
            }
            
            return await Task.FromResult(history);
        }

        public async Task ClearHistoryAsync()
        {
            lock (_queueLock)
            {
                _notificationQueue.Clear();
            }
            
            _logger.LogInfo("Notification history cleared");
            await Task.CompletedTask;
        }

        private async Task ShowNotificationAsync(NotificationType type, string title, string message, NotificationOptions options)
        {
            if (!_notificationsEnabled)
                return;

            try
            {
                var request = new NotificationRequest
                {
                    Type = type,
                    Title = title,
                    Message = message,
                    Options = options ?? new NotificationOptions(),
                    Timestamp = DateTime.UtcNow
                };

                await DisplayNotificationAsync(request);
                
                lock (_queueLock)
                {
                    _notificationQueue.Enqueue(request);
                    
                    // Keep only last 100 notifications
                    while (_notificationQueue.Count > 100)
                    {
                        _notificationQueue.Dequeue();
                    }
                }
                
                NotificationSent?.Invoke(this, new NotificationEventArgs
                {
                    Title = title,
                    Message = message,
                    Type = type,
                    Timestamp = request.Timestamp
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to show notification: {title}", ex);
            }
        }

        private async Task DisplayNotificationAsync(NotificationRequest request)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    // Create Windows toast notification
                    var template = GetToastTemplate(request);
                    var toastXml = ToastNotificationManager.GetTemplateContent(template);
                    
                    // Set text
                    var textElements = toastXml.GetElementsByTagName("text");
                    textElements[0].AppendChild(toastXml.CreateTextNode(request.Title));
                    if (textElements.Length > 1)
                    {
                        textElements[1].AppendChild(toastXml.CreateTextNode(request.Message));
                    }
                    
                    // Set image if needed
                    if (!string.IsNullOrEmpty(request.IconPath) || request.Type != NotificationType.Information)
                    {
                        var imageElements = toastXml.GetElementsByTagName("image");
                        if (imageElements.Length > 0)
                        {
                            var imagePath = GetIconPath(request);
                            ((XmlElement)imageElements[0]).SetAttribute("src", imagePath);
                        }
                    }
                    
                    // Add actions if specified
                    if (request.Options.Actions != null && request.Options.Actions.Count > 0)
                    {
                        var actions = toastXml.CreateElement("actions");
                        foreach (var action in request.Options.Actions)
                        {
                            var actionElement = toastXml.CreateElement("action");
                            actionElement.SetAttribute("content", action.Label);
                            actionElement.SetAttribute("arguments", action.ActionId);
                            actions.AppendChild(actionElement);
                        }
                        toastXml.DocumentElement.AppendChild(actions);
                    }
                    
                    var toast = new ToastNotification(toastXml);
                    
                    // Set expiration
                    if (request.Options.Duration.HasValue)
                    {
                        toast.ExpirationTime = DateTimeOffset.Now.AddMilliseconds(request.Options.Duration.Value);
                    }
                    
                    // Handle events
                    toast.Activated += (s, e) =>
                    {
                        request.WasClicked = true;
                        NotificationActionInvoked?.Invoke(this, new NotificationActionEventArgs
                        {
                            NotificationId = request.Id,
                            ActionId = e.ToString()
                        });
                    };
                    
                    toast.Dismissed += (s, e) =>
                    {
                        request.WasDismissed = true;
                    };
                    
                    ToastNotificationManager.CreateToastNotifier("MurtiWifiConnecter").Show(toast);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to display Windows notification", ex);
                    
                    // Fallback to message box
                    var icon = request.Type switch
                    {
                        NotificationType.Error => MessageBoxImage.Error,
                        NotificationType.Warning => MessageBoxImage.Warning,
                        NotificationType.Success => MessageBoxImage.Information,
                        _ => MessageBoxImage.Information
                    };
                    
                    MessageBox.Show(request.Message, request.Title, MessageBoxButton.OK, icon);
                }
            });
        }

        private ToastTemplateType GetToastTemplate(NotificationRequest request)
        {
            if (!string.IsNullOrEmpty(request.IconPath) || request.Type != NotificationType.Information)
            {
                return ToastTemplateType.ToastImageAndText02;
            }
            return ToastTemplateType.ToastText02;
        }

        private string GetIconPath(NotificationRequest request)
        {
            if (!string.IsNullOrEmpty(request.IconPath))
                return request.IconPath;
            
            return request.Type switch
            {
                NotificationType.Success => "ms-appx:///Assets/success.png",
                NotificationType.Warning => "ms-appx:///Assets/warning.png",
                NotificationType.Error => "ms-appx:///Assets/error.png",
                _ => "ms-appx:///Assets/info.png"
            };
        }

        private void LoadConfiguration()
        {
            _notificationsEnabled = _configService.GetValue("UI:ShowNotifications", true);
        }

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            if (e.Key == "UI:ShowNotifications")
            {
                _notificationsEnabled = Convert.ToBoolean(e.NewValue);
            }
        }
    }

    public enum NotificationType
    {
        Information,
        Success,
        Warning,
        Error,
        Progress,
        Custom
    }

    public enum ToastDuration
    {
        Short = 2000,
        Medium = 4000,
        Long = 6000
    }

    public enum BalloonIcon
    {
        None,
        Info,
        Warning,
        Error
    }

    public class NotificationRequest
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public NotificationType Type { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string IconPath { get; set; }
        public int Progress { get; set; }
        public NotificationOptions Options { get; set; }
        public DateTime Timestamp { get; set; }
        public bool WasClicked { get; set; }
        public bool WasDismissed { get; set; }
    }

    public class NotificationOptions
    {
        public int? Duration { get; set; }
        public bool ShowInActionCenter { get; set; } = true;
        public bool PlaySound { get; set; } = true;
        public List<NotificationAction> Actions { get; set; } = new List<NotificationAction>();
    }

    public class NotificationAction
    {
        public string ActionId { get; set; }
        public string Label { get; set; }
    }

    public class NotificationHistory
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public NotificationType Type { get; set; }
        public DateTime Timestamp { get; set; }
        public bool WasClicked { get; set; }
        public bool WasDismissed { get; set; }
    }

    public class NotificationEventArgs : EventArgs
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public NotificationType Type { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class NotificationActionEventArgs : EventArgs
    {
        public string NotificationId { get; set; }
        public string ActionId { get; set; }
    }
}