using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace MurtiWifiConnecter.Automation
{
    /// <summary>
    /// タスクスケジューラー
    /// </summary>
    public interface ITaskScheduler
    {
        void ScheduleTask(ScheduledTask task);
        void CancelTask(string taskId);
        void StartScheduler();
        void StopScheduler();
        List<ScheduledTask> GetActiveTasks();
        event Action<ScheduledTask> TaskExecuted;
        event Action<ScheduledTask, Exception> TaskFailed;
    }

    /// <summary>
    /// タスクスケジューラーの実装
    /// </summary>
    public class TaskScheduler : ITaskScheduler, IDisposable
    {
        private readonly ConcurrentDictionary<string, ScheduledTask> _scheduledTasks;
        private readonly ConcurrentDictionary<string, System.Timers.Timer> _timers;
        private readonly SemaphoreSlim _executionSemaphore;
        private readonly int _maxConcurrentTasks;
        private bool _isRunning;

        public event Action<ScheduledTask> TaskExecuted;
        public event Action<ScheduledTask, Exception> TaskFailed;

        public TaskScheduler(int maxConcurrentTasks = 5)
        {
            _scheduledTasks = new ConcurrentDictionary<string, ScheduledTask>();
            _timers = new ConcurrentDictionary<string, System.Timers.Timer>();
            _maxConcurrentTasks = maxConcurrentTasks;
            _executionSemaphore = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);
        }

        /// <summary>
        /// タスクをスケジュール
        /// </summary>
        public void ScheduleTask(ScheduledTask task)
        {
            if (task == null || string.IsNullOrEmpty(task.Id))
                throw new ArgumentException("Invalid task");

            _scheduledTasks[task.Id] = task;

            if (_isRunning)
            {
                SetupTimer(task);
            }
        }

        /// <summary>
        /// タスクをキャンセル
        /// </summary>
        public void CancelTask(string taskId)
        {
            if (_timers.TryRemove(taskId, out var timer))
            {
                timer.Stop();
                timer.Dispose();
            }

            if (_scheduledTasks.TryRemove(taskId, out var task))
            {
                task.Status = TaskStatus.Cancelled;
            }
        }

        /// <summary>
        /// スケジューラーを開始
        /// </summary>
        public void StartScheduler()
        {
            if (_isRunning) return;

            _isRunning = true;

            foreach (var task in _scheduledTasks.Values)
            {
                if (task.Status == TaskStatus.Pending)
                {
                    SetupTimer(task);
                }
            }
        }

        /// <summary>
        /// スケジューラーを停止
        /// </summary>
        public void StopScheduler()
        {
            _isRunning = false;

            foreach (var timer in _timers.Values)
            {
                timer.Stop();
                timer.Dispose();
            }
            _timers.Clear();
        }

        /// <summary>
        /// アクティブなタスクを取得
        /// </summary>
        public List<ScheduledTask> GetActiveTasks()
        {
            var activeTasks = new List<ScheduledTask>();
            foreach (var task in _scheduledTasks.Values)
            {
                if (task.Status == TaskStatus.Pending || task.Status == TaskStatus.Running)
                {
                    activeTasks.Add(task);
                }
            }
            return activeTasks;
        }

        /// <summary>
        /// タイマーを設定
        /// </summary>
        private void SetupTimer(ScheduledTask task)
        {
            var now = DateTime.Now;
            var nextExecution = CalculateNextExecution(task, now);

            if (nextExecution.HasValue)
            {
                var delay = (nextExecution.Value - now).TotalMilliseconds;
                if (delay > 0)
                {
                    var timer = new System.Timers.Timer(delay);
                    timer.Elapsed += async (sender, e) => await ExecuteTask(task);
                    timer.AutoReset = false;
                    timer.Start();

                    _timers[task.Id] = timer;
                }
            }
        }

        /// <summary>
        /// 次の実行時刻を計算
        /// </summary>
        private DateTime? CalculateNextExecution(ScheduledTask task, DateTime now)
        {
            switch (task.Schedule.Type)
            {
                case ScheduleType.Once:
                    return task.Schedule.ExecutionTime > now ? task.Schedule.ExecutionTime : null;

                case ScheduleType.Interval:
                    if (task.LastExecuted == null)
                        return now.Add(task.Schedule.Interval);
                    return task.LastExecuted.Value.Add(task.Schedule.Interval);

                case ScheduleType.Daily:
                    var dailyTime = now.Date.Add(task.Schedule.TimeOfDay);
                    return dailyTime > now ? dailyTime : dailyTime.AddDays(1);

                case ScheduleType.Weekly:
                    var daysUntilTarget = ((int)task.Schedule.DayOfWeek - (int)now.DayOfWeek + 7) % 7;
                    var weeklyTime = now.Date.AddDays(daysUntilTarget).Add(task.Schedule.TimeOfDay);
                    return weeklyTime > now ? weeklyTime : weeklyTime.AddDays(7);

                default:
                    return null;
            }
        }

        /// <summary>
        /// タスクを実行
        /// </summary>
        private async Task ExecuteTask(ScheduledTask task)
        {
            await _executionSemaphore.WaitAsync();

            try
            {
                task.Status = TaskStatus.Running;
                task.LastExecuted = DateTime.Now;

                await task.Action();

                task.Status = TaskStatus.Completed;
                task.ExecutionCount++;

                TaskExecuted?.Invoke(task);

                // 繰り返しタスクの場合は再スケジュール
                if (task.Schedule.Type != ScheduleType.Once)
                {
                    task.Status = TaskStatus.Pending;
                    SetupTimer(task);
                }
            }
            catch (Exception ex)
            {
                task.Status = TaskStatus.Failed;
                task.LastError = ex.Message;
                TaskFailed?.Invoke(task, ex);
            }
            finally
            {
                _executionSemaphore.Release();
                _timers.TryRemove(task.Id, out _);
            }
        }

        public void Dispose()
        {
            StopScheduler();
            _executionSemaphore?.Dispose();
        }
    }

    /// <summary>
    /// スケジュール済みタスク
    /// </summary>
    public class ScheduledTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Description { get; set; }
        public Func<Task> Action { get; set; }
        public TaskSchedule Schedule { get; set; }
        public TaskStatus Status { get; set; } = TaskStatus.Pending;
        public DateTime? LastExecuted { get; set; }
        public int ExecutionCount { get; set; }
        public string LastError { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// タスクスケジュール
    /// </summary>
    public class TaskSchedule
    {
        public ScheduleType Type { get; set; }
        public DateTime ExecutionTime { get; set; }
        public TimeSpan Interval { get; set; }
        public TimeSpan TimeOfDay { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// スケジュールタイプ
    /// </summary>
    public enum ScheduleType
    {
        Once,
        Interval,
        Daily,
        Weekly
    }

    /// <summary>
    /// タスクステータス
    /// </summary>
    public enum TaskStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }
}