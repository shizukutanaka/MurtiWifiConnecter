using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// ブロックチェーンセキュリティログマネージャー
    /// </summary>
    public class BlockchainSecurityLogManager
    {
        private readonly ILogger<BlockchainSecurityLogManager> _logger;
        private readonly List<BlockchainLogEntry> _logEntries;
        private string _lastHash;

        public BlockchainSecurityLogManager(ILogger<BlockchainSecurityLogManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logEntries = new List<BlockchainLogEntry>();
            _lastHash = "Genesis"; // 初期ブロック
        }

        /// <summary>
        /// セキュリティイベントをブロックチェーンに記録
        /// </summary>
        public async Task<string> LogSecurityEventAsync(string eventType, string description, Dictionary<string, object>? metadata = null)
        {
            try
            {
                var entry = new BlockchainLogEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    EventType = eventType,
                    Description = description,
                    Timestamp = DateTime.UtcNow,
                    Metadata = metadata ?? new Dictionary<string, object>(),
                    PreviousHash = _lastHash,
                    Hash = await ComputeHashAsync(eventType, description, _lastHash)
                };

                _logEntries.Add(entry);
                _lastHash = entry.Hash;

                await _logger.LogInformation($"ブロックチェーンセキュリティログを記録しました: {entry.Id}");

                return entry.Hash;
            }
            catch (Exception ex)
            {
                await _logger.LogError($"ブロックチェーンセキュリティログ記録に失敗しました: {ex.Message}", ex);
                return "";
            }
        }

        /// <summary>
        /// ログの整合性を検証
        /// </summary>
        public bool VerifyLogIntegrity()
        {
            try
            {
                for (int i = 1; i < _logEntries.Count; i++)
                {
                    var current = _logEntries[i];
                    var previous = _logEntries[i - 1];

                    if (current.PreviousHash != previous.Hash)
                    {
                        _logger.LogWarning($"ログ整合性エラー検知: インデックス {i}");
                        return false;
                    }

                    // ハッシュ値の再計算と比較
                    var computedHash = ComputeHashSync(current.EventType, current.Description, current.PreviousHash);
                    if (computedHash != current.Hash)
                    {
                        _logger.LogWarning($"ハッシュ不一致検知: インデックス {i}");
                        return false;
                    }
                }

                _logger.LogInformation("ログ整合性検証完了: すべてのエントリが有効");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"ログ整合性検証に失敗しました: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// ログエントリを取得
        /// </summary>
        public IReadOnlyList<BlockchainLogEntry> GetLogEntries()
        {
            return _logEntries.AsReadOnly();
        }

        /// <summary>
        /// 指定された期間のログエントリを取得
        /// </summary>
        public IReadOnlyList<BlockchainLogEntry> GetLogEntries(DateTime startTime, DateTime endTime)
        {
            return _logEntries.Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime).ToList().AsReadOnly();
        }

        /// <summary>
        /// ログエントリを検索
        /// </summary>
        public IReadOnlyList<BlockchainLogEntry> SearchLogEntries(string searchTerm)
        {
            return _logEntries.Where(e =>
                e.EventType.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.Metadata.Any(m => m.Value.ToString()?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
            ).ToList().AsReadOnly();
        }

        private async Task<string> ComputeHashAsync(string eventType, string description, string previousHash)
        {
            await Task.Delay(10); // 計算時間をシミュレート
            return ComputeHashSync(eventType, description, previousHash);
        }

        private string ComputeHashSync(string eventType, string description, string previousHash)
        {
            var input = $"{eventType}|{description}|{previousHash}|{DateTime.UtcNow.Ticks}";
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hashBytes);
        }
    }

    /// <summary>
    /// ブロックチェーンセキュリティログエントリ
    /// </summary>
    public class BlockchainLogEntry
    {
        public string Id { get; set; } = "";
        public string EventType { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public string Hash { get; set; } = "";
        public string PreviousHash { get; set; } = "";
        public int Index { get; set; }
    }
}
