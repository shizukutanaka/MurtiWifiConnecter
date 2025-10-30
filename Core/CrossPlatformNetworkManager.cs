using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// クロスプラットフォームネットワーク操作マネージャー
    /// </summary>
    public static class CrossPlatformNetworkManager
    {
        private static readonly Lazy<INetworkOperations> _platformImplementation =
            new Lazy<INetworkOperations>(CreatePlatformImplementation, LazyThreadSafetyMode.ExecutionAndPublication);

        private static PlatformInfo _detectedPlatformInfo;

        /// <summary>
        /// 現在のプラットフォームの実装を取得
        /// </summary>
        public static INetworkOperations Current => _platformImplementation.Value;

        /// <summary>
        /// 検出されたプラットフォームを取得
        /// </summary>
        public static PlatformType DetectedPlatform => DetectPlatformInfo().PlatformType;

        /// <summary>
        /// プラットフォーム情報を取得
        /// </summary>
        public static PlatformInfo PlatformInfo => _detectedPlatformInfo ??= DetectPlatformInfo();

        /// <summary>
        /// プラットフォーム情報を検出（詳細版）
        /// </summary>
        private static PlatformInfo DetectPlatformInfo()
        {
            var info = new PlatformInfo();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                info.PlatformType = PlatformType.Windows;
                info.Version = Environment.OSVersion.Version;
                info.Description = $"Windows {Environment.OSVersion.VersionString}";
                info.Is64Bit = Environment.Is64BitOperatingSystem;

                // Windows特有のバージョン詳細を取得
                try
                {
                    var version = Environment.OSVersion.Version;
                    if (version.Major == 10 && version.Minor == 0 && version.Build >= 22000)
                        info.VersionName = "Windows 11";
                    else if (version.Major == 10)
                        info.VersionName = "Windows 10";
                    else if (version.Major == 6 && version.Minor == 3)
                        info.VersionName = "Windows 8.1";
                    else if (version.Major == 6 && version.Minor == 2)
                        info.VersionName = "Windows 8";
                    else if (version.Major == 6 && version.Minor == 1)
                        info.VersionName = "Windows 7";
                    else
                        info.VersionName = "Windows (Unknown Version)";
                }
                catch
                {
                    info.VersionName = "Windows";
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                info.PlatformType = PlatformType.macOS;
                info.Version = Environment.OSVersion.Version;
                info.Description = $"macOS {Environment.OSVersion.VersionString}";
                info.Is64Bit = Environment.Is64BitOperatingSystem;

                // macOSバージョン名を取得
                try
                {
                    var version = Environment.OSVersion.Version;
                    if (version.Major == 13)
                        info.VersionName = "macOS Ventura";
                    else if (version.Major == 12)
                        info.VersionName = "macOS Monterey";
                    else if (version.Major == 11)
                        info.VersionName = "macOS Big Sur";
                    else if (version.Major == 10 && version.Minor == 15)
                        info.VersionName = "macOS Catalina";
                    else if (version.Major == 10 && version.Minor == 14)
                        info.VersionName = "macOS Mojave";
                    else if (version.Major == 10 && version.Minor == 13)
                        info.VersionName = "macOS High Sierra";
                    else
                        info.VersionName = "macOS (Unknown Version)";
                }
                catch
                {
                    info.VersionName = "macOS";
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                info.PlatformType = PlatformType.Linux;
                info.Version = Environment.OSVersion.Version;
                info.Description = $"Linux {Environment.OSVersion.VersionString}";
                info.Is64Bit = Environment.Is64BitOperatingSystem;

                // Linuxディストリビューションを検出
                try
                {
                    info.VersionName = DetectLinuxDistribution();
                }
                catch
                {
                    info.VersionName = "Linux";
                }
            }
            else
            {
                info.PlatformType = PlatformType.Unknown;
                info.Version = Environment.OSVersion.Version;
                info.Description = $"Unknown OS {Environment.OSVersion.VersionString}";
                info.VersionName = "Unknown";
                info.Is64Bit = Environment.Is64BitOperatingSystem;
            }

            return info;
        }

        /// <summary>
        /// Linuxディストリビューションを検出
        /// </summary>
        private static string DetectLinuxDistribution()
        {
            try
            {
                // /etc/os-releaseファイルからディストリビューション情報を取得
                if (System.IO.File.Exists("/etc/os-release"))
                {
                    var lines = System.IO.File.ReadAllLines("/etc/os-release");
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("PRETTY_NAME="))
                        {
                            return line.Substring(13).Trim('"');
                        }
                    }
                }

                // フォールバック: unameコマンドを使用
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "uname",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                var output = process?.StandardOutput.ReadToEnd();
                if (!string.IsNullOrEmpty(output))
                {
                    return "Linux (" + output.Split()[0] + ")";
                }
            }
            catch
            {
                // 検出失敗時は一般的な名前を返す
            }

            return "Linux";
        }

        /// <summary>
        /// プラットフォームに応じた実装を作成（強化版）
        /// </summary>
        private static INetworkOperations CreatePlatformImplementation()
        {
            var platformInfo = PlatformInfo;

            // パフォーマンス監視を開始
            PerformanceProfiler.ProfileMethod(() =>
            {
                Logger.LogInfo($"Initializing network operations for {platformInfo.VersionName} ({platformInfo.Description})",
                    nameof(CrossPlatformNetworkManager));
            }, "PlatformInitialization");

            return platformInfo.PlatformType switch
            {
                PlatformType.Windows => new WindowsNetworkOperations(),
                PlatformType.macOS => new MacOSNetworkOperations(),
                PlatformType.Linux => new LinuxNetworkOperations(),
                _ => throw new PlatformNotSupportedException($"Platform {platformInfo.PlatformType} ({platformInfo.VersionName}) is not supported")
            };
        }

        /// <summary>
        /// プラットフォーム互換性を検証
        /// </summary>
        public static async Task<PlatformCompatibilityResult> ValidatePlatformCompatibilityAsync()
        {
            var result = new PlatformCompatibilityResult
            {
                PlatformInfo = PlatformInfo,
                IsCompatible = false,
                Warnings = new List<string>(),
                Recommendations = new List<string>()
            };

            try
            {
                // 基本的なネットワーク操作をテスト
                var testResult = await PerformanceProfiler.ProfileMethodAsync(
                    async () => await Current.GetStatusAsync(),
                    "PlatformCompatibilityTest"
                );

                result.IsCompatible = true;
                result.TestSuccessful = true;

                // プラットフォーム固有のチェック
                switch (PlatformInfo.PlatformType)
                {
                    case PlatformType.Windows:
                        await ValidateWindowsCompatibility(result);
                        break;
                    case PlatformType.macOS:
                        await ValidateMacOSCompatibility(result);
                        break;
                    case PlatformType.Linux:
                        await ValidateLinuxCompatibility(result);
                        break;
                }
            }
            catch (Exception ex)
            {
                result.IsCompatible = false;
                result.TestSuccessful = false;
                result.ErrorMessage = ex.Message;
                result.Warnings.Add($"Platform compatibility test failed: {ex.Message}");
            }

            return result;
        }

        private static async Task ValidateWindowsCompatibility(PlatformCompatibilityResult result)
        {
            // Windows特有のチェック（管理者権限、必要なサービスなど）
            try
            {
                // netshコマンドが利用可能かチェック
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show interfaces",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    result.Warnings.Add("netsh command may not be available or accessible");
                    result.Recommendations.Add("Ensure you have administrator privileges for network operations");
                }
            }
            catch
            {
                result.Warnings.Add("Failed to validate Windows networking capabilities");
            }
        }

        private static async Task ValidateMacOSCompatibility(PlatformCompatibilityResult result)
        {
            // macOS特有のチェック（airportコマンド、ネットワーク設定など）
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "airport",
                    Arguments = "-I",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    result.Warnings.Add("airport command may not be available");
                    result.Recommendations.Add("Ensure airport utility is properly installed");
                }
            }
            catch
            {
                result.Warnings.Add("Failed to validate macOS networking capabilities");
            }
        }

        private static async Task ValidateLinuxCompatibility(PlatformCompatibilityResult result)
        {
            // Linux特有のチェック（nmcli、NetworkManagerなど）
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "nmcli",
                    Arguments = "device wifi list",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    result.Warnings.Add("nmcli command may not be available or NetworkManager is not running");
                    result.Recommendations.Add("Ensure NetworkManager is installed and running");
                }
            }
            catch
            {
                result.Warnings.Add("Failed to validate Linux networking capabilities");
                result.Recommendations.Add("Consider installing NetworkManager and nmcli");
            }
        }
    }

    /// <summary>
    /// プラットフォーム情報
    /// </summary>
    public class PlatformInfo
    {
        public PlatformType PlatformType { get; set; }
        public Version Version { get; set; }
        public string VersionName { get; set; }
        public string Description { get; set; }
        public bool Is64Bit { get; set; }
        public string Architecture => Is64Bit ? "64-bit" : "32-bit";
    }

    /// <summary>
    /// プラットフォーム互換性検証結果
    /// </summary>
    public class PlatformCompatibilityResult
    {
        public PlatformInfo PlatformInfo { get; set; }
        public bool IsCompatible { get; set; }
        public bool TestSuccessful { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// プラットフォームタイプ
    /// </summary>
    public enum PlatformType
    {
        Unknown,
        Windows,
        macOS,
        Linux
    }

    /// <summary>
    /// クロスプラットフォームネットワーク操作インターフェース
    /// </summary>
    public interface INetworkOperations
    {
        Task<List<NetworkInfo>> ScanNetworksAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
        Task<bool> ConnectAsync(string ssid, string password = null, CancellationToken cancellationToken = default);
        Task<bool> DisconnectAsync(CancellationToken cancellationToken = default);
        Task<ConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default);
        Task<List<string>> GetSavedProfilesAsync(CancellationToken cancellationToken = default);
        Task<bool> DeleteProfileAsync(string ssid, CancellationToken cancellationToken = default);
        PlatformType Platform { get; }
    }
}
