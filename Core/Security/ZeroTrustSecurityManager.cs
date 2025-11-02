using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core.Security
{
    /// <summary>
    /// ゼロトラストセキュリティマネージャー
    /// 継続的な認証と検証を実装
    /// </summary>
    public class ZeroTrustSecurityManager
    {
        private readonly Dictionary<string, DeviceTrustScore> _deviceTrustScores;
        private readonly Dictionary<string, SessionContext> _activeSessions;
        private readonly object _trustLock = new();
        private readonly object _sessionLock = new();

        // 信頼スコアの閾値設定
        private const double MinimumTrustScore = 0.7;
        private const double TrustDecayRate = 0.05; // 1時間あたり5%低下
        private const int SessionTimeoutMinutes = 30;

        public ZeroTrustSecurityManager()
        {
            _deviceTrustScores = new Dictionary<string, DeviceTrustScore>();
            _activeSessions = new Dictionary<string, SessionContext>();
        }

        /// <summary>
        /// デバイスを認証して信頼スコアを評価
        /// </summary>
        public async Task<DeviceAuthenticationResult> AuthenticateDeviceAsync(string deviceId, DeviceContext deviceContext)
        {
            try
            {
                var trustScore = await CalculateDeviceTrustScoreAsync(deviceId, deviceContext);
                var isTrusted = trustScore.Score >= MinimumTrustScore;

                if (isTrusted)
                {
                    await UpdateDeviceTrustScoreAsync(deviceId, trustScore);
                    var sessionId = Guid.NewGuid().ToString();
                    var session = new SessionContext
                    {
                        SessionId = sessionId,
                        DeviceId = deviceId,
                        StartedAt = DateTime.UtcNow,
                        LastActivity = DateTime.UtcNow,
                        TrustScore = trustScore.Score,
                        AccessLevel = trustScore.RecommendedAccessLevel
                    };

                    lock (_sessionLock)
                    {
                        _activeSessions[sessionId] = session;
                    }

                    return new DeviceAuthenticationResult
                    {
                        IsAuthenticated = true,
                        SessionId = sessionId,
                        TrustScore = trustScore.Score,
                        AccessLevel = session.AccessLevel,
                        ExpiresAt = DateTime.UtcNow.AddMinutes(SessionTimeoutMinutes),
                        SecurityPolicies = GenerateSecurityPolicies(session.AccessLevel)
                    };
                }
                else
                {
                    await Logger.LogSecurity("デバイス認証拒否", "DeviceAuthenticationFailed",
                        new Dictionary<string, object>
                        {
                            ["deviceId"] = deviceId,
                            ["trustScore"] = trustScore.Score,
                            ["reason"] = trustScore.Reasons.FirstOrDefault()
                        });

                    return new DeviceAuthenticationResult
                    {
                        IsAuthenticated = false,
                        TrustScore = trustScore.Score,
                        DenialReasons = trustScore.Reasons,
                        RecommendedActions = trustScore.RecommendedActions
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("デバイス認証に失敗しました", nameof(ZeroTrustSecurityManager), null, ex);
                return new DeviceAuthenticationResult { IsAuthenticated = false };
            }
        }

        /// <summary>
        /// セッションを検証して継続的なアクセスを許可
        /// </summary>
        public async Task<SessionValidationResult> ValidateSessionAsync(string sessionId)
        {
            try
            {
                SessionContext session;
                lock (_sessionLock)
                {
                    if (!_activeSessions.TryGetValue(sessionId, out session))
                    {
                        return new SessionValidationResult
                        {
                            IsValid = false,
                            Reason = "セッションが見つかりません"
                        };
                    }
                }

                // セッションタイムアウトチェック
                if (DateTime.UtcNow - session.LastActivity > TimeSpan.FromMinutes(SessionTimeoutMinutes))
                {
                    await TerminateSessionAsync(sessionId);
                    return new SessionValidationResult
                    {
                        IsValid = false,
                        Reason = "セッションがタイムアウトしました"
                    };
                }

                // 信頼スコアの再評価
                var currentTrustScore = await GetCurrentDeviceTrustScoreAsync(session.DeviceId);
                if (currentTrustScore < MinimumTrustScore)
                {
                    await TerminateSessionAsync(sessionId);
                    return new SessionValidationResult
                    {
                        IsValid = false,
                        Reason = "信頼スコアが低下しました",
                        TrustScore = currentTrustScore
                    };
                }

                // セッションを更新
                session.LastActivity = DateTime.UtcNow;
                session.TrustScore = currentTrustScore;

                return new SessionValidationResult
                {
                    IsValid = true,
                    SessionId = sessionId,
                    TrustScore = currentTrustScore,
                    AccessLevel = session.AccessLevel,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(SessionTimeoutMinutes)
                };
            }
            catch (Exception ex)
            {
                Logger.LogError("セッション検証に失敗しました", nameof(ZeroTrustSecurityManager), null, ex);
                return new SessionValidationResult { IsValid = false };
            }
        }

        /// <summary>
        /// デバイス信頼スコアを計算
        /// </summary>
        private async Task<DeviceTrustScore> CalculateDeviceTrustScoreAsync(string deviceId, DeviceContext deviceContext)
        {
            var factors = new List<TrustFactor>();
            var reasons = new List<string>();
            var actions = new List<string>();

            // デバイス認証状態チェック
            if (deviceContext.IsAuthenticated)
            {
                factors.Add(new TrustFactor("認証状態", 0.3, 1.0));
            }
            else
            {
                factors.Add(new TrustFactor("認証状態", 0.3, 0.0));
                reasons.Add("デバイスが認証されていません");
                actions.Add("デバイス認証を実行してください");
            }

            // セキュリティパッチレベルチェック
            if (deviceContext.SecurityPatchLevel > DateTime.UtcNow.AddDays(-30))
            {
                factors.Add(new TrustFactor("セキュリティパッチ", 0.2, 1.0));
            }
            else if (deviceContext.SecurityPatchLevel > DateTime.UtcNow.AddDays(-90))
            {
                factors.Add(new TrustFactor("セキュリティパッチ", 0.2, 0.7));
                reasons.Add("セキュリティパッチが古いです");
                actions.Add("セキュリティパッチを更新してください");
            }
            else
            {
                factors.Add(new TrustFactor("セキュリティパッチ", 0.2, 0.3));
                reasons.Add("セキュリティパッチが非常に古いです");
                actions.Add("直ちにセキュリティパッチを更新してください");
            }

            // ネットワーク位置チェック
            if (await IsTrustedNetworkLocationAsync(deviceContext.NetworkInfo))
            {
                factors.Add(new TrustFactor("ネットワーク位置", 0.2, 1.0));
            }
            else
            {
                factors.Add(new TrustFactor("ネットワーク位置", 0.2, 0.5));
                reasons.Add("信頼できないネットワーク位置です");
                actions.Add("信頼できるネットワークに接続してください");
            }

            // 過去の行動パターンチェック
            var behaviorScore = await AnalyzeDeviceBehaviorAsync(deviceId, deviceContext);
            factors.Add(new TrustFactor("行動パターン", 0.3, behaviorScore));

            if (behaviorScore < 0.7)
            {
                reasons.Add("異常な行動パターンが検出されました");
                actions.Add("行動パターンを確認してください");
            }

            // 総合スコアを計算
            var totalScore = factors.Sum(f => f.Weight * f.Score);

            return new DeviceTrustScore
            {
                Score = totalScore,
                Factors = factors,
                Reasons = reasons,
                RecommendedActions = actions,
                RecommendedAccessLevel = CalculateAccessLevel(totalScore)
            };
        }

        /// <summary>
        /// デバイス行動を分析
        /// </summary>
        private async Task<double> AnalyzeDeviceBehaviorAsync(string deviceId, DeviceContext deviceContext)
        {
            // 簡易的な実装 - 実際には機械学習モデルを使用
            var baseScore = 0.8;

            // 異常な接続試行のチェック
            if (deviceContext.FailedConnectionAttempts > 5)
            {
                baseScore -= 0.3;
            }

            // 異常な時間帯のチェック
            if (deviceContext.LastActivity.Hour < 6 || deviceContext.LastActivity.Hour > 22)
            {
                baseScore -= 0.1;
            }

            // 位置情報の異常チェック
            if (deviceContext.LocationChangeCount > 3) // 短時間に複数の場所から接続
            {
                baseScore -= 0.2;
            }

            return Math.Max(0, Math.Min(1, baseScore));
        }

        /// <summary>
        /// 信頼できるネットワーク位置かチェック
        /// </summary>
        private async Task<bool> IsTrustedNetworkLocationAsync(NetworkLocationInfo networkInfo)
        {
            // 簡易的な実装 - 実際にはより詳細なチェックを実装
            var trustedSubnets = new[] { "192.168.1.0/24", "10.0.0.0/8", "172.16.0.0/12" };

            foreach (var subnet in trustedSubnets)
            {
                if (IsInSubnet(networkInfo.IPAddress, subnet))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// サブネットチェック
        /// </summary>
        private bool IsInSubnet(string ipAddress, string subnet)
        {
            // 簡易的な実装 - 実際にはより正確な実装が必要
            var ipParts = ipAddress.Split('.');
            var subnetParts = subnet.Split('/')[0].Split('.');

            if (ipParts.Length != 4 || subnetParts.Length != 4)
                return false;

            for (int i = 0; i < 3; i++) // 最後のオクテットはチェックしない
            {
                if (ipParts[i] != subnetParts[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// アクセスレベルを計算
        /// </summary>
        private AccessLevel CalculateAccessLevel(double trustScore)
        {
            if (trustScore >= 0.9) return AccessLevel.Full;
            if (trustScore >= 0.8) return AccessLevel.High;
            if (trustScore >= 0.7) return AccessLevel.Medium;
            if (trustScore >= 0.5) return AccessLevel.Limited;
            return AccessLevel.None;
        }

        /// <summary>
        /// セキュリティポリシーを生成
        /// </summary>
        private List<string> GenerateSecurityPolicies(AccessLevel accessLevel)
        {
            var policies = new List<string>();

            switch (accessLevel)
            {
                case AccessLevel.Full:
                    policies.Add("すべてのネットワークリソースにアクセス可能");
                    policies.Add("無制限の帯域幅使用可能");
                    break;
                case AccessLevel.High:
                    policies.Add("重要なネットワークリソースにアクセス可能");
                    policies.Add("制限付きの帯域幅使用可能");
                    break;
                case AccessLevel.Medium:
                    policies.Add("基本的なネットワークリソースのみアクセス可能");
                    policies.Add("帯域幅制限あり");
                    break;
                case AccessLevel.Limited:
                    policies.Add("最小限のネットワークアクセス");
                    policies.Add("厳格な帯域幅制限");
                    break;
            }

            return policies;
        }

        /// <summary>
        /// デバイス信頼スコアを更新
        /// </summary>
        private async Task UpdateDeviceTrustScoreAsync(string deviceId, DeviceTrustScore trustScore)
        {
            lock (_trustLock)
            {
                _deviceTrustScores[deviceId] = trustScore;
            }
        }

        /// <summary>
        /// 現在のデバイス信頼スコアを取得
        /// </summary>
        private async Task<double> GetCurrentDeviceTrustScoreAsync(string deviceId)
        {
            lock (_trustLock)
            {
                if (_deviceTrustScores.TryGetValue(deviceId, out var trustScore))
                {
                    // 信頼スコアの経時劣化を適用
                    var timeSinceLastUpdate = DateTime.UtcNow - trustScore.LastUpdated;
                    var decayAmount = TrustDecayRate * timeSinceLastUpdate.TotalHours;
                    return Math.Max(0, trustScore.Score - decayAmount);
                }

                return 0.0;
            }
        }

        /// <summary>
        /// セッションを終了
        /// </summary>
        private async Task TerminateSessionAsync(string sessionId)
        {
            lock (_sessionLock)
            {
                _activeSessions.Remove(sessionId);
            }

            await Logger.LogInfo("セッションを終了しました", nameof(ZeroTrustSecurityManager),
                new Dictionary<string, object> { ["sessionId"] = sessionId });
        }

        /// <summary>
        /// 定期的な信頼スコアの更新とメンテナンス
        /// </summary>
        public async Task PerformMaintenanceAsync()
        {
            try
            {
                var expiredSessions = new List<string>();

                lock (_sessionLock)
                {
                    foreach (var session in _activeSessions)
                    {
                        if (DateTime.UtcNow - session.Value.LastActivity > TimeSpan.FromMinutes(SessionTimeoutMinutes))
                        {
                            expiredSessions.Add(session.Key);
                        }
                    }

                    foreach (var sessionId in expiredSessions)
                    {
                        _activeSessions.Remove(sessionId);
                    }
                }

                if (expiredSessions.Any())
                {
                    await Logger.LogInfo("期限切れセッションをクリーンアップしました",
                        nameof(ZeroTrustSecurityManager),
                        new Dictionary<string, object> { ["expiredCount"] = expiredSessions.Count });
                }

                // 信頼スコアのクリーンアップ
                var expiredDevices = new List<string>();
                lock (_trustLock)
                {
                    foreach (var device in _deviceTrustScores)
                    {
                        if (DateTime.UtcNow - device.Value.LastUpdated > TimeSpan.FromDays(7))
                        {
                            expiredDevices.Add(device.Key);
                        }
                    }

                    foreach (var deviceId in expiredDevices)
                    {
                        _deviceTrustScores.Remove(deviceId);
                    }
                }

                if (expiredDevices.Any())
                {
                    await Logger.LogInfo("古いデバイス信頼スコアをクリーンアップしました",
                        nameof(ZeroTrustSecurityManager),
                        new Dictionary<string, object> { ["expiredCount"] = expiredDevices.Count });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ゼロトラストメンテナンスに失敗しました", nameof(ZeroTrustSecurityManager), null, ex);
            }
        }
    }

    // データ構造定義
    public class DeviceContext
    {
        public bool IsAuthenticated { get; set; }
        public DateTime SecurityPatchLevel { get; set; }
        public NetworkLocationInfo NetworkInfo { get; set; }
        public int FailedConnectionAttempts { get; set; }
        public DateTime LastActivity { get; set; }
        public int LocationChangeCount { get; set; }
    }

    public class NetworkLocationInfo
    {
        public string IPAddress { get; set; }
        public string Subnet { get; set; }
        public string Gateway { get; set; }
    }

    public class DeviceAuthenticationResult
    {
        public bool IsAuthenticated { get; set; }
        public string SessionId { get; set; }
        public double TrustScore { get; set; }
        public AccessLevel AccessLevel { get; set; }
        public DateTime ExpiresAt { get; set; }
        public List<string> SecurityPolicies { get; set; } = new();
        public List<string> DenialReasons { get; set; } = new();
        public List<string> RecommendedActions { get; set; } = new();
    }

    public class SessionValidationResult
    {
        public bool IsValid { get; set; }
        public string SessionId { get; set; }
        public string Reason { get; set; }
        public double TrustScore { get; set; }
        public AccessLevel AccessLevel { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class SessionContext
    {
        public string SessionId { get; set; }
        public string DeviceId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public double TrustScore { get; set; }
        public AccessLevel AccessLevel { get; set; }
    }

    public class DeviceTrustScore
    {
        public double Score { get; set; }
        public List<TrustFactor> Factors { get; set; } = new();
        public List<string> Reasons { get; set; } = new();
        public List<string> RecommendedActions { get; set; } = new();
        public AccessLevel RecommendedAccessLevel { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    public class TrustFactor
    {
        public string Name { get; set; }
        public double Weight { get; set; }
        public double Score { get; set; }

        public TrustFactor(string name, double weight, double score)
        {
            Name = name;
            Weight = weight;
            Score = score;
        }
    }

    public enum AccessLevel
    {
        None,
        Limited,
        Medium,
        High,
        Full
    }
}
