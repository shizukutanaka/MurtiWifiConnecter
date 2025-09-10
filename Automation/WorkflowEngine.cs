using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Automation
{
    public interface IWorkflowEngine
    {
        Task<string> CreateWorkflowAsync(WorkflowDefinition definition);
        Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object> parameters = null);
        Task<bool> DeleteWorkflowAsync(string workflowId);
        Task<List<WorkflowInfo>> GetWorkflowsAsync();
        Task<WorkflowExecutionHistory> GetExecutionHistoryAsync(string workflowId);
    }

    public class WorkflowEngine : IWorkflowEngine, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly Dictionary<string, WorkflowDefinition> _workflows;
        private readonly Dictionary<string, List<WorkflowExecution>> _executionHistory;
        private readonly object _lock = new object();

        public WorkflowEngine(ILoggingService logger)
        {
            _logger = logger;
            _workflows = new Dictionary<string, WorkflowDefinition>();
            _executionHistory = new Dictionary<string, List<WorkflowExecution>>();
        }

        public async Task<string> CreateWorkflowAsync(WorkflowDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            
            if (string.IsNullOrWhiteSpace(definition.Name))
                throw new ArgumentException("Workflow name cannot be empty", nameof(definition));
            
            if (definition.Steps == null || !definition.Steps.Any())
                throw new ArgumentException("Workflow must have at least one step", nameof(definition));
            
            var workflowId = Guid.NewGuid().ToString();
            definition.Id = workflowId;
            definition.CreatedAt = DateTime.UtcNow;
            
            lock (_lock)
            {
                _workflows[workflowId] = definition;
                _executionHistory[workflowId] = new List<WorkflowExecution>();
            }
            
            _logger.LogInfo($"Workflow created: {definition.Name} ({workflowId})");
            return await Task.FromResult(workflowId);
        }

        public async Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(workflowId))
                throw new ArgumentException("Workflow ID cannot be empty", nameof(workflowId));
            
            WorkflowDefinition workflow;
            lock (_lock)
            {
                if (!_workflows.TryGetValue(workflowId, out workflow))
                    throw new ArgumentException($"Workflow not found: {workflowId}", nameof(workflowId));
            }
            
            var execution = new WorkflowExecution
            {
                Id = Guid.NewGuid().ToString(),
                WorkflowId = workflowId,
                StartTime = DateTime.UtcNow,
                Parameters = parameters ?? new Dictionary<string, object>(),
                Status = WorkflowExecutionStatus.Running,
                StepResults = new List<WorkflowStepResult>()
            };
            
            var result = new WorkflowExecutionResult
            {
                ExecutionId = execution.Id,
                WorkflowId = workflowId,
                StartTime = execution.StartTime,
                Status = WorkflowExecutionStatus.Running
            };
            
            lock (_lock)
            {
                _executionHistory[workflowId].Add(execution);
            }
            
            _logger.LogInfo($"Starting workflow execution: {workflow.Name} ({execution.Id})");
            
            try
            {
                var context = new WorkflowExecutionContext
                {
                    WorkflowId = workflowId,
                    ExecutionId = execution.Id,
                    Parameters = execution.Parameters,
                    Variables = new Dictionary<string, object>()
                };
                
                foreach (var step in workflow.Steps.OrderBy(s => s.Order))
                {
                    var stepResult = await ExecuteStepAsync(step, context);
                    execution.StepResults.Add(stepResult);
                    result.StepResults.Add(stepResult);
                    
                    if (!stepResult.Success && step.StopOnFailure)
                    {
                        execution.Status = WorkflowExecutionStatus.Failed;
                        result.Status = WorkflowExecutionStatus.Failed;
                        result.ErrorMessage = stepResult.ErrorMessage;
                        break;
                    }
                    
                    // Update context variables with step outputs
                    if (stepResult.Outputs != null)
                    {
                        foreach (var output in stepResult.Outputs)
                        {
                            context.Variables[output.Key] = output.Value;
                        }
                    }
                }
                
                if (result.Status == WorkflowExecutionStatus.Running)
                {
                    execution.Status = WorkflowExecutionStatus.Completed;
                    result.Status = WorkflowExecutionStatus.Completed;
                }
                
                execution.EndTime = DateTime.UtcNow;
                result.EndTime = execution.EndTime.Value;
                result.Duration = result.EndTime - result.StartTime;
                
                _logger.LogInfo($"Workflow execution completed: {workflow.Name} ({execution.Id}) - Status: {result.Status}");
            }
            catch (Exception ex)
            {
                execution.Status = WorkflowExecutionStatus.Failed;
                execution.EndTime = DateTime.UtcNow;
                execution.ErrorMessage = ex.Message;
                
                result.Status = WorkflowExecutionStatus.Failed;
                result.EndTime = execution.EndTime.Value;
                result.Duration = result.EndTime - result.StartTime;
                result.ErrorMessage = ex.Message;
                
                _logger.LogError($"Workflow execution failed: {workflow.Name} ({execution.Id})", ex);
            }
            
            return result;
        }

        public async Task<bool> DeleteWorkflowAsync(string workflowId)
        {
            if (string.IsNullOrWhiteSpace(workflowId))
                return false;
            
            lock (_lock)
            {
                var removed = _workflows.Remove(workflowId);
                if (removed)
                {
                    _executionHistory.Remove(workflowId);
                    _logger.LogInfo($"Workflow deleted: {workflowId}");
                }
                return removed;
            }
        }

        public async Task<List<WorkflowInfo>> GetWorkflowsAsync()
        {
            lock (_lock)
            {
                var workflowInfos = _workflows.Values.Select(w => new WorkflowInfo
                {
                    Id = w.Id,
                    Name = w.Name,
                    Description = w.Description,
                    CreatedAt = w.CreatedAt,
                    StepCount = w.Steps?.Count ?? 0,
                    LastExecutionTime = _executionHistory.TryGetValue(w.Id, out var history) 
                        ? history.LastOrDefault()?.StartTime 
                        : null
                }).ToList();
                
                return workflowInfos;
            }
        }

        public async Task<WorkflowExecutionHistory> GetExecutionHistoryAsync(string workflowId)
        {
            if (string.IsNullOrWhiteSpace(workflowId))
                throw new ArgumentException("Workflow ID cannot be empty", nameof(workflowId));
            
            lock (_lock)
            {
                if (!_workflows.TryGetValue(workflowId, out var workflow))
                    throw new ArgumentException($"Workflow not found: {workflowId}", nameof(workflowId));
                
                var executions = _executionHistory.TryGetValue(workflowId, out var history) 
                    ? history.ToList() 
                    : new List<WorkflowExecution>();
                
                return await Task.FromResult(new WorkflowExecutionHistory
                {
                    WorkflowId = workflowId,
                    WorkflowName = workflow.Name,
                    Executions = executions
                });
            }
        }

        private async Task<WorkflowStepResult> ExecuteStepAsync(WorkflowStep step, WorkflowExecutionContext context)
        {
            var stepResult = new WorkflowStepResult
            {
                StepId = step.Id,
                StepName = step.Name,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                _logger.LogDebug($"Executing workflow step: {step.Name} ({step.Id})");
                
                switch (step.Type)
                {
                    case WorkflowStepType.WifiConnect:
                        stepResult = await ExecuteWifiConnectStepAsync(step, context);
                        break;
                    
                    case WorkflowStepType.WifiDisconnect:
                        stepResult = await ExecuteWifiDisconnectStepAsync(step, context);
                        break;
                    
                    case WorkflowStepType.WifiScan:
                        stepResult = await ExecuteWifiScanStepAsync(step, context);
                        break;
                    
                    case WorkflowStepType.Delay:
                        stepResult = await ExecuteDelayStepAsync(step, context);
                        break;
                    
                    case WorkflowStepType.Condition:
                        stepResult = await ExecuteConditionStepAsync(step, context);
                        break;
                    
                    case WorkflowStepType.Log:
                        stepResult = await ExecuteLogStepAsync(step, context);
                        break;
                    
                    case WorkflowStepType.Custom:
                        stepResult = await ExecuteCustomStepAsync(step, context);
                        break;
                    
                    default:
                        throw new NotSupportedException($"Workflow step type not supported: {step.Type}");
                }
                
                stepResult.EndTime = DateTime.UtcNow;
                stepResult.Duration = stepResult.EndTime.Value - stepResult.StartTime;
                
                _logger.LogDebug($"Workflow step completed: {step.Name} ({step.Id}) - Success: {stepResult.Success}");
            }
            catch (Exception ex)
            {
                stepResult.Success = false;
                stepResult.ErrorMessage = ex.Message;
                stepResult.EndTime = DateTime.UtcNow;
                stepResult.Duration = stepResult.EndTime.Value - stepResult.StartTime;
                
                _logger.LogError($"Workflow step failed: {step.Name} ({step.Id})", ex);
            }
            
            return stepResult;
        }

        private async Task<WorkflowStepResult> ExecuteWifiConnectStepAsync(WorkflowStep step, WorkflowExecutionContext context)
        {
            var result = new WorkflowStepResult
            {
                StepId = step.Id,
                StepName = step.Name,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                var ssid = ResolveParameter(step.Parameters.GetValueOrDefault("ssid", "").ToString(), context);
                var password = ResolveParameter(step.Parameters.GetValueOrDefault("password", "").ToString(), context);
                
                var connectionResult = await FastWifiConnector.ConnectAsync(ssid, password);
                
                result.Success = connectionResult.Success;
                result.ErrorMessage = connectionResult.Success ? null : connectionResult.Message;
                result.Outputs = new Dictionary<string, object>
                {
                    ["connected"] = connectionResult.Success,
                    ["ssid"] = ssid,
                    ["message"] = connectionResult.Message
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            return result;
        }

        private async Task<WorkflowStepResult> ExecuteWifiDisconnectStepAsync(WorkflowStep step, WorkflowExecutionContext context)
        {
            var result = new WorkflowStepResult
            {
                StepId = step.Id,
                StepName = step.Name,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                var disconnected = await FastWifiConnector.DisconnectAsync();
                
                result.Success = disconnected;
                result.Outputs = new Dictionary<string, object>
                {
                    ["disconnected"] = disconnected
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            return result;
        }

        private async Task<WorkflowStepResult> ExecuteWifiScanStepAsync(WorkflowStep step, WorkflowExecutionContext context)
        {
            var result = new WorkflowStepResult
            {
                StepId = step.Id,
                StepName = step.Name,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                var networks = await NetworkUtils.ScanWifiNetworksAsync();
                
                result.Success = true;
                result.Outputs = new Dictionary<string, object>
                {
                    ["networkCount"] = networks.Count,
                    ["networks"] = networks
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            return result;
        }

        private async Task<WorkflowStepResult> ExecuteDelayStepAsync(WorkflowStep step, WorkflowExecutionContext context)
        {
            var result = new WorkflowStepResult
            {
                StepId = step.Id,
                StepName = step.Name,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                var delayMs = Convert.ToInt32(step.Parameters.GetValueOrDefault("delayMs", 1000));
                await Task.Delay(delayMs);
                
                result.Success = true;
                result.Outputs = new Dictionary<string, object>
                {
                    ["delayMs"] = delayMs
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            return result;
        }

        private async Task<WorkflowStepResult> ExecuteConditionStepAsync(WorkflowStep step, WorkflowExecutionContext context)
        {
            var result = new WorkflowStepResult
            {
                StepId = step.Id,
                StepName = step.Name,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                var condition = step.Parameters.GetValueOrDefault("condition", "").ToString();
                var conditionResult = EvaluateCondition(condition, context);
                
                result.Success = true;
                result.Outputs = new Dictionary<string, object>
                {
                    ["conditionResult"] = conditionResult,
                    ["condition"] = condition
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            return result;
        }

        private async Task<WorkflowStepResult> ExecuteLogStepAsync(WorkflowStep step, WorkflowExecutionContext context)
        {
            var result = new WorkflowStepResult
            {
                StepId = step.Id,
                StepName = step.Name,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                var message = ResolveParameter(step.Parameters.GetValueOrDefault("message", "").ToString(), context);
                var level = step.Parameters.GetValueOrDefault("level", "Info").ToString();
                
                switch (level.ToLowerInvariant())
                {
                    case "debug":
                        _logger.LogDebug(message);
                        break;
                    case "info":
                        _logger.LogInfo(message);
                        break;
                    case "warning":
                        _logger.LogWarning(message);
                        break;
                    case "error":
                        _logger.LogError(message);
                        break;
                    default:
                        _logger.LogInfo(message);
                        break;
                }
                
                result.Success = true;
                result.Outputs = new Dictionary<string, object>
                {
                    ["message"] = message,
                    ["level"] = level
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            return result;
        }

        private async Task<WorkflowStepResult> ExecuteCustomStepAsync(WorkflowStep step, WorkflowExecutionContext context)
        {
            var result = new WorkflowStepResult
            {
                StepId = step.Id,
                StepName = step.Name,
                StartTime = DateTime.UtcNow
            };
            
            // Custom step execution would be implemented here
            // This is a placeholder for extensibility
            result.Success = true;
            result.Outputs = new Dictionary<string, object>
            {
                ["customStepExecuted"] = true
            };
            
            return await Task.FromResult(result);
        }

        private string ResolveParameter(string parameter, WorkflowExecutionContext context)
        {
            if (string.IsNullOrEmpty(parameter))
                return parameter;
            
            // Simple variable substitution: ${variableName}
            foreach (var variable in context.Variables)
            {
                parameter = parameter.Replace($"${{{variable.Key}}}", variable.Value?.ToString() ?? "");
            }
            
            foreach (var param in context.Parameters)
            {
                parameter = parameter.Replace($"${{{param.Key}}}", param.Value?.ToString() ?? "");
            }
            
            return parameter;
        }

        private bool EvaluateCondition(string condition, WorkflowExecutionContext context)
        {
            // Simple condition evaluation - in a real implementation, you'd use a proper expression evaluator
            condition = ResolveParameter(condition, context);
            
            // Basic condition parsing (very simplified)
            if (condition.Contains("=="))
            {
                var parts = condition.Split(new[] { "==" }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    return parts[0].Trim().Equals(parts[1].Trim(), StringComparison.OrdinalIgnoreCase);
                }
            }
            
            // Default to true for now
            return true;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _workflows.Clear();
                _executionHistory.Clear();
            }
            
            _logger.LogInfo("WorkflowEngine disposed");
        }
    }

    // Data classes for workflow system
    public class WorkflowDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
        public DateTime CreatedAt { get; set; }
    }

    public class WorkflowStep
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public int Order { get; set; }
        public WorkflowStepType Type { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
        public bool StopOnFailure { get; set; } = true;
    }

    public enum WorkflowStepType
    {
        WifiConnect,
        WifiDisconnect,
        WifiScan,
        Delay,
        Condition,
        Log,
        Custom
    }

    public class WorkflowExecutionContext
    {
        public string WorkflowId { get; set; }
        public string ExecutionId { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();
    }

    public class WorkflowExecution
    {
        public string Id { get; set; }
        public string WorkflowId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
        public WorkflowExecutionStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        public List<WorkflowStepResult> StepResults { get; set; } = new List<WorkflowStepResult>();
    }

    public class WorkflowExecutionResult
    {
        public string ExecutionId { get; set; }
        public string WorkflowId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public WorkflowExecutionStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        public List<WorkflowStepResult> StepResults { get; set; } = new List<WorkflowStepResult>();
    }

    public class WorkflowStepResult
    {
        public string StepId { get; set; }
        public string StepName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public Dictionary<string, object> Outputs { get; set; } = new Dictionary<string, object>();
    }

    public class WorkflowInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public int StepCount { get; set; }
        public DateTime? LastExecutionTime { get; set; }
    }

    public class WorkflowExecutionHistory
    {
        public string WorkflowId { get; set; }
        public string WorkflowName { get; set; }
        public List<WorkflowExecution> Executions { get; set; } = new List<WorkflowExecution>();
    }

    public enum WorkflowExecutionStatus
    {
        Scheduled,
        Running,
        Completed,
        Failed,
        Canceled
    }
}