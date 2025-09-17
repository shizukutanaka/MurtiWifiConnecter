using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// WiFi adapter recovery utility
    /// </summary>
    public class AdapterRecovery
    {
        private readonly ProcessExecutor _processExecutor;
        private DateTime _lastRecoveryAttempt = DateTime.MinValue;
        private readonly TimeSpan _recoveryInterval = TimeSpan.FromMinutes(1);

        public AdapterRecovery()
        {
            _processExecutor = new ProcessExecutor();
        }

        /// <summary>
        /// Check if WiFi adapter is working
        /// </summary>
        public async Task<bool> IsAdapterWorkingAsync(CancellationToken ct = default)
        {
            try
            {
                var result = await _processExecutor.RunAsync("netsh", "wlan show interfaces", 3000);
                return result.Success && !string.IsNullOrEmpty(result.Output);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempt to recover WiFi adapter
        /// </summary>
        public async Task<RecoveryResult> RecoverAdapterAsync(CancellationToken ct = default)
        {
            var result = new RecoveryResult();

            // Check if enough time has passed since last recovery attempt
            if (DateTime.UtcNow - _lastRecoveryAttempt < _recoveryInterval)
            {
                result.Success = false;
                result.Message = "Recovery attempt too soon. Please wait.";
                return result;
            }

            _lastRecoveryAttempt = DateTime.UtcNow;

            try
            {
                Logger.Info("Starting WiFi adapter recovery...");

                // Step 1: Try to restart the adapter
                var restartResult = await RestartAdapterAsync(ct);
                if (restartResult)
                {
                    result.Success = true;
                    result.Message = "WiFi adapter restarted successfully";
                    result.Method = RecoveryMethod.AdapterRestart;
                    return result;
                }

                // Step 2: Try to reset WiFi settings
                var resetResult = await ResetWifiSettingsAsync(ct);
                if (resetResult)
                {
                    result.Success = true;
                    result.Message = "WiFi settings reset successfully";
                    result.Method = RecoveryMethod.SettingsReset;
                    return result;
                }

                // Step 3: Try to restart network service
                var serviceResult = await RestartNetworkServiceAsync(ct);
                if (serviceResult)
                {
                    result.Success = true;
                    result.Message = "Network service restarted successfully";
                    result.Method = RecoveryMethod.ServiceRestart;
                    return result;
                }

                // Step 4: Try to reset TCP/IP stack
                var tcpipResult = await ResetTcpIpStackAsync(ct);
                if (tcpipResult)
                {
                    result.Success = true;
                    result.Message = "TCP/IP stack reset successfully";
                    result.Method = RecoveryMethod.TcpIpReset;
                    return result;
                }

                result.Success = false;
                result.Message = "All recovery methods failed";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Recovery failed: {ex.Message}";
                Logger.Error($"Adapter recovery failed: {ex.Message}", ex);
            }

            return result;
        }

        /// <summary>
        /// Restart WiFi adapter
        /// </summary>
        private async Task<bool> RestartAdapterAsync(CancellationToken ct)
        {
            try
            {
                Logger.Info("Attempting to restart WiFi adapter...");

                // Disable adapter
                var disableResult = await _processExecutor.RunAsync(
                    "netsh", "interface set interface \"Wi-Fi\" disable", 5000);

                if (!disableResult.Success)
                {
                    // Try alternative name
                    disableResult = await _processExecutor.RunAsync(
                        "netsh", "interface set interface \"Wireless Network Connection\" disable", 5000);
                }

                await Task.Delay(2000, ct);

                // Enable adapter
                var enableResult = await _processExecutor.RunAsync(
                    "netsh", "interface set interface \"Wi-Fi\" enable", 5000);

                if (!enableResult.Success)
                {
                    // Try alternative name
                    enableResult = await _processExecutor.RunAsync(
                        "netsh", "interface set interface \"Wireless Network Connection\" enable", 5000);
                }

                await Task.Delay(3000, ct);

                // Verify it's working
                return await IsAdapterWorkingAsync(ct);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to restart adapter: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Reset WiFi settings
        /// </summary>
        private async Task<bool> ResetWifiSettingsAsync(CancellationToken ct)
        {
            try
            {
                Logger.Info("Attempting to reset WiFi settings...");

                // Delete all profiles
                var deleteResult = await _processExecutor.RunAsync(
                    "netsh", "wlan delete profile name=* i=*", 5000);

                // Reset winsock
                var winsockResult = await _processExecutor.RunAsync(
                    "netsh", "winsock reset", 5000);

                await Task.Delay(2000, ct);

                return await IsAdapterWorkingAsync(ct);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to reset WiFi settings: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Restart network service
        /// </summary>
        private async Task<bool> RestartNetworkServiceAsync(CancellationToken ct)
        {
            try
            {
                Logger.Info("Attempting to restart network service...");

                // Stop WLAN AutoConfig service
                var stopResult = await _processExecutor.RunAsync(
                    "net", "stop Wlansvc /y", 10000);

                await Task.Delay(2000, ct);

                // Start WLAN AutoConfig service
                var startResult = await _processExecutor.RunAsync(
                    "net", "start Wlansvc", 10000);

                await Task.Delay(3000, ct);

                return await IsAdapterWorkingAsync(ct);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to restart network service: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Reset TCP/IP stack
        /// </summary>
        private async Task<bool> ResetTcpIpStackAsync(CancellationToken ct)
        {
            try
            {
                Logger.Info("Attempting to reset TCP/IP stack...");

                // Reset TCP/IP
                var tcpResult = await _processExecutor.RunAsync(
                    "netsh", "int ip reset", 5000);

                // Reset IPv6
                var ipv6Result = await _processExecutor.RunAsync(
                    "netsh", "int ipv6 reset", 5000);

                // Flush DNS
                var dnsResult = await _processExecutor.RunAsync(
                    "ipconfig", "/flushdns", 5000);

                // Release and renew IP
                await _processExecutor.RunAsync("ipconfig", "/release", 5000);
                await Task.Delay(1000, ct);
                await _processExecutor.RunAsync("ipconfig", "/renew", 5000);

                await Task.Delay(3000, ct);

                return await IsAdapterWorkingAsync(ct);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to reset TCP/IP stack: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Diagnose network issues
        /// </summary>
        public async Task<DiagnosticResult> DiagnoseNetworkAsync(CancellationToken ct = default)
        {
            var result = new DiagnosticResult();

            try
            {
                // Check adapter status
                var adapterWorking = await IsAdapterWorkingAsync(ct);
                result.AdapterDetected = adapterWorking;

                if (!adapterWorking)
                {
                    result.Issues.Add("WiFi adapter not detected or disabled");
                    result.Recommendations.Add("Try running adapter recovery");
                }

                // Check for driver issues
                var driverResult = await _processExecutor.RunAsync(
                    "pnputil", "/enum-drivers", 5000);

                if (!driverResult.Success || driverResult.Output.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    result.Issues.Add("Possible driver issues detected");
                    result.Recommendations.Add("Update or reinstall WiFi drivers");
                }

                // Check Windows services
                var serviceResult = await _processExecutor.RunAsync(
                    "sc", "query Wlansvc", 3000);

                if (serviceResult.Success && serviceResult.Output.Contains("STOPPED"))
                {
                    result.Issues.Add("WLAN service is stopped");
                    result.Recommendations.Add("Start WLAN AutoConfig service");
                }

                // Check firewall
                var firewallResult = await _processExecutor.RunAsync(
                    "netsh", "advfirewall show currentprofile", 3000);

                if (firewallResult.Success && firewallResult.Output.Contains("Block"))
                {
                    result.Issues.Add("Firewall may be blocking connections");
                    result.Recommendations.Add("Check firewall settings");
                }

                result.Success = result.Issues.Count == 0;
                result.Summary = result.Success
                    ? "No issues detected"
                    : $"Found {result.Issues.Count} issue(s)";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Summary = $"Diagnosis failed: {ex.Message}";
                Logger.Error($"Network diagnosis failed: {ex.Message}", ex);
            }

            return result;
        }
    }

    public class RecoveryResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public RecoveryMethod Method { get; set; }
    }

    public enum RecoveryMethod
    {
        None,
        AdapterRestart,
        SettingsReset,
        ServiceRestart,
        TcpIpReset
    }

    public class DiagnosticResult
    {
        public bool Success { get; set; }
        public bool AdapterDetected { get; set; }
        public System.Collections.Generic.List<string> Issues { get; set; } = new();
        public System.Collections.Generic.List<string> Recommendations { get; set; } = new();
        public string Summary { get; set; } = string.Empty;
    }
}