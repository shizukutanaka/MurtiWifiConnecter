using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Automation
{
    public interface ITaskScheduler
    {
        Task<string> ScheduleTaskAsync(string name, Func<CancellationToken, Task> task, TimeSpan delay);
        Task<string> ScheduleRecurringTaskAsync(string name, Func<CancellationToken, Task> task, TimeSpan interval);
        Task<bool> CancelTaskAsync(string taskId);
        Task<List<ScheduledTaskInfo>> GetScheduledTasksAsync();
        void Dispose();
    }

    public class TaskScheduler : ITaskScheduler, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly ConcurrentDictionary<string, ScheduledTaskWrapper> _scheduledTasks;
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        public TaskScheduler(ILoggingService logger)
        {
            _logger = logger;
            _scheduledTasks = new ConcurrentDictionary<string, ScheduledTaskWrapper>();
            
            // Cleanup completed tasks every 5 minutes
            _cleanupTimer = new Timer(CleanupCompletedTasks, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task<string> ScheduleTaskAsync(string name, Func<CancellationToken, Task> task, TimeSpan delay)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Task name cannot be empty", nameof(name));
            
            if (task == null)
                throw new ArgumentNullException(nameof(task));
            
            if (delay < TimeSpan.Zero)
                throw new ArgumentException("Delay cannot be negative", nameof(delay));
            
            var taskId = GenerateTaskId();
            var wrapper = new ScheduledTaskWrapper
            {
                Id = taskId,
                Name = name,
                Task = task,
                ScheduledTime = DateTime.UtcNow.Add(delay),
                IsRecurring = false,
                Status = TaskStatus.Scheduled
            };
            
            wrapper.CancellationTokenSource = new CancellationTokenSource();
            
            _scheduledTasks[taskId] = wrapper;
            
            // Schedule the task execution
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, wrapper.CancellationTokenSource.Token);
                    await ExecuteTaskAsync(wrapper);
                }
                catch (OperationCanceledException)
                {
                    wrapper.Status = TaskStatus.Canceled;
                    _logger.LogInfo($"Scheduled task cancelled: {name} ({taskId})");
                }
                catch (Exception ex)
                {
                    wrapper.Status = TaskStatus.Failed;
                    wrapper.LastError = ex;
                    _logger.LogError($"Scheduled task failed: {name} ({taskId})", ex);
                }
            });
            
            _logger.LogInfo($"Task scheduled: {name} ({taskId}) - Delay: {delay}");
            return taskId;
        }

        public async Task<string> ScheduleRecurringTaskAsync(string name, Func<CancellationToken, Task> task, TimeSpan interval)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Task name cannot be empty", nameof(name));
            
            if (task == null)
                throw new ArgumentNullException(nameof(task));
            
            if (interval <= TimeSpan.Zero)
                throw new ArgumentException("Interval must be positive", nameof(interval));
            
            var taskId = GenerateTaskId();
            var wrapper = new ScheduledTaskWrapper
            {
                Id = taskId,
                Name = name,
                Task = task,
                ScheduledTime = DateTime.UtcNow.Add(interval),
                IsRecurring = true,
                RecurringInterval = interval,
                Status = TaskStatus.Scheduled
            };
            
            wrapper.CancellationTokenSource = new CancellationTokenSource();
            
            _scheduledTasks[taskId] = wrapper;
            
            // Schedule the recurring task execution
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!wrapper.CancellationTokenSource.Token.IsCancellationRequested)
                    {
                        await Task.Delay(interval, wrapper.CancellationTokenSource.Token);
                        
                        if (!wrapper.CancellationTokenSource.Token.IsCancellationRequested)
                        {
                            wrapper.ScheduledTime = DateTime.UtcNow.Add(interval);
                            await ExecuteTaskAsync(wrapper);
                            wrapper.ExecutionCount++;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    wrapper.Status = TaskStatus.Canceled;
                    _logger.LogInfo($"Recurring task cancelled: {name} ({taskId})");
                }
                catch (Exception ex)
                {
                    wrapper.Status = TaskStatus.Failed;
                    wrapper.LastError = ex;
                    _logger.LogError($"Recurring task failed: {name} ({taskId})", ex);
                }
            });
            
            _logger.LogInfo($"Recurring task scheduled: {name} ({taskId}) - Interval: {interval}");
            return taskId;
        }

        public async Task<bool> CancelTaskAsync(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                return false;
            
            if (_scheduledTasks.TryGetValue(taskId, out var wrapper))
            {
                wrapper.CancellationTokenSource?.Cancel();
                wrapper.Status = TaskStatus.Canceled;
                
                _logger.LogInfo($"Task cancelled: {wrapper.Name} ({taskId})");
                return await Task.FromResult(true);
            }
            
            return await Task.FromResult(false);
        }

        public async Task<List<ScheduledTaskInfo>> GetScheduledTasksAsync()
        {
            var taskInfos = _scheduledTasks.Values.Select(wrapper => new ScheduledTaskInfo
            {
                Id = wrapper.Id,
                Name = wrapper.Name,
                ScheduledTime = wrapper.ScheduledTime,
                IsRecurring = wrapper.IsRecurring,
                RecurringInterval = wrapper.RecurringInterval,
                Status = wrapper.Status,
                ExecutionCount = wrapper.ExecutionCount,
                LastExecutionTime = wrapper.LastExecutionTime,
                LastError = wrapper.LastError?.Message
            }).ToList();
            
            return await Task.FromResult(taskInfos);
        }

        private async Task ExecuteTaskAsync(ScheduledTaskWrapper wrapper)
        {
            try
            {
                wrapper.Status = TaskStatus.Running;
                wrapper.LastExecutionTime = DateTime.UtcNow;
                
                _logger.LogDebug($"Executing task: {wrapper.Name} ({wrapper.Id})");
                
                await wrapper.Task(wrapper.CancellationTokenSource.Token);
                
                wrapper.Status = wrapper.IsRecurring ? TaskStatus.Scheduled : TaskStatus.Completed;
                
                _logger.LogDebug($"Task completed: {wrapper.Name} ({wrapper.Id})");
            }
            catch (OperationCanceledException) when (wrapper.CancellationTokenSource.Token.IsCancellationRequested)
            {
                wrapper.Status = TaskStatus.Canceled;
                throw;
            }
            catch (Exception ex)
            {
                wrapper.Status = TaskStatus.Failed;
                wrapper.LastError = ex;
                _logger.LogError($"Task execution failed: {wrapper.Name} ({wrapper.Id})", ex);
                throw;
            }
        }

        private void CleanupCompletedTasks(object state)
        {
            try
            {
                var completedTasks = _scheduledTasks
                    .Where(kvp => kvp.Value.Status == TaskStatus.Completed || 
                                 kvp.Value.Status == TaskStatus.Failed ||
                                 kvp.Value.Status == TaskStatus.Canceled)
                    .Where(kvp => DateTime.UtcNow - kvp.Value.LastExecutionTime > TimeSpan.FromHours(1))
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var taskId in completedTasks)
                {
                    if (_scheduledTasks.TryRemove(taskId, out var wrapper))
                    {
                        wrapper.CancellationTokenSource?.Dispose();
                    }
                }
                
                if (completedTasks.Count > 0)
                {
                    _logger.LogDebug($"Cleaned up {completedTasks.Count} completed tasks");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to cleanup completed tasks", ex);
            }
        }

        private string GenerateTaskId()
        {
            return $"task_{DateTime.UtcNow.Ticks}_{Guid.NewGuid():N}";
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            
            _disposed = true;
            
            _cleanupTimer?.Dispose();
            
            // Cancel all running tasks
            foreach (var wrapper in _scheduledTasks.Values)
            {
                wrapper.CancellationTokenSource?.Cancel();
                wrapper.CancellationTokenSource?.Dispose();
            }
            
            _scheduledTasks.Clear();
            
            _logger.LogInfo("TaskScheduler disposed");
        }

        private class ScheduledTaskWrapper
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public Func<CancellationToken, Task> Task { get; set; }
            public DateTime ScheduledTime { get; set; }
            public bool IsRecurring { get; set; }
            public TimeSpan? RecurringInterval { get; set; }
            public TaskStatus Status { get; set; }
            public int ExecutionCount { get; set; }
            public DateTime? LastExecutionTime { get; set; }
            public Exception LastError { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; }
        }
    }

    public class ScheduledTaskInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DateTime ScheduledTime { get; set; }
        public bool IsRecurring { get; set; }
        public TimeSpan? RecurringInterval { get; set; }
        public TaskStatus Status { get; set; }
        public int ExecutionCount { get; set; }
        public DateTime? LastExecutionTime { get; set; }
        public string LastError { get; set; }
    }

    public enum TaskStatus
    {
        Scheduled,
        Running,
        Completed,
        Failed,
        Canceled
    }
}