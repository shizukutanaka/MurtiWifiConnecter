using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// エンドポイントセキュリティ統合マネージャー
    /// </summary>
    public class EndpointSecurityIntegrationManager
    {
        private readonly ILogger<EndpointSecurityIntegrationManager> _logger;
        private readonly Dictionary<string, EndpointDevice> _endpoints;
        private readonly List<SecurityPolicy> _globalPolicies;

        public EndpointSecurityIntegrationManager(ILogger<EndpointSecurityIntegrationManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _endpoints = new Dictionary<string, EndpointDevice>();
            _globalPolicies = new List<SecurityPolicy>();
        }

        /// <summary>
        /// エンドポイントデバイスを登録
        /// </summary>
        public async Task<bool> RegisterEndpointDeviceAsync(string deviceId, EndpointDeviceConfig config)
        {
            try
            {
                if (_endpoints.ContainsKey(deviceId))
                    throw new InvalidOperationException($"エンドポイントデバイス '{deviceId}' は既に登録されています");

                var device = new EndpointDevice
                {
                    Id = deviceId,
                    Config = config,
                    RegisteredAt = DateTime.UtcNow,
                    IsActive = true,
                    LastSeen = DateTime.UtcNow,
                    SecurityStatus = SecurityStatus.Healthy
                };

                _endpoints[deviceId] = device;

                await _logger.LogInformation($"エンドポイントデバイスを登録しました: {deviceId}");

                return true;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"エンドポイントデバイス登録に失敗しました: {deviceId} - {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// エンドポイントデバイスにセキュリティポリシーを適用
        /// </summary>
        public async Task<bool> ApplySecurityPoliciesToEndpointAsync(string deviceId, List<string> policyIds)
        {
            try
            {
                if (!_endpoints.TryGetValue(deviceId, out var device))
                    return false;

                var policies = _globalPolicies.Where(p => policyIds.Contains(p.Id)).ToList();

                device.AppliedPolicies = policies;
                device.SecurityStatus = SecurityStatus.Protected;

                await _logger.LogInformation($"エンドポイントデバイスにセキュリティポリシーを適用しました: {deviceId}");

                return true;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"セキュリティポリシー適用に失敗しました: {deviceId} - {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// エンドポイントデバイスのセキュリティ状態をチェック
        /// </summary>
        public async Task<EndpointSecurityStatus> CheckEndpointSecurityStatusAsync(string deviceId)
        {
            try
            {
                if (!_endpoints.TryGetValue(deviceId, out var device))
                    return new EndpointSecurityStatus { DeviceId = deviceId, Status = SecurityStatus.Unknown };

                // セキュリティチェックシミュレーション
                await Task.Delay(100);

                var status = new EndpointSecurityStatus
                {
                    DeviceId = deviceId,
                    Status = device.SecurityStatus,
                    LastChecked = DateTime.UtcNow,
                    Vulnerabilities = GenerateVulnerabilityReport(device),
                    ComplianceScore = CalculateComplianceScore(device)
                };

                await _logger.LogInformation($"エンドポイントセキュリティ状態をチェックしました: {deviceId}");

                return status;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"セキュリティ状態チェックに失敗しました: {deviceId} - {ex.Message}", ex);
                return new EndpointSecurityStatus { DeviceId = deviceId, Status = SecurityStatus.Error };
            }
        }

        /// <summary>
        /// グローバルセキュリティポリシーを追加
        /// </summary>
        public async Task<bool> AddGlobalSecurityPolicyAsync(SecurityPolicy policy)
        {
            try
            {
                policy.Id = Guid.NewGuid().ToString();
                policy.CreatedAt = DateTime.UtcNow;
                policy.IsActive = true;

                _globalPolicies.Add(policy);

                await _logger.LogInformation($"グローバルセキュリティポリシーを追加しました: {policy.Id}");

                return true;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"グローバルポリシー追加に失敗しました: {ex.Message}", ex);
                return false;
            }
        }

        private List<string> GenerateVulnerabilityReport(EndpointDevice device)
        {
            var vulnerabilities = new List<string>();

            if (!device.Config.EnableEncryption)
                vulnerabilities.Add("暗号化が無効");

            if (!device.Config.EnableFirewall)
                vulnerabilities.Add("ファイアウォールが無効");

            if (device.Config.OSVersion == "Legacy")
                vulnerabilities.Add("古いOSバージョン");

            return vulnerabilities;
        }

        private double CalculateComplianceScore(EndpointDevice device)
        {
            var score = 100.0;

            if (!device.Config.EnableEncryption) score -= 20;
            if (!device.Config.EnableFirewall) score -= 15;
            if (!device.Config.EnableAntivirus) score -= 25;
            if (device.Config.OSVersion == "Legacy") score -= 10;
            if (device.AppliedPolicies.Count == 0) score -= 30;

            return Math.Max(score, 0);
        }
    }

    /// <summary>
    /// エンドポイントデバイス情報
    /// </summary>
    public class EndpointDevice
    {
        public string Id { get; set; } = "";
        public EndpointDeviceConfig Config { get; set; } = new();
        public DateTime RegisteredAt { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastSeen { get; set; }
        public SecurityStatus SecurityStatus { get; set; }
        public List<SecurityPolicy> AppliedPolicies { get; set; } = new();
    }

    /// <summary>
    /// エンドポイントデバイス設定
    /// </summary>
    public class EndpointDeviceConfig
    {
        public string DeviceType { get; set; } = "";
        public string OSVersion { get; set; } = "";
        public string OSPlatform { get; set; } = "";
        public bool EnableEncryption { get; set; } = true;
        public bool EnableFirewall { get; set; } = true;
        public bool EnableAntivirus { get; set; } = true;
        public bool EnableAutoUpdate { get; set; } = true;
        public Dictionary<string, object> CustomSettings { get; set; } = new();
    }

    /// <summary>
    /// エンドポイントセキュリティ状態
    /// </summary>
    public class EndpointSecurityStatus
    {
        public string DeviceId { get; set; } = "";
        public SecurityStatus Status { get; set; }
        public DateTime LastChecked { get; set; }
        public List<string> Vulnerabilities { get; set; } = new();
        public double ComplianceScore { get; set; }
    }

    /// <summary>
    /// セキュリティ状態
    /// </summary>
    public enum SecurityStatus
    {
        Healthy,
        Protected,
        Vulnerable,
        Compromised,
        Unknown,
        Error
    }
}
