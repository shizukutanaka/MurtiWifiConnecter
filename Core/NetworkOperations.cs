using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// クロスプラットフォームネットワーク操作クラス
    /// ネットワーク操作の統一インターフェースを提供
    /// </summary>
    public static class NetworkOperations
    {
        private static readonly SemaphoreSlim _operationSemaphore = new SemaphoreSlim(1, 1);
        private static readonly Dictionary<string, DateTime> _lastOperationTimes = new Dictionary<string, DateTime>();
        private static readonly TimeSpan _minOperationInterval = TimeSpan.FromMilliseconds(50); // Reduced from 100ms

        /// <summary>
        /// 利用可能なWi-Fiネットワークをスキャンします
        /// </summary>
        /// <param name="forceRefresh">キャッシュを無視して強制的にスキャンするか</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>ネットワーク情報リスト</returns>
        public static async Task<List<NetworkInfo>> ScanNetworksAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            var operationKey = $"ScanNetworks_{forceRefresh}";
            await EnforceOperationRateLimit(operationKey, cancellationToken);

            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await CrossPlatformNetworkManager.Current.ScanNetworksAsync(forceRefresh, cancellationToken);
                stopwatch.Stop();

                await Logger.LogInfo($"Network scan completed", nameof(NetworkOperations),
                    new Dictionary<string, object>
                    {
                        ["networks_found"] = result?.Count ?? 0,
                        ["force_refresh"] = forceRefresh,
                        ["duration_ms"] = stopwatch.ElapsedMilliseconds
                    });

                return result;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Network scan failed: {ex.Message}", nameof(NetworkOperations), null, ex);
                throw;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// 指定されたWi-Fiネットワークに接続します
        /// </summary>
        /// <param name="ssid">接続するネットワークのSSID</param>
        /// <param name="password">パスワード（該当する場合）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>接続成功かどうか</returns>
        public static async Task<bool> ConnectAsync(string ssid, string password = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                throw new ArgumentException("SSID cannot be null or empty", nameof(ssid));

            var operationKey = $"Connect_{ssid}";
            await EnforceOperationRateLimit(operationKey, cancellationToken);

            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await CrossPlatformNetworkManager.Current.ConnectAsync(ssid, password, cancellationToken);
                stopwatch.Stop();

                await Logger.LogInfo($"Network connection attempt", nameof(NetworkOperations),
                    new Dictionary<string, object>
                    {
                        ["ssid"] = ssid,
                        ["has_password"] = !string.IsNullOrEmpty(password),
                        ["success"] = result,
                        ["duration_ms"] = stopwatch.ElapsedMilliseconds
                    });

                return result;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Network connection failed: {ex.Message}", nameof(NetworkOperations),
                    new Dictionary<string, object> { ["ssid"] = ssid }, ex);
                throw;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// 現在のWi-Fi接続を切断します
        /// </summary>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>切断成功かどうか</returns>
        public static async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            var operationKey = "Disconnect";
            await EnforceOperationRateLimit(operationKey, cancellationToken);

            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await CrossPlatformNetworkManager.Current.DisconnectAsync(cancellationToken);
                stopwatch.Stop();

                await Logger.LogInfo($"Network disconnection completed", nameof(NetworkOperations),
                    new Dictionary<string, object>
                    {
                        ["success"] = result,
                        ["duration_ms"] = stopwatch.ElapsedMilliseconds
                    });

                return result;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Network disconnection failed: {ex.Message}", nameof(NetworkOperations), null, ex);
                throw;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// 現在の接続状態を取得します
        /// </summary>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>接続状態情報</returns>
        public static async Task<ConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            var operationKey = "GetStatus";
            await EnforceOperationRateLimit(operationKey, cancellationToken);

            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await CrossPlatformNetworkManager.Current.GetStatusAsync(cancellationToken);
                stopwatch.Stop();

                await Logger.LogInfo($"Status check completed", nameof(NetworkOperations),
                    new Dictionary<string, object>
                    {
                        ["is_connected"] = result?.IsConnected ?? false,
                        ["duration_ms"] = stopwatch.ElapsedMilliseconds
                    });

                return result;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Status check failed: {ex.Message}", nameof(NetworkOperations), null, ex);
                throw;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// 保存されたWi-Fiプロファイルの一覧を取得します
        /// </summary>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>プロファイル名リスト</returns>
        public static async Task<List<string>> GetSavedProfilesAsync(CancellationToken cancellationToken = default)
        {
            var operationKey = "GetSavedProfiles";
            await EnforceOperationRateLimit(operationKey, cancellationToken);

            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await CrossPlatformNetworkManager.Current.GetSavedProfilesAsync(cancellationToken);
                stopwatch.Stop();

                await Logger.LogInfo($"Saved profiles retrieved", nameof(NetworkOperations),
                    new Dictionary<string, object>
                    {
                        ["profiles_count"] = result?.Count ?? 0,
                        ["duration_ms"] = stopwatch.ElapsedMilliseconds
                    });

                return result;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Profile retrieval failed: {ex.Message}", nameof(NetworkOperations), null, ex);
                throw;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// 操作レート制限を適用します
        /// </summary>
        private static async Task EnforceOperationRateLimit(string operationKey, CancellationToken cancellationToken)
        {
            lock (_lastOperationTimes)
            {
                if (_lastOperationTimes.TryGetValue(operationKey, out var lastTime))
                {
                    var timeSinceLast = DateTime.UtcNow - lastTime;
                    if (timeSinceLast < _minOperationInterval)
                    {
                        var delay = _minOperationInterval - timeSinceLast;
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                _lastOperationTimes[operationKey] = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 指定されたWi-Fiプロファイルを削除します
        /// </summary>
        public static async Task<bool> DeleteProfileAsync(string ssid, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                throw new ArgumentException("SSID cannot be null or empty", nameof(ssid));

            var operationKey = $"DeleteProfile_{ssid}";
            await EnforceOperationRateLimit(operationKey, cancellationToken);

            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await CrossPlatformNetworkManager.Current.DeleteProfileAsync(ssid, cancellationToken);
                stopwatch.Stop();

                await Logger.LogInfo($"Profile deletion completed", nameof(NetworkOperations),
                    new Dictionary<string, object>
                    {
                        ["ssid"] = ssid,
                        ["success"] = result,
                        ["duration_ms"] = stopwatch.ElapsedMilliseconds
                    });

                return result;
            }
            catch (Exception ex)
            {
                await Logger.LogError($"Profile deletion failed: {ex.Message}", nameof(NetworkOperations),
                    new Dictionary<string, object> { ["ssid"] = ssid }, ex);
                throw;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// 検出されたプラットフォームを取得します
        /// </summary>
        public static PlatformType DetectedPlatform => CrossPlatformNetworkManager.DetectedPlatform;
    }
}