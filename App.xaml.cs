using System;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

namespace MurtiWifiConnecter;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            // コマンドライン引数処理
            if (e.Args.Length > 0)
            {
                var processed = ProcessCommandLineArgs(e.Args);
                if (processed)
                {
                    // コマンドライン処理のみで終了
                    Shutdown(0);
                    return;
                }
            }

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"起動エラー: {ex.Message}", "Murti WiFi コネクター", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static bool ProcessCommandLineArgs(string[] args)
    {
        var command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "--help":
            case "-h":
                ShowHelp();
                return true;

            case "--version":
            case "-v":
                ShowVersion();
                return true;

            case "--scan":
            case "-s":
                _ = ScanNetworksAsync();
                return true;

            case "--connect":
            case "-c":
                if (args.Length > 1)
                {
                    var ssid = args[1];
                    var password = args.Length > 2 ? args[2] : "";
                    _ = ConnectToNetworkAsync(ssid, password);
                }
                else
                {
                    Console.WriteLine("使用法: --connect <SSID> [password]");
                }
                return true;

            case "--status":
                _ = ShowNetworkStatusAsync();
                return true;
                
            case "--diagnose":
            case "-d":
                _ = RunNetworkDiagnosticsAsync();
                return true;
                
            case "--fix":
            case "-f":
                _ = RunQuickFixAsync();
                return true;
                
            case "--portable":
                // ポータブルモード（簡易版）
                return true;

            default:
                return false; // 通常のGUI起動
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Murti WiFi コネクター - コマンドライン使用法:");
        Console.WriteLine();
        Console.WriteLine("  --help, -h          このヘルプを表示");
        Console.WriteLine("  --version, -v       バージョン情報を表示");
        Console.WriteLine("  --scan, -s          利用可能なWiFiネットワークをスキャン");
        Console.WriteLine("  --connect, -c <SSID> [password]  指定したネットワークに接続");
        Console.WriteLine("  --status            現在の接続状態を表示");
        Console.WriteLine("  --diagnose, -d      ネットワーク診断を実行");
        Console.WriteLine("  --fix, -f           ネットワーク問題の自動修復");
        Console.WriteLine();
        Console.WriteLine("例:");
        Console.WriteLine("  MurtiWifiConnecter --scan");
        Console.WriteLine("  MurtiWifiConnecter --connect \"MyNetwork\" \"password123\"");
        Console.WriteLine("  MurtiWifiConnecter --status");
        Console.WriteLine("  MurtiWifiConnecter --diagnose");
        Console.WriteLine("  MurtiWifiConnecter --fix");
        Console.WriteLine("  MurtiWifiConnecter --portable");
    }

    private static void ShowVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Console.WriteLine($"Murti WiFi コネクター v{version}");
        Console.WriteLine("軽量で実用的なWiFi接続管理ツール");
    }

    private static async System.Threading.Tasks.Task ScanNetworksAsync()
    {
        try
        {
            Console.WriteLine("WiFiネットワークをスキャンしています...");
            
            var output = await NetworkUtils.ExecuteNetshCommandAsync("wlan show profiles", 5000);
            if (string.IsNullOrEmpty(output))
            {
                Console.WriteLine("スキャンに失敗しました。");
                return;
            }

            Console.WriteLine("利用可能なネットワーク:");
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("All User Profile") || line.Contains("User Profile"))
                {
                    var parts = line.Split(':');
                    if (parts.Length > 1)
                    {
                        Console.WriteLine($"  - {parts[1].Trim()}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"スキャンエラー: {ex.Message}");
        }
    }

    private static async System.Threading.Tasks.Task ConnectToNetworkAsync(string ssid, string password)
    {
        try
        {
            Console.WriteLine($"'{ssid}' に接続しています...");
            
            var success = await NetworkUtils.ExecuteNetshCommandWithResultAsync($"wlan connect name=\"{ssid}\"", 15000);
            
            if (success)
            {
                Console.WriteLine($"'{ssid}' に正常に接続しました。");
            }
            else
            {
                Console.WriteLine($"'{ssid}' への接続に失敗しました。");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"接続エラー: {ex.Message}");
        }
    }

    private static async System.Threading.Tasks.Task ShowNetworkStatusAsync()
    {
        try
        {
            var output = await NetworkUtils.ExecuteNetshCommandAsync("wlan show interfaces", 5000);
            if (string.IsNullOrEmpty(output))
            {
                Console.WriteLine("ネットワーク状態の取得に失敗しました。");
                return;
            }

            Console.WriteLine("現在のWiFi接続状態:");
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Name") || trimmed.StartsWith("Description") || 
                    trimmed.StartsWith("State") || trimmed.StartsWith("SSID"))
                {
                    Console.WriteLine($"  {trimmed}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"状態取得エラー: {ex.Message}");
        }
    }

    private static async System.Threading.Tasks.Task RunNetworkDiagnosticsAsync()
    {
        try
        {
            Console.WriteLine("ネットワーク診断を実行しています...");
            
            // 簡易診断
            var connectedSsid = await NetworkUtils.GetCurrentConnectedSSIDAsync();
            if (!string.IsNullOrEmpty(connectedSsid))
            {
                Console.WriteLine($"✅ 現在接続中: {connectedSsid}");
            }
            else
            {
                Console.WriteLine("⚠️ 現在WiFiに接続されていません。");
                Console.WriteLine("利用可能なネットワークをスキャンしています...");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"診断エラー: {ex.Message}");
        }
    }

    private static async System.Threading.Tasks.Task RunQuickFixAsync()
    {
        try
        {
            Console.WriteLine("クイック修復を実行しています...");
            
            // 基本的な修復手順を順次実行
            var steps = new[]
            {
                ("DNSキャッシュクリア", "ipconfig", "/flushdns"),
                ("ARP キャッシュクリア", "arp", "-d *"),
                ("ネットワーク設定リフレッシュ", "ipconfig", "/release"),
                ("IPアドレス再取得", "ipconfig", "/renew")
            };
            
            var successCount = 0;
            
            foreach (var (description, command, args) in steps)
            {
                Console.Write($"  {description}... ");
                
                try
                {
                    var success = await NetworkUtils.ExecuteCommandWithResultAsync(command, args, 10000);
                    if (success)
                    {
                        Console.WriteLine("✅");
                        successCount++;
                    }
                    else
                    {
                        Console.WriteLine("❌");
                    }
                }
                catch
                {
                    Console.WriteLine("❌");
                }
                
                await System.Threading.Tasks.Task.Delay(1000);
            }
            
            Console.WriteLine($"\n修復完了: {successCount}/{steps.Length} の手順が成功しました。");
            
            // 接続テスト（簡易版）
            Console.WriteLine("\n接続テストを実行しています...");
            var connectedSsid = await NetworkUtils.GetCurrentConnectedSSIDAsync();
            
            if (!string.IsNullOrEmpty(connectedSsid))
            {
                Console.WriteLine($"✅ {connectedSsid} に接続されています");
            }
            else
            {
                Console.WriteLine("❌ WiFi接続に問題があります");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"修復エラー: {ex.Message}");
        }
    }
}

