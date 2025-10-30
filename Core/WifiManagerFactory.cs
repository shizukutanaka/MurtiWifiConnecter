using System;
using System.Runtime.InteropServices;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// プラットフォームに応じたWiFiマネージャーを提供するファクトリークラス
    /// </summary>
    public static class WifiManagerFactory
    {
        /// <summary>
        /// 現在のプラットフォームに適したWiFiマネージャーを作成する
        /// </summary>
        public static IWifiManager CreateWifiManager()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new Core.Windows.WindowsWifiManager();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new Core.macOS.macOSWifiManager();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new Core.Linux.LinuxWifiManager();
            }
            else
            {
                throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription}");
            }
        }

        /// <summary>
        /// 現在のプラットフォームがサポートされているかを確認する
        /// </summary>
        public static bool IsPlatformSupported()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                   RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                   RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }

        /// <summary>
        /// 現在のプラットフォーム名を取得する
        /// </summary>
        public static string GetPlatformName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "Windows";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "macOS";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "Linux";
            }
            else
            {
                return "Unknown";
            }
        }
    }
}
