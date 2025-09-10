using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MurtiWifiConnecter.Infrastructure.ExceptionHandling
{
    /// <summary>
    /// ネットワーク操作例外ハンドラー
    /// </summary>
    public class NetworkOperationExceptionHandler : IExceptionHandler
    {
        public int Priority => 10;

        public async Task<bool> CanHandleAsync(ExceptionProcessingContext context)
        {
            return context.Exception is NetworkOperationException ||
                   context.Exception is WifiOperationException ||
                   context.Exception.Message.Contains("network", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ExceptionHandlingResult> HandleAsync(ExceptionProcessingContext context)
        {
            var result = new ExceptionHandlingResult
            {
                WasHandled = true,
                RecoveryActions = new List<RecoveryAction>()
            };

            if (context.Exception is NetworkOperationException netEx)
            {
                result.SuggestedUserAction = netEx.OperationType switch
                {
                    NetworkOperationType.Scan => "ネットワークスキャンを再実行してください。WiFiが有効になっていることを確認してください。",
                    NetworkOperationType.Connect => $"'{netEx.NetworkSSID}'への接続を再試行してください。パスワードが正しいことを確認してください。",
                    NetworkOperationType.Disconnect => "切断処理を再試行してください。",
                    _ => "ネットワーク操作を再試行してください。"
                };

                // 復旧アクションの追加
                if (netEx.OperationType == NetworkOperationType.Connect)
                {
                    result.RecoveryActions.Add(new RecoveryAction
                    {
                        ActionType = "RetryConnection",
                        Description = "接続を再試行",
                        Priority = 1,
                        ExecuteAsync = async () =>
                        {
                            await Task.Delay(2000); // 2秒待機してから再試行
                            return true; // 実際の再接続ロジックに置き換える
                        }
                    });

                    result.RecoveryActions.Add(new RecoveryAction
                    {
                        ActionType = "RefreshNetworkList",
                        Description = "ネットワーク一覧の更新",
                        Priority = 2
                    });
                }
            }
            else
            {
                result.SuggestedUserAction = "ネットワーク接続を確認して、操作を再試行してください。";
                
                result.RecoveryActions.Add(new RecoveryAction
                {
                    ActionType = "CheckNetworkAdapter",
                    Description = "ネットワークアダプターの確認",
                    Priority = 1
                });
            }

            return result;
        }
    }

    /// <summary>
    /// 認証例外ハンドラー
    /// </summary>
    public class AuthenticationExceptionHandler : IExceptionHandler
    {
        public int Priority => 20;

        public async Task<bool> CanHandleAsync(ExceptionProcessingContext context)
        {
            return context.Exception is AuthenticationException ||
                   context.Exception.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                   context.Exception.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ExceptionHandlingResult> HandleAsync(ExceptionProcessingContext context)
        {
            var result = new ExceptionHandlingResult
            {
                WasHandled = true,
                RecoveryActions = new List<RecoveryAction>()
            };

            if (context.Exception is AuthenticationException authEx)
            {
                result.SuggestedUserAction = authEx.FailureReason switch
                {
                    AuthenticationFailureReason.InvalidPassword => 
                        $"'{authEx.NetworkSSID}'のパスワードが間違っています。正しいパスワードを入力してください。",
                    AuthenticationFailureReason.NetworkNotFound => 
                        $"ネットワーク'{authEx.NetworkSSID}'が見つかりません。ネットワーク一覧を更新してください。",
                    AuthenticationFailureReason.AccessDenied => 
                        $"'{authEx.NetworkSSID}'への接続が拒否されました。ネットワーク管理者にお問い合わせください。",
                    AuthenticationFailureReason.Timeout => 
                        "認証がタイムアウトしました。しばらく時間をおいてから再試行してください。",
                    _ => "認証に失敗しました。ネットワーク設定を確認してください。"
                };

                // 復旧アクション
                result.RecoveryActions.Add(new RecoveryAction
                {
                    ActionType = "PromptForNewPassword",
                    Description = "パスワードの再入力を促す",
                    Priority = 1
                });

                if (authEx.FailureReason == AuthenticationFailureReason.NetworkNotFound)
                {
                    result.RecoveryActions.Add(new RecoveryAction
                    {
                        ActionType = "RefreshNetworkList",
                        Description = "ネットワーク一覧の更新",
                        Priority = 2
                    });
                }
            }
            else
            {
                result.SuggestedUserAction = "認証情報を確認して、再度接続を試行してください。";
                
                result.RecoveryActions.Add(new RecoveryAction
                {
                    ActionType = "ClearSavedPassword",
                    Description = "保存されたパスワードのクリア",
                    Priority = 1
                });
            }

            return result;
        }
    }

    /// <summary>
    /// 設定例外ハンドラー
    /// </summary>
    public class ConfigurationExceptionHandler : IExceptionHandler
    {
        public int Priority => 30;

        public async Task<bool> CanHandleAsync(ExceptionProcessingContext context)
        {
            return context.Exception is ConfigurationException ||
                   context.Exception.Message.Contains("configuration", StringComparison.OrdinalIgnoreCase) ||
                   context.Exception.Message.Contains("settings", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ExceptionHandlingResult> HandleAsync(ExceptionProcessingContext context)
        {
            var result = new ExceptionHandlingResult
            {
                WasHandled = true,
                RecoveryActions = new List<RecoveryAction>()
            };

            result.SuggestedUserAction = "設定を確認し、必要に応じてデフォルト値にリセットしてください。";

            result.RecoveryActions.Add(new RecoveryAction
            {
                ActionType = "ResetToDefaults",
                Description = "デフォルト設定への復元",
                Priority = 1
            });

            result.RecoveryActions.Add(new RecoveryAction
            {
                ActionType = "ValidateConfiguration",
                Description = "設定の検証",
                Priority = 2
            });

            return result;
        }
    }

    /// <summary>
    /// リソース例外ハンドラー
    /// </summary>
    public class ResourceExceptionHandler : IExceptionHandler
    {
        public int Priority => 40;

        public async Task<bool> CanHandleAsync(ExceptionProcessingContext context)
        {
            return context.Exception is ResourceException ||
                   context.Exception is OutOfMemoryException ||
                   context.Exception is UnauthorizedAccessException ||
                   context.Exception.Message.Contains("access", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ExceptionHandlingResult> HandleAsync(ExceptionProcessingContext context)
        {
            var result = new ExceptionHandlingResult
            {
                WasHandled = true,
                RecoveryActions = new List<RecoveryAction>()
            };

            if (context.Exception is OutOfMemoryException)
            {
                result.SuggestedUserAction = "メモリ不足が発生しました。他のアプリケーションを終了してから再試行してください。";
                
                result.RecoveryActions.Add(new RecoveryAction
                {
                    ActionType = "ForceGarbageCollection",
                    Description = "ガベージコレクションの実行",
                    Priority = 1,
                    ExecuteAsync = async () =>
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        return true;
                    }
                });
            }
            else if (context.Exception is UnauthorizedAccessException)
            {
                result.SuggestedUserAction = "アクセス権限がありません。管理者として実行するか、管理者に権限を確認してください。";
                
                result.RecoveryActions.Add(new RecoveryAction
                {
                    ActionType = "CheckPermissions",
                    Description = "権限の確認",
                    Priority = 1
                });
            }
            else
            {
                result.SuggestedUserAction = "リソースアクセスに問題が発生しました。システム状態を確認してください。";
            }

            return result;
        }
    }

    /// <summary>
    /// 汎用例外ハンドラー - フォールバック
    /// </summary>
    public class GenericExceptionHandler : IExceptionHandler
    {
        public int Priority => 1000; // 最低優先度

        public async Task<bool> CanHandleAsync(ExceptionProcessingContext context)
        {
            return true; // 全ての例外を処理可能
        }

        public async Task<ExceptionHandlingResult> HandleAsync(ExceptionProcessingContext context)
        {
            var result = new ExceptionHandlingResult
            {
                WasHandled = true,
                RecoveryActions = new List<RecoveryAction>()
            };

            result.SuggestedUserAction = context.Exception switch
            {
                TimeoutException => "操作がタイムアウトしました。しばらく時間をおいてから再試行してください。",
                ArgumentException => "入力値に問題があります。値を確認して再試行してください。",
                InvalidOperationException => "現在の状態では操作を実行できません。状態を確認してください。",
                _ => "予期しないエラーが発生しました。操作を再試行してください。問題が継続する場合はサポートに連絡してください。"
            };

            // 基本的な復旧アクション
            result.RecoveryActions.Add(new RecoveryAction
            {
                ActionType = "Retry",
                Description = "操作の再試行",
                Priority = 1
            });

            result.RecoveryActions.Add(new RecoveryAction
            {
                ActionType = "LogError",
                Description = "エラーログの記録",
                Priority = 2
            });

            return result;
        }
    }
}