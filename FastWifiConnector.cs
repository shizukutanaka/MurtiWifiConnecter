using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Text;
using System.Collections.Concurrent;
using System.Linq;
using MurtiWifiConnecter.Services;
using MurtiWifiConnecter.Constants;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// 高速WiFi接続クラス（最適化版）
    /// </summary>
    public static class FastWifiConnector
    {
        private static readonly ConcurrentDictionary<string, DateTime> _lastConnectAttempts = new();
        private static readonly SemaphoreSlim _connectSemaphore = new(1, 1);
        private static DateTime _lastCleanup = DateTime.MinValue;
        
        /// <summary>
        /// WiFiに高速接続
        /// </summary>
        public static async Task<WifiConnectionResult> ConnectAsync(string ssid, string password, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return new WifiConnectionResult { Success = false, ErrorMessage = "SSIDが空です" };
                
            if (string.IsNullOrWhiteSpace(password))
                return new WifiConnectionResult { Success = false, ErrorMessage = "パスワードが空です" };
            
            // WiFi認証情報の基本検証
            if (!SecurityManager.ValidateWiFiCredentials(ssid, password))
                return new WifiConnectionResult { Success = false, ErrorMessage = "無効なSSIDまたはパスワード形式です" };
            
            // 短期間での重複接続を防止
            var lastAttempt = _lastConnectAttempts.GetValueOrDefault(ssid, DateTime.MinValue);
            if (DateTime.Now - lastAttempt < TimeSpan.FromSeconds(2))
                return new WifiConnectionResult { Success = false, ErrorMessage = "短期間での重複接続は防止されています" };
            
            _lastConnectAttempts[ssid] = DateTime.Now;
            
            // 定期的にメモリクリーンアップ
            if (DateTime.Now - _lastCleanup > TimeSpan.FromMinutes(10))
            {
                CleanupOldAttempts();
                _lastCleanup = DateTime.Now;
            }
            
            if (!await _connectSemaphore.WaitAsync(100, cancellationToken))
                return new WifiConnectionResult { Success = false, ErrorMessage = "他の接続処理が実行中です" };
                
            try
            {
                // リトライ機能付きで接続実行
                return await ErrorHandler.ExecuteWithRetryAsync(
                    "WiFiConnection",
                    async (ct) => await ConnectInternalAsync(ssid, password, ct),
                    customPolicy: new RetryPolicy
                    {
                        MaxAttempts = AppConstants.Wifi.MaxRetryAttempts,
                        InitialDelay = TimeSpan.FromMilliseconds(AppConstants.Wifi.BaseRetryDelayMs),
                        MaxDelay = TimeSpan.FromMilliseconds(AppConstants.Wifi.MaxRetryDelayMs),
                        ShouldRetry = ex => !(ex is ArgumentException || ex is UnauthorizedAccessException)
                    },
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new WifiConnectionResult { Success = false, ErrorMessage = "接続がキャンセルされました" };
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError($"FastWifiConnector.Connect_{ssid}", ex);
                return new WifiConnectionResult { Success = false, ErrorMessage = GetFriendlyErrorMessage(ex) };
            }
            finally
            {
                _connectSemaphore.Release();
            }
        }
        
        /// <summary>
        /// 内部接続処理（高速化）
        /// </summary>
        private static async Task<WifiConnectionResult> ConnectInternalAsync(string ssid, string password, CancellationToken cancellationToken)
        {
            var safeSsid = ssid.Replace("\"", "");
            
            // 安全な一時ファイル作成
            var xmlPath = Path.GetTempFileName();
            
            try
            {
                // 最適化されたプロファイル作成
                var profileXml = CreateOptimizedProfile(safeSsid, password);
                await File.WriteAllTextAsync(xmlPath, profileXml, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
                
                // プロファイル追加（最適化タイムアウト使用）
                var profileResult = await ExecuteNetshCommandOptimizedAsync(
                    $"wlan add profile filename=\"{xmlPath}\" user=current", 
                    "profile_add", 
                    cancellationToken).ConfigureAwait(false);
                
                if (!profileResult.Success)
                    return new WifiConnectionResult { Success = false, ErrorMessage = $"プロファイル追加失敗: {profileResult.ErrorMessage}" };
                
                // 短い遅延の後に接続実行
                await Task.Delay(AppConstants.Wifi.ConnectionDelayMs, cancellationToken).ConfigureAwait(false);
                
                var connectResult = await ExecuteNetshCommandOptimizedAsync(
                    $"wlan connect name=\"{safeSsid}\"", 
                    "wifi_connect", 
                    cancellationToken).ConfigureAwait(false);
                
                if (!connectResult.Success)
                    return new WifiConnectionResult { Success = false, ErrorMessage = $"接続失敗: {connectResult.ErrorMessage}" };
                    
                return new WifiConnectionResult { Success = true, Message = "接続成功", ConnectedSSID = safeSsid };
            }
            finally
            {
                // 一時ファイルを安全に削除
                try 
                { 
                    if (File.Exists(xmlPath)) 
                        SecurityManager.SecureDeleteFile(xmlPath); 
                } 
                catch (Exception ex)
                {
                    ErrorHandler.LogError("FastWifiConnector.SecureFileDelete", ex);
                }
            }
        }
        
        /// <summary>
        /// netshコマンド実行（従来版）
        /// </summary>
        private static async Task<WifiConnectionResult> ExecuteNetshCommandAsync(string arguments, CancellationToken cancellationToken)
        {
            var result = await NetworkUtils.ExecuteNetshCommandAsync(arguments, 10000, cancellationToken).ConfigureAwait(false);
            return new WifiConnectionResult 
            { 
                Success = result.Success, 
                ErrorMessage = result.ErrorMessage 
            };
        }
        
        /// <summary>
        /// netshコマンド実行（最適化版）
        /// </summary>
        private static async Task<WifiConnectionResult> ExecuteNetshCommandOptimizedAsync(
            string arguments, 
            string operationType, 
            CancellationToken cancellationToken)
        {
            var timeout = ConnectionTimeoutOptimizer.GetOptimalTimeout(operationType);
            var result = await NetworkUtils.ExecuteNetshCommandAsync(arguments, timeout, cancellationToken).ConfigureAwait(false);
            return new WifiConnectionResult 
            { 
                Success = result.Success, 
                ErrorMessage = result.ErrorMessage 
            };
        }
        
        /// <summary>
        /// 最適化されたWiFiプロファイル作成
        /// </summary>
        private static string CreateOptimizedProfile(string ssid, string password)
        {
            var escapedSsid = System.Security.SecurityElement.Escape(ssid);
            var escapedPassword = System.Security.SecurityElement.Escape(password);
            
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{escapedSsid}</name>
    <SSIDConfig>
        <SSID>
            <hex>{ConvertToHex(ssid)}</hex>
            <name>{escapedSsid}</name>
        </SSID>
        <nonBroadcast>false</nonBroadcast>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <autoSwitch>true</autoSwitch>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>true</protected>
                <keyMaterial>{escapedPassword}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";
        }
        
        /// <summary>
        /// SSID文字列をHEX形式に変換
        /// </summary>
        private static string ConvertToHex(string ssid)
        {
            var bytes = Encoding.UTF8.GetBytes(ssid);
            return Convert.ToHexString(bytes);
        }
        
        /// <summary>
        /// 高速切断
        /// </summary>
        public static async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            var result = await ExecuteNetshCommandOptimizedAsync("wlan disconnect", "disconnect", cancellationToken);
            return result.Success;
        }
        
        /// <summary>
        /// プロファイル削除
        /// </summary>
        public static async Task<bool> DeleteProfileAsync(string ssid, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return false;
            
            var safeSsid = ssid.Replace("\"", "");
            var result = await ExecuteNetshCommandOptimizedAsync(
                $"wlan delete profile name=\"{safeSsid}\"", 
                "profile_delete", 
                cancellationToken);
            return result.Success;
        }
        
        /// <summary>
        /// 現在接続中のSSIDを高速取得
        /// </summary>
        public static async Task<string?> GetCurrentConnectedSSIDAsync(CancellationToken cancellationToken = default)
        {
            var timeout = ConnectionTimeoutOptimizer.GetOptimalTimeout("current_ssid");
            var networkResult = await NetworkUtils.ExecuteNetshCommandAsync("wlan show interfaces", timeout, cancellationToken);
            
            if (!networkResult.Success || string.IsNullOrWhiteSpace(networkResult.Output))
                return null;
                
            // 高速文字列検索
            var lines = networkResult.Output.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("SSID") && trimmed.Contains(":"))
                {
                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex > 0 && colonIndex < trimmed.Length - 1)
                    {
                        return trimmed.Substring(colonIndex + 1).Trim();
                    }
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// 古い接続試行記録をクリーンアップ
        /// </summary>
        private static void CleanupOldAttempts()
        {
            var cutoffTime = DateTime.Now.AddMinutes(-30);
            var keysToRemove = _lastConnectAttempts
                .Where(pair => pair.Value < cutoffTime)
                .Select(pair => pair.Key)
                .Take(50) // 一度に最大50個まで削除
                .ToArray();
                
            foreach (var key in keysToRemove)
            {
                _lastConnectAttempts.TryRemove(key, out _);
            }
        }
        
        /// <summary>
        /// ユーザーフレンドリーなエラーメッセージを取得
        /// </summary>
        private static string GetFriendlyErrorMessage(Exception ex)
        {
            return ex switch
            {
                UnauthorizedAccessException => "管理者権限が必要です",
                TimeoutException => "接続がタイムアウトしました",
                IOException => "ネットワーク設定ファイルの操作に失敗しました",
                _ => $"接続エラー: {ex.Message}"
            };
        }
        
        /// <summary>
        /// キャッシュされたネットワーク数を取得（統計用）
        /// </summary>
        public static int GetCachedNetworkCount() => _lastConnectAttempts.Count;
        
        /// <summary>
        /// キャッシュをクリア
        /// </summary>
        public static void ClearCache() => _lastConnectAttempts.Clear();
    }
    
    /// <summary>
    /// WiFi接続結果
    /// </summary>
    public class WifiConnectionResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ConnectedSSID { get; set; }
    }
}