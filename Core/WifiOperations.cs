using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Security;
using System.Text.RegularExpressions;
using System.Linq;

namespace MurtiWifiConnecter
{
    // John Carmack: "Prefer straightforward code over clever abstractions"
    // Single Responsibility: Only WiFi operations via netsh
    public sealed class WifiOperations : IWifiService, IDisposable
    {
        private readonly IProcessExecutor _processExecutor;
        private readonly SemaphoreSlim _operationLock;
        
        public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
        public event EventHandler<WifiErrorEventArgs>? ErrorOccurred;

        public WifiOperations(IProcessExecutor processExecutor = null)
        {
            _processExecutor = processExecutor ?? new ProcessExecutor();
            _operationLock = new SemaphoreSlim(1, 1);
        }

        public async Task<Result<WifiConnectionResult>> ConnectAsync(string ssid, string password, CancellationToken ct = default)
        {
            // Optimized validation using performance utilities
            if (!PerformanceOptimizations.ShouldAttemptConnection(ssid, password))
            {
                return Result<WifiConnectionResult>.Failure("Invalid SSID or password format");
            }

            ssid = ssid.Trim();
            password = password.Trim();

            // Optimized operation locking with timeout
            if (!await _operationLock.WaitAsync(200, ct).ConfigureAwait(false))
                return Result<WifiConnectionResult>.Failure("Another operation in progress");

            try
            {
                // Create and add profile
                var profileResult = await AddProfileAsync(ssid, password, ct).ConfigureAwait(false);
                if (!profileResult.IsSuccess)
                    return Result<WifiConnectionResult>.Failure(profileResult.Error);

                // Optimized connection with adaptive timeout
                var connectCmd = $"wlan connect name=\"{EscapeSSID(ssid)}\"";
                const int maxConnectRetries = 2;
                ProcessResult connectResult = null;

                // Pre-validate using cached connection state to avoid duplicate checks
                if (PerformanceOptimizations.IsConnectionCheckFresh(ssid))
                {
                    return Result<WifiConnectionResult>.Success(WifiConnectionResult.CreateSuccess(ssid));
                }

                var currentSSID = await GetCurrentSSIDAsync(ct).ConfigureAwait(false);
                if (currentSSID.IsSuccess && PerformanceOptimizations.SSIDEquals(currentSSID.Value, ssid))
                {
                    PerformanceOptimizations.UpdateConnectionCache(ssid);
                    return Result<WifiConnectionResult>.Success(WifiConnectionResult.CreateSuccess(ssid));
                }

                for (int attempt = 0; attempt <= maxConnectRetries; attempt++)
                {
                    if (ct.IsCancellationRequested)
                        return Result<WifiConnectionResult>.Failure("Connection cancelled");

                    // Use optimized timeout calculation
                    var timeout = PerformanceOptimizations.GetOptimalTimeout(attempt, attempt > 0);
                    connectResult = await _processExecutor.RunAsync("netsh", connectCmd, timeout);

                    if (connectResult.Success)
                        break;

                    // Optimized delay: shorter intervals for faster retry
                    if (attempt < maxConnectRetries)
                    {
                        await Task.Delay(500 * (attempt + 1), ct).ConfigureAwait(false);
                    }
                }
                
                if (connectResult == null || !connectResult.Success)
                {
                        var errorDetail = AnalyzeConnectionError(connectResult?.Error ?? "Unknown error");
                    return Result<WifiConnectionResult>.Failure($"Connection failed: {errorDetail}");
                }

                // Optimized connection verification with early success detection
                await Task.Delay(300, ct).ConfigureAwait(false);

                // Quick verification attempt using optimized comparison
                var verifyResult = await GetCurrentSSIDAsync(ct).ConfigureAwait(false);
                if (verifyResult.IsSuccess && PerformanceOptimizations.SSIDEquals(verifyResult.Value, ssid))
                {
                    PerformanceOptimizations.UpdateConnectionCache(ssid);

                    // Schedule background memory optimization
                    PerformanceOptimizations.ScheduleBackgroundTask(async () =>
                    {
                        await MemoryOptimizer.OptimizeIfNeededAsync();
                    });

                    return Result<WifiConnectionResult>.Success(WifiConnectionResult.CreateSuccess(ssid));
                }

                // Fallback verification with one retry
                await Task.Delay(500, ct).ConfigureAwait(false);
                var finalVerify = await GetCurrentSSIDAsync(ct).ConfigureAwait(false);

                if (!finalVerify.IsSuccess || !PerformanceOptimizations.SSIDEquals(finalVerify.Value, ssid))
                    return Result<WifiConnectionResult>.Failure("Connection verification failed");

                // Update cache on successful connection
                PerformanceOptimizations.UpdateConnectionCache(ssid);

                // Schedule background cleanup
                PerformanceOptimizations.ScheduleBackgroundTask(async () =>
                {
                    await MemoryOptimizer.OptimizeIfNeededAsync();
                });

                return Result<WifiConnectionResult>.Success(WifiConnectionResult.CreateSuccess(ssid));
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task<Result<bool>> DisconnectAsync(CancellationToken ct = default)
        {
            try
            {
                // Clear connection cache on disconnect attempt
                PerformanceOptimizations.UpdateConnectionCache("");

                var result = await _processExecutor.RunAsync("netsh", "wlan disconnect", 3000);

                if (result.Success)
                {
                    // Verify disconnection with retry
                    await Task.Delay(500, ct).ConfigureAwait(false);

                    var currentSSID = await GetCurrentSSIDAsync(ct).ConfigureAwait(false);
                    bool actuallyDisconnected = !currentSSID.IsSuccess || string.IsNullOrEmpty(currentSSID.Value);

                    if (!actuallyDisconnected)
                    {
                        // Retry disconnect once
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                        result = await _processExecutor.RunAsync("netsh", "wlan disconnect", 3000);
                    }

                    return Result<bool>.Success(true);
                }

                return Result<bool>.Failure(result.Error);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure(ex.Message);
            }
        }

        public async Task<Result<string>> GetCurrentSSIDAsync(CancellationToken ct = default)
        {
            var result = await _processExecutor.RunAsync("netsh", "wlan show interfaces", 5000);
            if (!result.Success)
                return Result<string>.Failure(result.Error);
            
            var output = result.Output;
            
            if (string.IsNullOrEmpty(output))
                return Result<string>.Failure("Failed to get network interface information");

            // Simple, direct parsing - no regex overhead
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && 
                    !trimmed.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                {
                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex > 0 && colonIndex < trimmed.Length - 1)
                    {
                        var ssid = trimmed.Substring(colonIndex + 1).Trim();
                        if (!string.IsNullOrWhiteSpace(ssid))
                            return Result<string>.Success(ssid);
                    }
                }
            }

            return Result<string>.Failure("Not connected");
        }

