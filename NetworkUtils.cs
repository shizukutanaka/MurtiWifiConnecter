using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    public static class NetworkUtils
    {
        private const int QUICK_TIMEOUT_MS = 3000;
        private const int NORMAL_TIMEOUT_MS = 10000;
        private const int EXTENDED_TIMEOUT_MS = 15000;

        public static async Task<string> ExecuteNetshCommandAsync(string arguments, int timeoutMs = 10000, CancellationToken cancellationToken = default)
        {
            return await ExecuteCommandAsync("netsh", arguments, timeoutMs, cancellationToken);
        }

        public static async Task<string> ExecuteCommandAsync(string fileName, string arguments, int timeoutMs = 10000, CancellationToken cancellationToken = default)
        {
            Process proc = null;
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                proc = Process.Start(psi);
                if (proc == null) return string.Empty;

                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var finished = await Task.Run(() => proc.WaitForExit(timeoutMs), cancellationToken);

                if (!finished)
                {
                    try { proc.Kill(); } catch { }
                    return string.Empty;
                }

                if (proc.ExitCode != 0) return string.Empty;
                return await outputTask;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                try { proc?.Kill(); } catch { }
                proc?.Dispose();
            }
        }

        public static async Task<bool> ExecuteNetshCommandWithResultAsync(string arguments, int timeoutMs = 10000, CancellationToken cancellationToken = default)
        {
            return await ExecuteCommandWithResultAsync("netsh", arguments, timeoutMs, cancellationToken);
        }

        public static async Task<bool> ExecuteCommandWithResultAsync(string fileName, string arguments, int timeoutMs = 10000, CancellationToken cancellationToken = default)
        {
            Process proc = null;
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                proc = Process.Start(psi);
                if (proc == null) return false;

                var finished = await Task.Run(() => proc.WaitForExit(timeoutMs), cancellationToken);

                if (!finished)
                {
                    try { proc.Kill(); } catch { }
                    return false;
                }

                return proc.ExitCode == 0;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { proc?.Kill(); } catch { }
                proc?.Dispose();
            }
        }

        public static async Task<bool> ConnectWithRetryAsync(string ssid, string password, int maxRetries = 3, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return false;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (cancellationToken.IsCancellationRequested) return false;

                var attemptTimeout = attempt == 1 ? QUICK_TIMEOUT_MS : NORMAL_TIMEOUT_MS;

                try
                {
                    // 既存プロファイルで接続試行
                    var success = await ExecuteNetshCommandWithResultAsync(
                        $"wlan connect name=\"{SanitizeSSID(ssid)}\"",
                        attemptTimeout,
                        cancellationToken);

                    if (success)
                    {
                        // 接続成功後、実際に接続されたか確認
                        await Task.Delay(2000, cancellationToken);
                        var connectedSsid = await GetCurrentConnectedSSIDAsync(cancellationToken);
                        if (!string.IsNullOrEmpty(connectedSsid) &&
                            connectedSsid.Equals(ssid, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    // パスワードが提供されていて、プロファイルが存在しない場合は作成
                    if (!string.IsNullOrEmpty(password) && attempt == 1)
                    {
                        await CreateAndAddProfileAsync(ssid, password, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch
                {
                    // エラーを無視して次の試行へ
                }

                // 最後の試行でない場合は待機
                if (attempt < maxRetries)
                {
                    await Task.Delay(2000, cancellationToken);
                }
            }

            return false;
        }

        public static async Task<string> GetCurrentConnectedSSIDAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var output = await ExecuteNetshCommandAsync("wlan show interfaces", 3000, cancellationToken);
                if (string.IsNullOrEmpty(output)) return null;

                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                        !trimmedLine.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                    {
                        var colonIndex = trimmedLine.IndexOf(':');
                        if (colonIndex > 0 && colonIndex < trimmedLine.Length - 1)
                        {
                            var ssid = trimmedLine.Substring(colonIndex + 1).Trim();
                            return string.IsNullOrWhiteSpace(ssid) ? null : ssid;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static async Task<bool> CreateAndAddProfileAsync(string ssid, string password, CancellationToken cancellationToken = default)
        {
            try
            {
                string safePassword = System.Security.SecurityElement.Escape(password);
                string safeSsid = System.Security.SecurityElement.Escape(ssid);

                string profileXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{safeSsid}</name>
    <SSIDConfig>
        <SSID>
            <name>{safeSsid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{safePassword}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";

                var tempPath = Path.Combine(Path.GetTempPath(), $"wifi_{CreateSafeFileName(ssid)}_{Guid.NewGuid():N}.xml");
                await File.WriteAllTextAsync(tempPath, profileXml, cancellationToken);

                var success = await ExecuteNetshCommandWithResultAsync(
                    $"wlan add profile filename=\"{tempPath}\" user=current",
                    10000,
                    cancellationToken);

                try { File.Delete(tempPath); } catch { }

                return success;
            }
            catch
            {
                return false;
            }
        }

        public static string SanitizeSSID(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return string.Empty;

            // 長さ制限（WiFi SSID最大32バイト）
            if (ssid.Length > 32) return string.Empty;

            // 制御文字を除去
            return System.Text.RegularExpressions.Regex.Replace(ssid, @"[\x00-\x1F\x7F]", "");
        }

        public static string CreateSafeFileName(string input, int maxLength = 20)
        {
            if (string.IsNullOrWhiteSpace(input)) return "default";

            var safe = string.Join("", input.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'));
            return safe.Length > maxLength ? safe.Substring(0, maxLength) : safe;
        }

        public static bool IsValidNetworkName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   name.Length <= 32 &&
                   !name.Any(c => char.IsControl(c));
        }

        public static int CalculateSignalStrength(int rssi)
        {
            // RSSI を品質パーセンテージに変換
            // -30dBm = 100%, -90dBm = 0% の線形変換
            return (int)Math.Max(0, Math.Min(100, 2 * (rssi + 100)));
        }

        public static string FormatSignalStrength(int signalStrength)
        {
            return signalStrength switch
            {
                >= 80 => "優秀",
                >= 60 => "良好",
                >= 40 => "普通",
                >= 20 => "弱い",
                _ => "非常に弱い"
            };
        }

        public static TimeSpan GetOptimalScanInterval(int networkCount)
        {
            // ネットワーク数に基づいて最適なスキャン間隔を計算
            return networkCount switch
            {
                < 5 => TimeSpan.FromSeconds(10),
                < 15 => TimeSpan.FromSeconds(15),
                < 30 => TimeSpan.FromSeconds(20),
                _ => TimeSpan.FromSeconds(30)
            };
        }

        public static void OptimizeMemory()
        {
            MemoryOptimizer.OptimizeMemoryIfNeeded();
        }
    }
}