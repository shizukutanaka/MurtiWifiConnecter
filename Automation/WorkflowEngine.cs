using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Automation
{
    /// <summary>
    /// ワークフローエンジン
    /// </summary>
    public interface IWorkflowEngine
    {
        Task<WorkflowResult> ExecuteWorkflowAsync(Workflow workflow);
        void RegisterAction(string actionType, Func<WorkflowAction, Task<ActionResult>> handler);
        List<Workflow> GetActiveWorkflows();
    }

    /// <summary>
    /// ワークフローエンジンの実装
    /// </summary>
    public class WorkflowEngine : IWorkflowEngine
    {
        private readonly Dictionary<string, Func<WorkflowAction, Task<ActionResult>>> _actionHandlers;
        private readonly List<Workflow> _activeWorkflows;

        public WorkflowEngine()
        {
            _actionHandlers = new Dictionary<string, Func<WorkflowAction, Task<ActionResult>>>();
            _activeWorkflows = new List<Workflow>();
            RegisterDefaultActions();
        }

        /// <summary>
        /// ワークフローを実行
        /// </summary>
        public async Task<WorkflowResult> ExecuteWorkflowAsync(Workflow workflow)
        {
            _activeWorkflows.Add(workflow);
            workflow.Status = WorkflowStatus.Running;
            workflow.StartTime = DateTime.Now;

            var result = new WorkflowResult
            {
                WorkflowId = workflow.Id,
                StartTime = workflow.StartTime.Value,
                ActionResults = new List<ActionResult>()
            };

            try
            {
                foreach (var action in workflow.Actions.OrderBy(a => a.Order))
                {
                    if (!ShouldExecuteAction(action, result.ActionResults))
                        continue;

                    var actionResult = await ExecuteActionAsync(action);
                    result.ActionResults.Add(actionResult);

                    if (!actionResult.Success && action.IsRequired)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"Required action '{action.Name}' failed: {actionResult.ErrorMessage}";
                        break;
                    }
                }

                if (result.Success && result.ActionResults.All(r => r.Success))
                {
                    workflow.Status = WorkflowStatus.Completed;
                    result.Success = true;
                }
                else if (!result.Success)
                {
                    workflow.Status = WorkflowStatus.Failed;
                }
            }
            catch (Exception ex)
            {
                workflow.Status = WorkflowStatus.Failed;
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                workflow.EndTime = DateTime.Now;
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                _activeWorkflows.Remove(workflow);
            }

            return result;
        }

        /// <summary>
        /// アクションハンドラーを登録
        /// </summary>
        public void RegisterAction(string actionType, Func<WorkflowAction, Task<ActionResult>> handler)
        {
            _actionHandlers[actionType] = handler;
        }

        /// <summary>
        /// アクティブなワークフローを取得
        /// </summary>
        public List<Workflow> GetActiveWorkflows()
        {
            return _activeWorkflows.ToList();
        }

        /// <summary>
        /// アクションを実行
        /// </summary>
        private async Task<ActionResult> ExecuteActionAsync(WorkflowAction action)
        {
            var result = new ActionResult
            {
                ActionId = action.Id,
                ActionName = action.Name,
                StartTime = DateTime.Now
            };

            try
            {
                if (_actionHandlers.TryGetValue(action.Type, out var handler))
                {
                    result = await handler(action);
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"No handler registered for action type: {action.Type}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// アクションを実行すべきかチェック
        /// </summary>
        private bool ShouldExecuteAction(WorkflowAction action, List<ActionResult> previousResults)
        {
            if (action.Conditions == null || !action.Conditions.Any())
                return true;

            foreach (var condition in action.Conditions)
            {
                if (!EvaluateCondition(condition, previousResults))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 条件を評価
        /// </summary>
        private bool EvaluateCondition(ActionCondition condition, List<ActionResult> previousResults)
        {
            var targetResult = previousResults.FirstOrDefault(r => r.ActionId == condition.DependsOnActionId);
            if (targetResult == null)
                return false;

            return condition.RequiredOutcome switch
            {
                ActionOutcome.Success => targetResult.Success,
                ActionOutcome.Failure => !targetResult.Success,
                ActionOutcome.Any => true,
                _ => false
            };
        }

        /// <summary>
        /// デフォルトアクションを登録
        /// </summary>
        private void RegisterDefaultActions()
        {
            // WiFi接続アクション
            RegisterAction("WifiConnect", async (action) =>
            {
                await Task.Delay(100); // シミュレーション
                return new ActionResult
                {
                    ActionId = action.Id,
                    ActionName = action.Name,
                    Success = true,
                    Data = new Dictionary<string, object> { { "SSID", action.Parameters.GetValueOrDefault("SSID") } }
                };
            });

            // WiFi切断アクション
            RegisterAction("WifiDisconnect", async (action) =>
            {
                await Task.Delay(50); // シミュレーション
                return new ActionResult
                {
                    ActionId = action.Id,
                    ActionName = action.Name,
                    Success = true
                };
            });

            // 通知アクション
            RegisterAction("Notification", async (action) =>
            {
                await Task.Delay(10); // シミュレーション
                return new ActionResult
                {
                    ActionId = action.Id,
                    ActionName = action.Name,
                    Success = true,
                    Data = new Dictionary<string, object> { { "Message", action.Parameters.GetValueOrDefault("Message") } }
                };
            });

            // 待機アクション
            RegisterAction("Wait", async (action) =>
            {
                if (action.Parameters.TryGetValue("Duration", out var durationStr) && 
                    int.TryParse(durationStr?.ToString(), out var duration))
                {
                    await Task.Delay(duration);
                }
                return new ActionResult
                {
                    ActionId = action.Id,
                    ActionName = action.Name,
                    Success = true
                };
            });
        }
    }

    /// <summary>
    /// ワークフロー
    /// </summary>
    public class Workflow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Description { get; set; }
        public List<WorkflowAction> Actions { get; set; } = new();
        public WorkflowStatus Status { get; set; } = WorkflowStatus.Pending;
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public Dictionary<string, object> Variables { get; set; } = new();
    }

    /// <summary>
    /// ワークフローアクション
    /// </summary>
    public class WorkflowAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Type { get; set; }
        public int Order { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
        public List<ActionCondition> Conditions { get; set; } = new();
        public bool IsRequired { get; set; } = true;
        public TimeSpan? Timeout { get; set; }
    }

    /// <summary>
    /// アクション条件
    /// </summary>
    public class ActionCondition
    {
        public string DependsOnActionId { get; set; }
        public ActionOutcome RequiredOutcome { get; set; }
    }

    /// <summary>
    /// ワークフロー結果
    /// </summary>
    public class WorkflowResult
    {
        public string WorkflowId { get; set; }
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public List<ActionResult> ActionResults { get; set; } = new();
    }

    /// <summary>
    /// アクション結果
    /// </summary>
    public class ActionResult
    {
        public string ActionId { get; set; }
        public string ActionName { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
    }

    /// <summary>
    /// ワークフローステータス
    /// </summary>
    public enum WorkflowStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// アクション結果
    /// </summary>
    public enum ActionOutcome
    {
        Success,
        Failure,
        Any
    }
}