        private async Task<Result<bool>> AddProfileAsync(string ssid, string password, CancellationToken ct)
        {
            try
            {
                // Remove existing profile first (ignore errors)
                var removeCmd = $"wlan delete profile name=\"{EscapeSSID(ssid)}\"";
                await _processExecutor.RunAsync("netsh", removeCmd, 3000);

                var profileXml = CreateProfile(ssid, password);
                var tempFile = Path.GetTempFileName();

                try
                {
                    await File.WriteAllTextAsync(tempFile, profileXml, Encoding.UTF8, ct).ConfigureAwait(false);

                    var addCmd = $"wlan add profile filename=\"{tempFile}\" user=current";
                    var result = await _processExecutor.RunAsync("netsh", addCmd, 8000); // Increased timeout

                    if (result.Success)
                    {
                        return Result<bool>.Success(true);
                    }

                    // If first attempt fails, try without user=current
                    var addCmd2 = $"wlan add profile filename=\"{tempFile}\"";
                    var result2 = await _processExecutor.RunAsync("netsh", addCmd2, 8000);

                    return result2.Success
                        ? Result<bool>.Success(true)
                        : Result<bool>.Failure($"Profile add failed: {result2.Error ?? result.Error}");
                }
                finally
                {
                    // Clean up temp file
                    try { File.Delete(tempFile); } catch { /* Ignore temp file cleanup errors */ }
                }
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure($"Profile creation failed: {ex.Message}");
            }
        }

