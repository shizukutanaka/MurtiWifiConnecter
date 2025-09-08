using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MurtiWifiConnecter
{
    public class UIResponseOptimizer : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly SystemTrayManager? _systemTrayManager;
        private readonly ConnectionLogger _logger;
        private readonly Timer _batchUpdateTimer;
        private readonly ConcurrentQueue<UIUpdateTask> _updateQueue = new();
        private readonly ConcurrentQueue<MessageTask> _messageQueue = new();
        private readonly Dictionary<string, DateTime> _lastUpdateTimes = new();
        
        private bool _disposed = false;
        private int _processingUpdates = 0;
        private const int BatchUpdateIntervalMs = 16; // 60FPS相当
        private const int MessageProcessingIntervalMs = 100;
        
        public UIResponseOptimizer(Dispatcher dispatcher, SystemTrayManager? systemTrayManager, ConnectionLogger logger)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _systemTrayManager = systemTrayManager;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _batchUpdateTimer = new Timer(ProcessQueuedUpdates, null, BatchUpdateIntervalMs, BatchUpdateIntervalMs);
        }
        
        public void QueueUIUpdate(string key, Action uiAction, TimeSpan? throttleInterval = null)
        {
            if (_disposed || uiAction == null) return;
            
            var task = new UIUpdateTask
            {
                Key = key,
                Action = uiAction,
                Timestamp = DateTime.Now,
                ThrottleInterval = throttleInterval ?? TimeSpan.FromMilliseconds(100)
            };
            
            _updateQueue.Enqueue(task);
        }
        
        public void QueueUIUpdateAsync<T>(string key, Func<Task<T>> backgroundTask, Action<T> uiAction, TimeSpan? throttleInterval = null)
        {
            if (_disposed || backgroundTask == null || uiAction == null) return;
            
            Task.Run(async () =>
            {
                try
                {
                    var result = await backgroundTask();
                    QueueUIUpdate(key, () => uiAction(result), throttleInterval);
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError($"UIResponseOptimizer.QueueUIUpdateAsync.{key}", ex, _logger);
                }
            });
        }
        
        public void ShowNonBlockingMessage(string title, string message, MessageType type = MessageType.Information, bool forceDialog = false)
        {
            if (_disposed) return;
            
            var messageTask = new MessageTask
            {
                Title = title,
                Message = message,
                Type = type,
                ForceDialog = forceDialog,
                Timestamp = DateTime.Now
            };
            
            _messageQueue.Enqueue(messageTask);
            
            // 重要なメッセージは即座に処理
            if (type == MessageType.Critical || forceDialog)
            {
                Task.Run(() => ProcessMessage(messageTask));
            }
        }
        
        public async Task<MessageBoxResult> ShowNonBlockingQuestionAsync(string title, string message, MessageBoxButton buttons = MessageBoxButton.YesNo)
        {
            if (_disposed) return MessageBoxResult.None;
            
            var completionSource = new TaskCompletionSource<MessageBoxResult>();
            
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var result = MessageBox.Show(message, title, buttons, 
                        buttons == MessageBoxButton.YesNo ? MessageBoxImage.Question : MessageBoxImage.Information);
                    completionSource.SetResult(result);
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError("UIResponseOptimizer.ShowNonBlockingQuestionAsync", ex, _logger);
                    completionSource.SetResult(MessageBoxResult.None);
                }
            }, DispatcherPriority.Normal);
            
            return await completionSource.Task;
        }
        
        public void UpdateProgressBar(string progressKey, double progress, string? statusText = null)
        {
            QueueUIUpdate($"progress_{progressKey}", () =>
            {
                // プログレスバー更新の実装（MainWindowで実装される具体的なロジック）
                ProgressUpdated?.Invoke(this, new ProgressEventArgs
                {
                    Key = progressKey,
                    Progress = progress,
                    StatusText = statusText
                });
            }, TimeSpan.FromMilliseconds(50));
        }
        
        public void BatchUpdateControls(params (string key, Action action)[] updates)
        {
            if (_disposed || updates == null) return;
            
            var batchKey = $"batch_{DateTime.Now.Ticks}";
            QueueUIUpdate(batchKey, () =>
            {
                foreach (var (key, action) in updates)
                {
                    try
                    {
                        action?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler.LogError($"UIResponseOptimizer.BatchUpdateControls.{key}", ex, _logger);
                    }
                }
            }, TimeSpan.FromMilliseconds(16)); // 60FPS
        }
        
        private async void ProcessQueuedUpdates(object? state)
        {
            if (_disposed || Interlocked.CompareExchange(ref _processingUpdates, 1, 0) != 0)
                return;
            
            try
            {
                var tasksToProcess = new List<UIUpdateTask>();
                var currentTime = DateTime.Now;
                
                // キューから処理対象のタスクを抽出
                while (_updateQueue.TryDequeue(out var task))
                {
                    if (ShouldProcessUpdate(task, currentTime))
                    {
                        tasksToProcess.Add(task);
                        _lastUpdateTimes[task.Key] = currentTime;
                    }
                }
                
                if (tasksToProcess.Count > 0)
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        foreach (var task in tasksToProcess)
                        {
                            try
                            {
                                task.Action();
                            }
                            catch (Exception ex)
                            {
                                ErrorHandler.LogError($"UIResponseOptimizer.ProcessUpdate.{task.Key}", ex, _logger);
                            }
                        }
                    }, DispatcherPriority.Background);
                }
                
                // メッセージキューの処理
                ProcessMessageQueue();
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("UIResponseOptimizer.ProcessQueuedUpdates", ex, _logger);
            }
            finally
            {
                Interlocked.Exchange(ref _processingUpdates, 0);
            }
        }
        
        private bool ShouldProcessUpdate(UIUpdateTask task, DateTime currentTime)
        {
            if (!_lastUpdateTimes.TryGetValue(task.Key, out var lastUpdate))
                return true;
                
            return (currentTime - lastUpdate) >= task.ThrottleInterval;
        }
        
        private void ProcessMessageQueue()
        {
            var processedCount = 0;
            while (_messageQueue.TryDequeue(out var messageTask) && processedCount < 5)
            {
                ProcessMessage(messageTask);
                processedCount++;
            }
        }
        
        private void ProcessMessage(MessageTask messageTask)
        {
            try
            {
                // システムトレイ通知を優先
                var iconType = messageTask.Type switch
                {
                    MessageType.Error => System.Windows.Forms.ToolTipIcon.Error,
                    MessageType.Warning => System.Windows.Forms.ToolTipIcon.Warning,
                    MessageType.Information => System.Windows.Forms.ToolTipIcon.Info,
                    MessageType.Critical => System.Windows.Forms.ToolTipIcon.Error,
                    _ => System.Windows.Forms.ToolTipIcon.Info
                };
                
                _systemTrayManager?.ShowBalloonTip(messageTask.Title, messageTask.Message, iconType);
                
                // ログ記録
                var logLevel = messageTask.Type switch
                {
                    MessageType.Error => ConnectionLogger.LogLevel.Error,
                    MessageType.Warning => ConnectionLogger.LogLevel.Warning,
                    MessageType.Critical => ConnectionLogger.LogLevel.Error,
                    _ => ConnectionLogger.LogLevel.Info
                };
                
                _logger?.Log(logLevel, "UIMessage", $"{messageTask.Title}: {messageTask.Message}");
                
                // 重要なメッセージまたは強制ダイアログの場合のみMessageBoxを表示
                if (messageTask.Type == MessageType.Critical || messageTask.ForceDialog)
                {
                    _dispatcher.BeginInvoke(() =>
                    {
                        var icon = messageTask.Type switch
                        {
                            MessageType.Error => MessageBoxImage.Error,
                            MessageType.Warning => MessageBoxImage.Warning,
                            MessageType.Critical => MessageBoxImage.Stop,
                            _ => MessageBoxImage.Information
                        };
                        
                        MessageBox.Show(messageTask.Message, messageTask.Title, MessageBoxButton.OK, icon);
                    }, DispatcherPriority.Normal);
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("UIResponseOptimizer.ProcessMessage", ex, _logger);
            }
        }
        
        public async Task FlushPendingUpdatesAsync()
        {
            if (_disposed) return;
            
            var remainingTasks = new List<UIUpdateTask>();
            while (_updateQueue.TryDequeue(out var task))
            {
                remainingTasks.Add(task);
            }
            
            if (remainingTasks.Count > 0)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    foreach (var task in remainingTasks)
                    {
                        try
                        {
                            task.Action();
                        }
                        catch (Exception ex)
                        {
                            ErrorHandler.LogError($"UIResponseOptimizer.FlushPendingUpdates.{task.Key}", ex, _logger);
                        }
                    }
                }, DispatcherPriority.Normal);
            }
        }
        
        public event EventHandler<ProgressEventArgs>? ProgressUpdated;
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _batchUpdateTimer?.Dispose();
            
            // 残りのタスクを処理
            _ = FlushPendingUpdatesAsync();
        }
        
        #region Data Classes
        
        private class UIUpdateTask
        {
            public string Key { get; set; } = "";
            public Action Action { get; set; } = () => { };
            public DateTime Timestamp { get; set; }
            public TimeSpan ThrottleInterval { get; set; }
        }
        
        private class MessageTask
        {
            public string Title { get; set; } = "";
            public string Message { get; set; } = "";
            public MessageType Type { get; set; } = MessageType.Information;
            public bool ForceDialog { get; set; } = false;
            public DateTime Timestamp { get; set; }
        }
        
        #endregion
    }
    
    public enum MessageType
    {
        Information,
        Warning,
        Error,
        Critical
    }
    
    public class ProgressEventArgs : EventArgs
    {
        public string Key { get; set; } = "";
        public double Progress { get; set; }
        public string? StatusText { get; set; }
    }
}