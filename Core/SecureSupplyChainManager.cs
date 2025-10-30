using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// セキュアサプライチェーンマネージャー
    /// </summary>
    public class SecureSupplyChainManager
    {
        private readonly ILogger<SecureSupplyChainManager> _logger;
        private readonly Dictionary<string, SoftwareBillOfMaterials> _sboms;
        private readonly List<ComponentVerification> _verifications;

        public SecureSupplyChainManager(ILogger<SecureSupplyChainManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sboms = new Dictionary<string, SoftwareBillOfMaterials>();
            _verifications = new List<ComponentVerification>();
        }

        /// <summary>
        /// ソフトウェア部品表(SBOM)を登録
        /// </summary>
        public async Task<bool> RegisterSBOMAsync(string productId, SoftwareBillOfMaterials sbom)
        {
            try
            {
                sbom.Id = Guid.NewGuid().ToString();
                sbom.CreatedAt = DateTime.UtcNow;
                sbom.LastVerified = DateTime.UtcNow;

                _sboms[productId] = sbom;

                await _logger.LogInformation($"SBOMを登録しました: {productId}");

                return true;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"SBOM登録に失敗しました: {productId} - {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// コンポーネントを検証
        /// </summary>
        public async Task<bool> VerifyComponentAsync(string productId, string componentName, string expectedHash)
        {
            try
            {
                if (!_sboms.TryGetValue(productId, out var sbom))
                    return false;

                var component = sbom.Components.FirstOrDefault(c => c.Name == componentName);
                if (component == null)
                    return false;

                var verification = new ComponentVerification
                {
                    Id = Guid.NewGuid().ToString(),
                    ProductId = productId,
                    ComponentName = componentName,
                    ExpectedHash = expectedHash,
                    ActualHash = await ComputeComponentHashAsync(component),
                    IsVerified = expectedHash == await ComputeComponentHashAsync(component),
                    VerifiedAt = DateTime.UtcNow
                };

                _verifications.Add(verification);

                if (verification.IsVerified)
                {
                    await _logger.LogInformation($"コンポーネント検証に成功しました: {componentName}");
                }
                else
                {
                    await _logger.LogWarning($"コンポーネント検証に失敗しました: {componentName}");
                }

                return verification.IsVerified;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"コンポーネント検証に失敗しました: {componentName} - {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// サプライチェーン攻撃を検知
        /// </summary>
        public async Task<List<SupplyChainThreat>> DetectSupplyChainThreatsAsync()
        {
            var threats = new List<SupplyChainThreat>();

            try
            {
                foreach (var verification in _verifications.Where(v => !v.IsVerified))
                {
                    threats.Add(new SupplyChainThreat
                    {
                        Id = Guid.NewGuid().ToString(),
                        Type = SupplyChainThreatType.ComponentTampering,
                        ComponentName = verification.ComponentName,
                        ProductId = verification.ProductId,
                        Severity = ThreatSeverity.High,
                        DetectedAt = DateTime.UtcNow,
                        Description = $"コンポーネント '{verification.ComponentName}' のハッシュが一致しません"
                    });
                }

                await _logger.LogInformation($"サプライチェーン脅威を検知しました: {threats.Count}件");

                return threats;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"サプライチェーン脅威検知に失敗しました: {ex.Message}", ex);
                return threats;
            }
        }

        private async Task<string> ComputeComponentHashAsync(ComponentInfo component)
        {
            // コンポーネントハッシュ計算シミュレーション
            await Task.Delay(50);
            return $"hash_{component.Name}_{component.Version}".GetHashCode().ToString();
        }
    }

    /// <summary>
    /// ソフトウェア部品表
    /// </summary>
    public class SoftwareBillOfMaterials
    {
        public string Id { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Version { get; set; } = "";
        public List<ComponentInfo> Components { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime LastVerified { get; set; }
        public SBOMFormat Format { get; set; } = SBOMFormat.CycloneDX;
    }

    /// <summary>
    /// コンポーネント情報
    /// </summary>
    public class ComponentInfo
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string License { get; set; } = "";
        public string Supplier { get; set; } = "";
        public ComponentType Type { get; set; } = ComponentType.Library;
        public Dictionary<string, string> Hashes { get; set; } = new();
    }

    /// <summary>
    /// コンポーネントタイプ
    /// </summary>
    public enum ComponentType
    {
        Library,
        Framework,
        Application,
        Container
    }

    /// <summary>
    /// SBOMフォーマット
    /// </summary>
    public enum SBOMFormat
    {
        CycloneDX,
        SPDX
    }

    /// <summary>
    /// コンポーネント検証結果
    /// </summary>
    public class ComponentVerification
    {
        public string Id { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ComponentName { get; set; } = "";
        public string ExpectedHash { get; set; } = "";
        public string ActualHash { get; set; } = "";
        public bool IsVerified { get; set; }
        public DateTime VerifiedAt { get; set; }
    }

    /// <summary>
    /// サプライチェーン脅威
    /// </summary>
    public class SupplyChainThreat
    {
        public string Id { get; set; } = "";
        public SupplyChainThreatType Type { get; set; }
        public string ComponentName { get; set; } = "";
        public string ProductId { get; set; } = "";
        public ThreatSeverity Severity { get; set; }
        public DateTime DetectedAt { get; set; }
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// サプライチェーン脅威タイプ
    /// </summary>
    public enum SupplyChainThreatType
    {
        ComponentTampering,
        DependencyHijacking,
        MaliciousUpdate,
        SupplierCompromise
    }
}