        private static string CreateProfile(string ssid, string password)
        {
            var safeSsid = System.Security.SecurityElement.Escape(ssid);
            var safePassword = System.Security.SecurityElement.Escape(password);
            var hexSsid = Convert.ToHexString(Encoding.UTF8.GetBytes(ssid));

            // Direct XML string - no string builder overhead for small strings
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{safeSsid}</name>
    <SSIDConfig>
        <SSID>
            <hex>{hexSsid}</hex>
            <name>{safeSsid}</name>
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
                <keyMaterial>{safePassword}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";
        }

        private static string EscapeSSID(string ssid) => Validation.SanitizeSSID(ssid).Value ?? ssid;

        private static string AnalyzeConnectionError(string error)
        {
            if (string.IsNullOrEmpty(error))
                return "Unknown connection error";
                
            var lowerError = error.ToLowerInvariant();
            
            if (lowerError.Contains("not found") || lowerError.Contains("does not exist"))
                return "Network not found. Please check if the network is available.";
            
            if (lowerError.Contains("denied") || lowerError.Contains("authentication"))
                return "Authentication failed. Please check your password.";
                
            if (lowerError.Contains("timeout"))
                return "Connection timeout. The network may be out of range.";
                
            if (lowerError.Contains("already"))
                return "Already connected to another network.";
                
            if (lowerError.Contains("disabled") || lowerError.Contains("adapter"))
                return "WiFi adapter is disabled or not found.";
                
            return error;
        }

        
        private void OnConnectionStateChanged(ConnectionState oldState, ConnectionState newState, string ssid = null)
        {
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, newState, ssid, "WifiOperations"));
        }
        
        private void OnErrorOccurred(Exception error, string operation, string ssid = null, ErrorSeverity severity = ErrorSeverity.Error)
        {
            ErrorOccurred?.Invoke(this, new WifiErrorEventArgs(error, operation, ssid, severity, true, "WifiOperations"));
        }
        
        // セキュリティ・検証メソッド
        private static Result<bool> ValidateConnectionInput(string ssid, string password)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return Result<bool>.Failure("SSID cannot be empty");
            
            if (ssid.Length > 32)
                return Result<bool>.Failure("SSID cannot exceed 32 characters");
            
            if (string.IsNullOrEmpty(password))
                return Result<bool>.Failure("Password cannot be empty");
            
            if (password.Length < 8)
                return Result<bool>.Failure("Password must be at least 8 characters");
            
            // SSID文字制限チェック
            if (!Regex.IsMatch(ssid, @"^[\w\s\-\.!@#$%^&*()+={}[\]:;'<>?,./~`]+$"))
                return Result<bool>.Failure("SSID contains invalid characters");
            
            return Result<bool>.Success(true);
        }
        
        private static async Task ValidateNetworkSecurityAsync(string ssid, CancellationToken ct)
        {
            // 既知の危険なネットワーク名をチェック
            var dangerousNames = new[] { "Free WiFi", "Public WiFi", "Starbucks WiFi", "Airport WiFi", "Hotel WiFi" };
            if (dangerousNames.Any(name => ssid.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                Logging.LogWarning("SecurityCheck", $"Potentially unsafe network: {ssid}");
            }
            
            await Task.CompletedTask;
        }
        
        private static string SanitizeSSID(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return "";
            
            var sanitized = ssid.Trim();
            
            // 危険な文字を除去
            sanitized = Regex.Replace(sanitized, @"[\""|&;$`<>()]", "");
            
            return sanitized;
        }
        
        private static string SanitizePassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return "";
            
            // パスワードは基本的にそのまま使用するが、制御文字を除去
            return Regex.Replace(password, @"[\x00-\x1F\x7F]", "");
        }
        
        // 不足していたインターフェースメソッドを追加
        public async Task<Result<WifiConnectionResult>> ConnectSecureAsync(string ssid, SecureString password, CancellationToken ct = default)
        {
            var unsecuredPassword = password?.ToUnsecuredString() ?? "";
            return await ConnectAsync(ssid, unsecuredPassword, ct).ConfigureAwait(false);
        }

        public async Task<Result<NetworkInfo>> GetCurrentNetworkInfoAsync(CancellationToken ct = default)
        {
            try
            {
                var ssidResult = await GetCurrentSSIDAsync(ct).ConfigureAwait(false);
                if (!ssidResult.IsSuccess)
                    return Result<NetworkInfo>.Failure(ssidResult.Error);

                // 追加のネットワーク情報を取得
                var interfaceResult = await _processExecutor.RunAsync("netsh", "wlan show interfaces", 5000);
                if (!interfaceResult.Success)
                    return Result<NetworkInfo>.Failure("Could not get network interface information");

                var networkInfo = ParseNetworkInfo(interfaceResult.Output, ssidResult.Value);
                return Result<NetworkInfo>.Success(networkInfo);
            }
            catch (Exception ex)
            {
                return Result<NetworkInfo>.Failure(ex.Message);
            }
        }

        public async Task<Result<bool>> ValidateConnectionAsync(string ssid, CancellationToken ct = default)
        {
            try
            {
                var currentResult = await GetCurrentSSIDAsync(ct).ConfigureAwait(false);
                if (!currentResult.IsSuccess)
                    return Result<bool>.Success(false);

                var isConnected = string.Equals(currentResult.Value, ssid, StringComparison.OrdinalIgnoreCase);
                return Result<bool>.Success(isConnected);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure(ex.Message);
            }
        }

        private NetworkInfo ParseNetworkInfo(string interfaceOutput, string ssid)
        {
            var networkInfo = new NetworkInfo 
            { 
                SSID = ssid,
                SignalStrength = 0,
                Authentication = "Unknown"
            };

            if (string.IsNullOrEmpty(interfaceOutput))
                return networkInfo;

            var lines = interfaceOutput.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                if (trimmed.StartsWith("Signal", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("%"))
                {
                    var percentIndex = trimmed.IndexOf('%');
                    if (percentIndex > 0)
                    {
                        var signalText = trimmed.Substring(0, percentIndex).Split(' ').LastOrDefault();
                        if (int.TryParse(signalText, out var signal))
                            networkInfo.SignalStrength = signal;
                    }
                }
                else if (trimmed.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase))
                {
                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex > 0 && colonIndex < trimmed.Length - 1)
                    {
                        networkInfo.Authentication = trimmed.Substring(colonIndex + 1).Trim();
                    }
                }
            }

            return networkInfo;
        }

        private void OnConnectionStateChanged(string ssid, ConnectionState previousState, ConnectionState currentState, string? message = null)
        {
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(ssid, previousState, currentState, message));
        }

        private void OnErrorOccurred(string errorMessage, Exception? exception = null, string? ssid = null, ErrorSeverity severity = ErrorSeverity.Warning, string? context = null)
        {
            ErrorOccurred?.Invoke(this, new WifiErrorEventArgs(errorMessage, exception, ssid, severity, context));
        }

        public void Dispose()
        {
            ConnectionStateChanged = null;
            ErrorOccurred = null;
            
            _operationLock?.Dispose();
            
            Logging.LogInfo("WifiOperations", "WiFi operations disposed");
        }
    }
}