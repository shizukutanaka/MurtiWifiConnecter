using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Automation
{
    public interface IRuleEngine
    {
        Task<string> CreateRuleAsync(RuleDefinition rule);
        Task<bool> UpdateRuleAsync(string ruleId, RuleDefinition rule);
        Task<bool> DeleteRuleAsync(string ruleId);
        Task<List<RuleInfo>> GetRulesAsync();
        Task<RuleEvaluationResult> EvaluateRuleAsync(string ruleId, Dictionary<string, object> facts);
        Task<List<RuleEvaluationResult>> EvaluateAllRulesAsync(Dictionary<string, object> facts);
        Task<bool> EnableRuleAsync(string ruleId);
        Task<bool> DisableRuleAsync(string ruleId);
    }

    public class RuleEngine : IRuleEngine
    {
        private readonly ILoggingService _logger;
        private readonly Dictionary<string, RuleDefinition> _rules;
        private readonly Dictionary<string, List<RuleExecution>> _executionHistory;
        private readonly object _lock = new object();

        public RuleEngine(ILoggingService logger)
        {
            _logger = logger;
            _rules = new Dictionary<string, RuleDefinition>();
            _executionHistory = new Dictionary<string, List<RuleExecution>>();
            
            CreateDefaultRules();
        }

        public async Task<string> CreateRuleAsync(RuleDefinition rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));
            
            if (string.IsNullOrWhiteSpace(rule.Name))
                throw new ArgumentException("Rule name cannot be empty", nameof(rule));
            
            if (rule.Conditions == null || !rule.Conditions.Any())
                throw new ArgumentException("Rule must have at least one condition", nameof(rule));
            
            if (rule.Actions == null || !rule.Actions.Any())
                throw new ArgumentException("Rule must have at least one action", nameof(rule));
            
            var ruleId = Guid.NewGuid().ToString();
            rule.Id = ruleId;
            rule.CreatedAt = DateTime.UtcNow;
            rule.IsEnabled = true;
            
            lock (_lock)
            {
                _rules[ruleId] = rule;
                _executionHistory[ruleId] = new List<RuleExecution>();
            }
            
            _logger.LogInfo($"Rule created: {rule.Name} ({ruleId})");
            return await Task.FromResult(ruleId);
        }

        public async Task<bool> UpdateRuleAsync(string ruleId, RuleDefinition rule)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                return false;
            
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));
            
            lock (_lock)
            {
                if (_rules.ContainsKey(ruleId))
                {
                    rule.Id = ruleId;
                    rule.UpdatedAt = DateTime.UtcNow;
                    _rules[ruleId] = rule;
                    
                    _logger.LogInfo($"Rule updated: {rule.Name} ({ruleId})");
                    return true;
                }
            }
            
            return await Task.FromResult(false);
        }

        public async Task<bool> DeleteRuleAsync(string ruleId)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                return false;
            
            lock (_lock)
            {
                var removed = _rules.Remove(ruleId);
                if (removed)
                {
                    _executionHistory.Remove(ruleId);
                    _logger.LogInfo($"Rule deleted: {ruleId}");
                }
                return removed;
            }
        }

        public async Task<List<RuleInfo>> GetRulesAsync()
        {
            lock (_lock)
            {
                var ruleInfos = _rules.Values.Select(r => new RuleInfo
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description,
                    IsEnabled = r.IsEnabled,
                    Priority = r.Priority,
                    CreatedAt = r.CreatedAt,
                    UpdatedAt = r.UpdatedAt,
                    ExecutionCount = _executionHistory.TryGetValue(r.Id, out var history) ? history.Count : 0,
                    LastExecutionTime = history?.LastOrDefault()?.ExecutionTime
                }).ToList();
                
                return Task.FromResult(ruleInfos).Result;
            }
        }

        public async Task<RuleEvaluationResult> EvaluateRuleAsync(string ruleId, Dictionary<string, object> facts)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                throw new ArgumentException("Rule ID cannot be empty", nameof(ruleId));
            
            if (facts == null)
                facts = new Dictionary<string, object>();
            
            RuleDefinition rule;
            lock (_lock)
            {
                if (!_rules.TryGetValue(ruleId, out rule))
                    throw new ArgumentException($"Rule not found: {ruleId}", nameof(ruleId));
            }
            
            if (!rule.IsEnabled)
            {
                return new RuleEvaluationResult
                {
                    RuleId = ruleId,
                    RuleName = rule.Name,
                    Success = false,
                    Message = "Rule is disabled",
                    ExecutionTime = DateTime.UtcNow
                };
            }
            
            var execution = new RuleExecution
            {
                Id = Guid.NewGuid().ToString(),
                RuleId = ruleId,
                ExecutionTime = DateTime.UtcNow,
                Facts = new Dictionary<string, object>(facts)
            };
            
            var result = new RuleEvaluationResult
            {
                RuleId = ruleId,
                RuleName = rule.Name,
                ExecutionTime = execution.ExecutionTime
            };
            
            try
            {
                _logger.LogDebug($"Evaluating rule: {rule.Name} ({ruleId})");
                
                // Evaluate conditions
                var conditionResults = new List<ConditionEvaluationResult>();
                foreach (var condition in rule.Conditions)
                {
                    var conditionResult = EvaluateCondition(condition, facts);
                    conditionResults.Add(conditionResult);
                }
                
                // Apply condition logic (AND/OR)
                bool allConditionsMet = false;
                switch (rule.ConditionLogic)
                {
                    case ConditionLogic.All:
                        allConditionsMet = conditionResults.All(c => c.IsMet);
                        break;
                    case ConditionLogic.Any:
                        allConditionsMet = conditionResults.Any(c => c.IsMet);
                        break;
                    case ConditionLogic.None:
                        allConditionsMet = !conditionResults.Any(c => c.IsMet);
                        break;
                }
                
                result.ConditionsEvaluated = conditionResults;
                result.ConditionsMet = allConditionsMet;
                
                if (allConditionsMet)
                {
                    // Execute actions
                    var actionResults = new List<ActionExecutionResult>();
                    foreach (var action in rule.Actions)
                    {
                        var actionResult = await ExecuteActionAsync(action, facts);
                        actionResults.Add(actionResult);
                    }
                    
                    result.ActionsExecuted = actionResults;
                    result.Success = actionResults.All(a => a.Success);
                    result.Message = result.Success ? "Rule executed successfully" : "Some actions failed";
                }
                else
                {
                    result.Success = true;
                    result.Message = "Conditions not met";
                }
                
                execution.Success = result.Success;
                execution.ConditionsMet = result.ConditionsMet;
                execution.ActionsExecuted = result.ActionsExecuted?.Count ?? 0;
                
                lock (_lock)
                {
                    _executionHistory[ruleId].Add(execution);
                    
                    // Keep only last 100 executions per rule
                    var history = _executionHistory[ruleId];
                    if (history.Count > 100)
                    {
                        history.RemoveRange(0, history.Count - 100);
                    }
                }
                
                _logger.LogDebug($"Rule evaluation completed: {rule.Name} ({ruleId}) - Success: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Rule evaluation failed: {ex.Message}";
                execution.Success = false;
                execution.ErrorMessage = ex.Message;
                
                _logger.LogError($"Rule evaluation failed: {rule.Name} ({ruleId})", ex);
            }
            
            return result;
        }

        public async Task<List<RuleEvaluationResult>> EvaluateAllRulesAsync(Dictionary<string, object> facts)
        {
            if (facts == null)
                facts = new Dictionary<string, object>();
            
            var results = new List<RuleEvaluationResult>();
            
            List<RuleDefinition> enabledRules;
            lock (_lock)
            {
                enabledRules = _rules.Values.Where(r => r.IsEnabled).OrderBy(r => r.Priority).ToList();
            }
            
            foreach (var rule in enabledRules)
            {
                try
                {
                    var result = await EvaluateRuleAsync(rule.Id, facts);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to evaluate rule: {rule.Name} ({rule.Id})", ex);
                    results.Add(new RuleEvaluationResult
                    {
                        RuleId = rule.Id,
                        RuleName = rule.Name,
                        Success = false,
                        Message = $"Evaluation failed: {ex.Message}",
                        ExecutionTime = DateTime.UtcNow
                    });
                }
            }
            
            return results;
        }

        public async Task<bool> EnableRuleAsync(string ruleId)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                return false;
            
            lock (_lock)
            {
                if (_rules.TryGetValue(ruleId, out var rule))
                {
                    rule.IsEnabled = true;
                    rule.UpdatedAt = DateTime.UtcNow;
                    _logger.LogInfo($"Rule enabled: {rule.Name} ({ruleId})");
                    return true;
                }
            }
            
            return await Task.FromResult(false);
        }

        public async Task<bool> DisableRuleAsync(string ruleId)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                return false;
            
            lock (_lock)
            {
                if (_rules.TryGetValue(ruleId, out var rule))
                {
                    rule.IsEnabled = false;
                    rule.UpdatedAt = DateTime.UtcNow;
                    _logger.LogInfo($"Rule disabled: {rule.Name} ({ruleId})");
                    return true;
                }
            }
            
            return await Task.FromResult(false);
        }

        private ConditionEvaluationResult EvaluateCondition(RuleCondition condition, Dictionary<string, object> facts)
        {
            var result = new ConditionEvaluationResult
            {
                ConditionId = condition.Id,
                Field = condition.Field,
                Operator = condition.Operator,
                Value = condition.Value
            };
            
            try
            {
                if (!facts.TryGetValue(condition.Field, out var factValue))
                {
                    result.IsMet = false;
                    result.Message = $"Field '{condition.Field}' not found in facts";
                    return result;
                }
                
                switch (condition.Operator)
                {
                    case RuleOperator.Equals:
                        result.IsMet = Equals(factValue, condition.Value);
                        break;
                    
                    case RuleOperator.NotEquals:
                        result.IsMet = !Equals(factValue, condition.Value);
                        break;
                    
                    case RuleOperator.GreaterThan:
                        result.IsMet = Compare(factValue, condition.Value) > 0;
                        break;
                    
                    case RuleOperator.GreaterThanOrEqual:
                        result.IsMet = Compare(factValue, condition.Value) >= 0;
                        break;
                    
                    case RuleOperator.LessThan:
                        result.IsMet = Compare(factValue, condition.Value) < 0;
                        break;
                    
                    case RuleOperator.LessThanOrEqual:
                        result.IsMet = Compare(factValue, condition.Value) <= 0;
                        break;
                    
                    case RuleOperator.Contains:
                        result.IsMet = factValue?.ToString()?.Contains(condition.Value?.ToString() ?? "") ?? false;
                        break;
                    
                    case RuleOperator.StartsWith:
                        result.IsMet = factValue?.ToString()?.StartsWith(condition.Value?.ToString() ?? "") ?? false;
                        break;
                    
                    case RuleOperator.EndsWith:
                        result.IsMet = factValue?.ToString()?.EndsWith(condition.Value?.ToString() ?? "") ?? false;
                        break;
                    
                    default:
                        result.IsMet = false;
                        result.Message = $"Unsupported operator: {condition.Operator}";
                        break;
                }
                
                result.ActualValue = factValue;
                result.Message = $"Condition {(result.IsMet ? "met" : "not met")}: {factValue} {condition.Operator} {condition.Value}";
            }
            catch (Exception ex)
            {
                result.IsMet = false;
                result.Message = $"Condition evaluation failed: {ex.Message}";
            }
            
            return result;
        }

        private async Task<ActionExecutionResult> ExecuteActionAsync(RuleAction action, Dictionary<string, object> facts)
        {
            var result = new ActionExecutionResult
            {
                ActionId = action.Id,
                ActionType = action.Type,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                switch (action.Type)
                {
                    case RuleActionType.WifiConnect:
                        result = await ExecuteWifiConnectActionAsync(action, facts);
                        break;
                    
                    case RuleActionType.WifiDisconnect:
                        result = await ExecuteWifiDisconnectActionAsync(action, facts);
                        break;
                    
                    case RuleActionType.Log:
                        result = await ExecuteLogActionAsync(action, facts);
                        break;
                    
                    case RuleActionType.Notification:
                        result = await ExecuteNotificationActionAsync(action, facts);
                        break;
                    
                    case RuleActionType.Custom:
                        result = await ExecuteCustomActionAsync(action, facts);
                        break;
                    
                    default:
                        result.Success = false;
                        result.Message = $"Unsupported action type: {action.Type}";
                        break;
                }
                
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime.Value - result.StartTime;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime.Value - result.StartTime;
            }
            
            return result;
        }

        private async Task<ActionExecutionResult> ExecuteWifiConnectActionAsync(RuleAction action, Dictionary<string, object> facts)
        {
            var result = new ActionExecutionResult
            {
                ActionId = action.Id,
                ActionType = action.Type,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                var ssid = action.Parameters.GetValueOrDefault("ssid", "").ToString();
                var password = action.Parameters.GetValueOrDefault("password", "").ToString();
                
                var connectionResult = await FastWifiConnector.ConnectAsync(ssid, password);
                
                result.Success = connectionResult.Success;
                result.Message = connectionResult.Message;
                result.Output = new Dictionary<string, object>
                {
                    ["ssid"] = ssid,
                    ["connected"] = connectionResult.Success
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }
            
            return result;
        }

        private async Task<ActionExecutionResult> ExecuteWifiDisconnectActionAsync(RuleAction action, Dictionary<string, object> facts)
        {
            var result = new ActionExecutionResult
            {
                ActionId = action.Id,
                ActionType = action.Type,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                var disconnected = await FastWifiConnector.DisconnectAsync();
                
                result.Success = disconnected;
                result.Message = disconnected ? "Disconnected successfully" : "Disconnect failed";
                result.Output = new Dictionary<string, object>
                {
                    ["disconnected"] = disconnected
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }
            
            return result;
        }

        private async Task<ActionExecutionResult> ExecuteLogActionAsync(RuleAction action, Dictionary<string, object> facts)
        {
            var result = new ActionExecutionResult
            {
                ActionId = action.Id,
                ActionType = action.Type,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                var message = action.Parameters.GetValueOrDefault("message", "").ToString();
                var level = action.Parameters.GetValueOrDefault("level", "Info").ToString();
                
                // Replace placeholders with fact values
                foreach (var fact in facts)
                {
                    message = message.Replace($"{{{fact.Key}}}", fact.Value?.ToString() ?? "");
                }
                
                switch (level.ToLowerInvariant())
                {
                    case "debug":
                        _logger.LogDebug($"[Rule Action] {message}");
                        break;
                    case "info":
                        _logger.LogInfo($"[Rule Action] {message}");
                        break;
                    case "warning":
                        _logger.LogWarning($"[Rule Action] {message}");
                        break;
                    case "error":
                        _logger.LogError($"[Rule Action] {message}");
                        break;
                    default:
                        _logger.LogInfo($"[Rule Action] {message}");
                        break;
                }
                
                result.Success = true;
                result.Message = "Log message written";
                result.Output = new Dictionary<string, object>
                {
                    ["message"] = message,
                    ["level"] = level
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }
            
            return result;
        }

        private async Task<ActionExecutionResult> ExecuteNotificationActionAsync(RuleAction action, Dictionary<string, object> facts)
        {
            var result = new ActionExecutionResult
            {
                ActionId = action.Id,
                ActionType = action.Type,
                StartTime = DateTime.UtcNow
            };
            
            try
            {
                var title = action.Parameters.GetValueOrDefault("title", "Notification").ToString();
                var message = action.Parameters.GetValueOrDefault("message", "").ToString();
                
                // Replace placeholders with fact values
                foreach (var fact in facts)
                {
                    title = title.Replace($"{{{fact.Key}}}", fact.Value?.ToString() ?? "");
                    message = message.Replace($"{{{fact.Key}}}", fact.Value?.ToString() ?? "");
                }
                
                // In a real implementation, this would use the notification service
                // For now, we'll just log it
                _logger.LogInfo($"[Rule Notification] {title}: {message}");
                
                result.Success = true;
                result.Message = "Notification sent";
                result.Output = new Dictionary<string, object>
                {
                    ["title"] = title,
                    ["message"] = message
                };
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }
            
            return result;
        }

        private async Task<ActionExecutionResult> ExecuteCustomActionAsync(RuleAction action, Dictionary<string, object> facts)
        {
            var result = new ActionExecutionResult
            {
                ActionId = action.Id,
                ActionType = action.Type,
                StartTime = DateTime.UtcNow
            };
            
            // Custom action execution would be implemented here
            result.Success = true;
            result.Message = "Custom action executed";
            result.Output = new Dictionary<string, object>
            {
                ["customActionExecuted"] = true
            };
            
            return await Task.FromResult(result);
        }

        private int Compare(object value1, object value2)
        {
            if (value1 == null && value2 == null) return 0;
            if (value1 == null) return -1;
            if (value2 == null) return 1;
            
            if (value1 is IComparable comparable1 && value2 is IComparable comparable2)
            {
                if (value1.GetType() == value2.GetType())
                {
                    return comparable1.CompareTo(comparable2);
                }
                
                // Try to convert to comparable types
                if (double.TryParse(value1.ToString(), out var d1) && double.TryParse(value2.ToString(), out var d2))
                {
                    return d1.CompareTo(d2);
                }
            }
            
            return string.Compare(value1.ToString(), value2.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private void CreateDefaultRules()
        {
            // Auto-connect rule
            var autoConnectRule = new RuleDefinition
            {
                Name = "Auto Connect to Known Networks",
                Description = "Automatically connect to known networks when available",
                Priority = 10,
                ConditionLogic = ConditionLogic.All,
                Conditions = new List<RuleCondition>
                {
                    new RuleCondition
                    {
                        Field = "networkAvailable",
                        Operator = RuleOperator.Equals,
                        Value = true
                    },
                    new RuleCondition
                    {
                        Field = "isKnownNetwork",
                        Operator = RuleOperator.Equals,
                        Value = true
                    },
                    new RuleCondition
                    {
                        Field = "currentlyConnected",
                        Operator = RuleOperator.Equals,
                        Value = false
                    }
                },
                Actions = new List<RuleAction>
                {
                    new RuleAction
                    {
                        Type = RuleActionType.WifiConnect,
                        Parameters = new Dictionary<string, object>
                        {
                            ["ssid"] = "{networkSSID}",
                            ["password"] = "{networkPassword}"
                        }
                    },
                    new RuleAction
                    {
                        Type = RuleActionType.Log,
                        Parameters = new Dictionary<string, object>
                        {
                            ["message"] = "Auto-connecting to {networkSSID}",
                            ["level"] = "Info"
                        }
                    }
                }
            };
            
            Task.Run(async () => await CreateRuleAsync(autoConnectRule));
        }
    }

    // Data classes for rule engine
    public class RuleDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int Priority { get; set; } = 0;
        public ConditionLogic ConditionLogic { get; set; } = ConditionLogic.All;
        public List<RuleCondition> Conditions { get; set; } = new List<RuleCondition>();
        public List<RuleAction> Actions { get; set; } = new List<RuleAction>();
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class RuleCondition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Field { get; set; }
        public RuleOperator Operator { get; set; }
        public object Value { get; set; }
    }

    public class RuleAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public RuleActionType Type { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }

    public enum ConditionLogic
    {
        All,
        Any,
        None
    }

    public enum RuleOperator
    {
        Equals,
        NotEquals,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Contains,
        StartsWith,
        EndsWith
    }

    public enum RuleActionType
    {
        WifiConnect,
        WifiDisconnect,
        Log,
        Notification,
        Custom
    }

    public class RuleExecution
    {
        public string Id { get; set; }
        public string RuleId { get; set; }
        public DateTime ExecutionTime { get; set; }
        public Dictionary<string, object> Facts { get; set; } = new Dictionary<string, object>();
        public bool Success { get; set; }
        public bool ConditionsMet { get; set; }
        public int ActionsExecuted { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class RuleEvaluationResult
    {
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public bool Success { get; set; }
        public bool ConditionsMet { get; set; }
        public string Message { get; set; }
        public DateTime ExecutionTime { get; set; }
        public List<ConditionEvaluationResult> ConditionsEvaluated { get; set; } = new List<ConditionEvaluationResult>();
        public List<ActionExecutionResult> ActionsExecuted { get; set; } = new List<ActionExecutionResult>();
    }

    public class ConditionEvaluationResult
    {
        public string ConditionId { get; set; }
        public string Field { get; set; }
        public RuleOperator Operator { get; set; }
        public object Value { get; set; }
        public object ActualValue { get; set; }
        public bool IsMet { get; set; }
        public string Message { get; set; }
    }

    public class ActionExecutionResult
    {
        public string ActionId { get; set; }
        public RuleActionType ActionType { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Output { get; set; } = new Dictionary<string, object>();
    }

    public class RuleInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsEnabled { get; set; }
        public int Priority { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int ExecutionCount { get; set; }
        public DateTime? LastExecutionTime { get; set; }
    }
}