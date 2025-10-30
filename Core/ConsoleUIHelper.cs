using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// コンソールベースのユーザーインターフェースヘルパー
    /// インタラクティブメニュー、プログレスバー、色付き出力などを提供
    /// </summary>
    public static class ConsoleUIHelper
    {
        private static readonly object _consoleLock = new();
        private static bool _colorsSupported;

        static ConsoleUIHelper()
        {
            // 色付き出力のサポートチェック
            try
            {
                _colorsSupported = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") == null;
            }
            catch
            {
                _colorsSupported = false;
            }
        }

        /// <summary>
        /// メインインタラクティブメニューを表示
        /// </summary>
        public static async Task ShowMainMenuAsync(CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested)
            {
                Console.Clear();
                ShowHeader();

                var options = new[]
                {
                    new MenuOption("1", "WiFiネットワーク管理", ShowWifiMenuAsync),
                    new MenuOption("2", "VPN接続管理", ShowVpnMenuAsync),
                    new MenuOption("3", "ネットワーク診断", ShowDiagnosticsMenuAsync),
                    new MenuOption("4", "バックアップ管理", ShowBackupMenuAsync),
                    new MenuOption("5", "設定管理", ShowSettingsMenuAsync),
                    new MenuOption("6", "システム情報", ShowSystemInfoAsync),
                    new MenuOption("0", "終了", () => Task.CompletedTask)
                };

                DisplayMenu("MurtiWifiConnecter メイン メニュー", options);

                var choice = ReadUserChoice();
                var selectedOption = options.FirstOrDefault(o => o.Key == choice);

                if (selectedOption == null)
                {
                    ShowError("無効な選択です。再度入力してください。");
                    await Task.Delay(1500, ct);
                    continue;
                }

                if (selectedOption.Key == "0")
                {
                    ShowInfo("アプリケーションを終了します。");
                    break;
                }

                try
                {
                    Console.Clear();
                    await selectedOption.Action();
                }
                catch (Exception ex)
                {
                    ShowError($"操作中にエラーが発生しました: {ex.Message}");
                    await Logger.LogError($"メニュー操作エラー: {ex.Message}", nameof(ConsoleUIHelper), null, ex);
                }

                if (!ct.IsCancellationRequested)
                {
                    ShowInfo("メインメニューに戻るにはEnterキーを押してください...");
                    Console.ReadLine();
                }
            }
        }

        /// <summary>
        /// WiFi管理メニュー
        /// </summary>
        private static async Task ShowWifiMenuAsync()
        {
            var options = new[]
            {
                new MenuOption("1", "利用可能なネットワークをスキャン", ScanNetworksAsync),
                new MenuOption("2", "ネットワークに接続", ConnectToNetworkAsync),
                new MenuOption("3", "現在の接続を切断", DisconnectNetworkAsync),
                new MenuOption("4", "接続状況を表示", ShowConnectionStatusAsync),
                new MenuOption("5", "保存済みプロファイルを表示", ShowSavedProfilesAsync),
                new MenuOption("6", "優先ネットワークを管理", ManagePreferredNetworksAsync),
                new MenuOption("7", "WiFi速度テスト", RunWifiSpeedTestAsync),
                new MenuOption("0", "戻る", () => Task.CompletedTask)
            };

            await ShowSubMenuAsync("WiFiネットワーク管理", options);
        }

        /// <summary>
        /// VPN管理メニュー
        /// </summary>
        private static async Task ShowVpnMenuAsync()
        {
            var options = new[]
            {
                new MenuOption("1", "利用可能なVPNプロバイダを表示", ShowVpnProvidersAsync),
                new MenuOption("2", "VPNプロファイルを作成", CreateVpnProfileAsync),
                new MenuOption("3", "VPNプロファイル一覧を表示", ListVpnProfilesAsync),
                new MenuOption("4", "VPN接続", ConnectVpnAsync),
                new MenuOption("5", "VPN切断", DisconnectVpnAsync),
                new MenuOption("6", "アクティブなVPN接続を表示", ShowActiveVpnConnectionsAsync),
                new MenuOption("7", "VPN速度テスト", TestVpnSpeedAsync),
                new MenuOption("0", "戻る", () => Task.CompletedTask)
            };

            await ShowSubMenuAsync("VPN接続管理", options);
        }

        /// <summary>
        /// 診断メニュー
        /// </summary>
        private static async Task ShowDiagnosticsMenuAsync()
        {
            var options = new[]
            {
                new MenuOption("1", "完全ネットワーク診断を実行", RunFullDiagnosticsAsync),
                new MenuOption("2", "WiFi診断を実行", RunWifiDiagnosticsAsync),
                new MenuOption("3", "接続テストを実行", RunConnectivityTestAsync),
                new MenuOption("4", "システム要件チェック", CheckSystemRequirementsAsync),
                new MenuOption("5", "パフォーマンス診断", RunPerformanceDiagnosticsAsync),
                new MenuOption("0", "戻る", () => Task.CompletedTask)
            };

            await ShowSubMenuAsync("ネットワーク診断", options);
        }

        /// <summary>
        /// バックアップメニュー
        /// </summary>
        private static async Task ShowBackupMenuAsync()
        {
            var options = new[]
            {
                new MenuOption("1", "完全バックアップを作成", CreateFullBackupAsync),
                new MenuOption("2", "設定のみバックアップを作成", CreateConfigBackupAsync),
                new MenuOption("3", "利用可能なバックアップを表示", ListBackupsAsync),
                new MenuOption("4", "バックアップから復元", RestoreFromBackupAsync),
                new MenuOption("5", "自動バックアップ設定", ConfigureAutoBackupAsync),
                new MenuOption("0", "戻る", () => Task.CompletedTask)
            };

            await ShowSubMenuAsync("バックアップ管理", options);
        }

        /// <summary>
        /// 設定メニュー
        /// </summary>
        private static async Task ShowSettingsMenuAsync()
        {
            var options = new[]
            {
                new MenuOption("1", "現在の設定を表示", ShowCurrentSettingsAsync),
                new MenuOption("2", "設定を変更", ModifySettingsAsync),
                new MenuOption("3", "設定をリセット", ResetSettingsAsync),
                new MenuOption("4", "設定を検証", ValidateSettingsAsync),
                new MenuOption("5", "設定をエクスポート", ExportSettingsAsync),
                new MenuOption("6", "設定をインポート", ImportSettingsAsync),
                new MenuOption("0", "戻る", () => Task.CompletedTask)
            };

            await ShowSubMenuAsync("設定管理", options);
        }

        // メニューアクションの実装
        private static async Task ScanNetworksAsync()
        {
            ShowInfo("ネットワークをスキャンしています...");

            using var progress = new ProgressIndicator("スキャン中");
            progress.Start();

            try
            {
                var networks = await NetworkOperations.GetAvailableNetworksAsync();

                progress.Stop();
                Console.WriteLine();

                if (networks.Count == 0)
                {
                    ShowWarning("利用可能なネットワークが見つかりませんでした。");
                    return;
                }

                ShowSuccess($"{networks.Count}個のネットワークが見つかりました:");
                Console.WriteLine();

                // ネットワーク一覧を表示
                var table = new ConsoleTable("SSID", "信号強度", "セキュリティ", "接続済み");
                foreach (var network in networks.OrderByDescending(n => n.SignalStrength))
                {
                    var connected = network.Ssid == (await NetworkOperations.GetCurrentConnectionAsync())?.Ssid ? "✓" : "";
                    table.AddRow(network.Ssid, $"{network.SignalStrength}%", network.Security, connected);
                }
                table.Display();

            }
            catch (Exception ex)
            {
                progress.Stop();
                ShowError($"ネットワークスキャンに失敗しました: {ex.Message}");
            }
        }

        private static async Task ConnectToNetworkAsync()
        {
            Console.Write("接続するネットワークのSSIDを入力してください: ");
            var ssid = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(ssid))
            {
                ShowError("SSIDが入力されていません。");
                return;
            }

            // 保存済みプロファイルのチェック
            var profiles = await NetworkOperations.GetSavedProfilesAsync();
            var profile = profiles.FirstOrDefault(p => p.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase));

            string? password = null;
            if (profile != null)
            {
                // 保存済みプロファイルを使用
                ShowInfo($"保存済みプロファイル '{profile.Ssid}' を使用します。");
            }
            else
            {
                // パスワード入力
                Console.Write("パスワードを入力してください: ");
                password = ReadPassword();
                if (string.IsNullOrEmpty(password))
                {
                    ShowError("パスワードが入力されていません。");
                    return;
                }
            }

            ShowInfo("接続しています...");

            using var progress = new ProgressIndicator("接続中");
            progress.Start();

            try
            {
                var result = await NetworkOperations.ConnectAsync(ssid, password);
                progress.Stop();

                if (result.Success)
                {
                    ShowSuccess($"ネットワーク '{ssid}' に正常に接続しました。");
                }
                else
                {
                    ShowError($"接続に失敗しました: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                progress.Stop();
                ShowError($"接続中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task DisconnectNetworkAsync()
        {
            ShowInfo("切断しています...");

            try
            {
                var result = await NetworkOperations.DisconnectAsync();
                if (result.Success)
                {
                    ShowSuccess("ネットワークから切断しました。");
                }
                else
                {
                    ShowError($"切断に失敗しました: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                ShowError($"切断中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ShowConnectionStatusAsync()
        {
            try
            {
                var connection = await NetworkOperations.GetCurrentConnectionAsync();

                if (connection == null)
                {
                    ShowWarning("現在接続されているネットワークはありません。");
                    return;
                }

                Console.WriteLine();
                ShowInfo("現在の接続状況:");
                Console.WriteLine();

                var table = new ConsoleTable("項目", "値");
                table.AddRow("SSID", connection.Ssid);
                table.AddRow("信号強度", $"{connection.SignalStrength}%");
                table.AddRow("セキュリティ", connection.Security);
                table.AddRow("接続時間", connection.ConnectedSince?.ToString("yyyy/MM/dd HH:mm:ss") ?? "不明");
                table.AddRow("IPアドレス", connection.IpAddress ?? "不明");
                table.AddRow("MACアドレス", connection.MacAddress ?? "不明");
                table.Display();

            }
            catch (Exception ex)
            {
                ShowError($"接続状況の取得に失敗しました: {ex.Message}");
            }
        }

        private static async Task ShowSavedProfilesAsync()
        {
            try
            {
                var profiles = await NetworkOperations.GetSavedProfilesAsync();

                if (profiles.Count == 0)
                {
                    ShowWarning("保存済みのプロファイルはありません。");
                    return;
                }

                ShowInfo($"保存済みプロファイル ({profiles.Count}件):");
                Console.WriteLine();

                var table = new ConsoleTable("SSID", "セキュリティ", "最終接続");
                foreach (var profile in profiles.OrderBy(p => p.Ssid))
                {
                    table.AddRow(profile.Ssid, profile.Security, profile.LastConnected?.ToString("yyyy/MM/dd HH:mm:ss") ?? "未接続");
                }
                table.Display();

            }
            catch (Exception ex)
            {
                ShowError($"プロファイル一覧の取得に失敗しました: {ex.Message}");
            }
        }

        private static async Task ManagePreferredNetworksAsync()
        {
            var subOptions = new[]
            {
                new MenuOption("1", "優先ネットワーク一覧を表示", ShowPreferredNetworksAsync),
                new MenuOption("2", "優先ネットワークを追加", AddPreferredNetworkAsync),
                new MenuOption("3", "優先ネットワークを削除", RemovePreferredNetworkAsync),
                new MenuOption("4", "優先ネットワークをクリア", ClearPreferredNetworksAsync),
                new MenuOption("0", "戻る", () => Task.CompletedTask)
            };

            await ShowSubMenuAsync("優先ネットワーク管理", subOptions);
        }

        private static async Task ShowPreferredNetworksAsync()
        {
            try
            {
                var networks = await ConfigManager.GetPreferredNetworks();

                if (networks.Count == 0)
                {
                    ShowWarning("優先ネットワークが設定されていません。");
                    return;
                }

                ShowInfo($"優先ネットワーク ({networks.Count}件):");
                Console.WriteLine();

                var table = new ConsoleTable("優先度", "SSID", "最終更新");
                foreach (var network in networks)
                {
                    table.AddRow(network.Priority.ToString(), network.Ssid, network.LastUpdated.ToString("yyyy/MM/dd HH:mm:ss"));
                }
                table.Display();

            }
            catch (Exception ex)
            {
                ShowError($"優先ネットワーク一覧の取得に失敗しました: {ex.Message}");
            }
        }

        private static async Task AddPreferredNetworkAsync()
        {
            Console.Write("追加するネットワークのSSIDを入力してください: ");
            var ssid = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(ssid))
            {
                ShowError("SSIDが入力されていません。");
                return;
            }

            Console.Write("優先度 (0-500、大きいほど優先度高、規定値: 0): ");
            var priorityInput = Console.ReadLine();
            var priority = 0;

            if (!string.IsNullOrEmpty(priorityInput) && !int.TryParse(priorityInput, out priority))
            {
                ShowError("優先度には数値を入力してください。");
                return;
            }

            try
            {
                var success = await ConfigManager.AddPreferredNetwork(ssid, priority);
                if (success)
                {
                    ShowSuccess($"優先ネットワーク '{ssid}' を追加しました（優先度: {priority}）。");
                }
                else
                {
                    ShowError("優先ネットワークの追加に失敗しました。");
                }
            }
            catch (Exception ex)
            {
                ShowError($"優先ネットワークの追加中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task RemovePreferredNetworkAsync()
        {
            Console.Write("削除するネットワークのSSIDを入力してください: ");
            var ssid = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(ssid))
            {
                ShowError("SSIDが入力されていません。");
                return;
            }

            try
            {
                var success = await ConfigManager.RemovePreferredNetwork(ssid);
                if (success)
                {
                    ShowSuccess($"優先ネットワーク '{ssid}' を削除しました。");
                }
                else
                {
                    ShowError($"'{ssid}' という優先ネットワークが見つかりません。");
                }
            }
            catch (Exception ex)
            {
                ShowError($"優先ネットワークの削除中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ClearPreferredNetworksAsync()
        {
            Console.Write("すべての優先ネットワークを削除します。よろしいですか？ (y/N): ");
            var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (confirm != "y" && confirm != "yes")
            {
                ShowInfo("キャンセルしました。");
                return;
            }

            try
            {
                var success = await ConfigManager.ClearPreferredNetworks();
                if (success)
                {
                    ShowSuccess("すべての優先ネットワークを削除しました。");
                }
                else
                {
                    ShowWarning("削除する優先ネットワークがありませんでした。");
                }
            }
            catch (Exception ex)
            {
                ShowError($"優先ネットワークのクリア中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task RunWifiSpeedTestAsync()
        {
            ShowInfo("WiFi速度テストを実行しています...");

            using var progress = new ProgressIndicator("テスト実行中");
            progress.Start();

            try
            {
                var result = await new EnhancedSpeedTest().PerformSpeedTestAsync();

                progress.Stop();
                Console.WriteLine();

                if (result.Success)
                {
                    ShowSuccess("速度テストが完了しました:");
                    Console.WriteLine();
                    Console.WriteLine($"ダウンロード速度: {result.DownloadSpeed:F2} Mbps");
                    Console.WriteLine($"アップロード速度: {result.UploadSpeed:F2} Mbps");
                    Console.WriteLine($"遅延: {result.Latency:F0} ms");
                }
                else
                {
                    ShowError($"速度テストに失敗しました: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                progress.Stop();
                ShowError($"速度テスト中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ShowVpnProvidersAsync()
        {
            ShowInfo("利用可能なVPNプロバイダーを確認しています...");

            try
            {
                var providers = await VpnManager.GetAvailableProvidersAsync();

                if (providers.Count == 0)
                {
                    ShowWarning("利用可能なVPNプロバイダーが見つかりませんでした。");
                    return;
                }

                ShowSuccess($"利用可能なVPNプロバイダー ({providers.Count}件):");
                Console.WriteLine();

                var table = new ConsoleTable("プロバイダー", "利用可能", "対応プロトコル");
                foreach (var provider in providers)
                {
                    table.AddRow(provider.Name, provider.IsAvailable ? "✓" : "✗", string.Join(", ", provider.SupportedProtocols));
                }
                table.Display();

            }
            catch (Exception ex)
            {
                ShowError($"VPNプロバイダー一覧の取得に失敗しました: {ex.Message}");
            }
        }

        private static async Task CreateVpnProfileAsync()
        {
            Console.Write("プロファイル名を入力してください: ");
            var name = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                ShowError("プロファイル名が入力されていません。");
                return;
            }

            var providers = await VpnManager.GetAvailableProvidersAsync();
            if (providers.Count == 0)
            {
                ShowError("利用可能なVPNプロバイダーがありません。");
                return;
            }

            Console.WriteLine("利用可能なプロバイダー:");
            for (int i = 0; i < providers.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {providers[i].Name}");
            }

            Console.Write("プロバイダー番号を選択してください: ");
            var providerChoice = Console.ReadLine();
            if (!int.TryParse(providerChoice, out var providerIndex) || providerIndex < 1 || providerIndex > providers.Count)
            {
                ShowError("無効なプロバイダー選択です。");
                return;
            }

            var selectedProvider = providers[providerIndex - 1];
            if (!selectedProvider.IsAvailable)
            {
                ShowError("選択したプロバイダーは利用できません。");
                return;
            }

            Console.Write("サーバーアドレスを入力してください: ");
            var server = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(server))
            {
                ShowError("サーバーアドレスが入力されていません。");
                return;
            }

            Console.Write("ポート番号を入力してください (規定値: 1194): ");
            var portInput = Console.ReadLine();
            var port = 1194;
            if (!string.IsNullOrEmpty(portInput) && !int.TryParse(portInput, out port))
            {
                ShowError("ポート番号には数値を入力してください。");
                return;
            }

            Console.Write("ユーザー名を入力してください (オプション): ");
            var username = Console.ReadLine()?.Trim();

            string? password = null;
            if (!string.IsNullOrEmpty(username))
            {
                Console.Write("パスワードを入力してください: ");
                password = ReadPassword();
            }

            try
            {
                var profile = new VpnConnectionProfile
                {
                    Name = name,
                    Provider = selectedProvider.Type,
                    Server = server,
                    Port = port,
                    Username = username ?? string.Empty,
                    Password = password ?? string.Empty
                };

                var success = await VpnManager.SaveProfileAsync(profile);
                if (success)
                {
                    ShowSuccess($"VPNプロファイル '{name}' を作成しました。");
                }
                else
                {
                    ShowError("VPNプロファイルの作成に失敗しました。");
                }
            }
            catch (Exception ex)
            {
                ShowError($"VPNプロファイル作成中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ListVpnProfilesAsync()
        {
            try
            {
                var profiles = await VpnManager.LoadProfilesAsync();

                if (profiles.Count == 0)
                {
                    ShowWarning("保存済みのVPNプロファイルはありません。");
                    return;
                }

                ShowInfo($"保存済みVPNプロファイル ({profiles.Count}件):");
                Console.WriteLine();

                var table = new ConsoleTable("名前", "プロバイダー", "サーバー", "ポート");
                foreach (var profile in profiles.Values.OrderBy(p => p.Name))
                {
                    table.AddRow(profile.Name, profile.Provider.ToString(), profile.Server, profile.Port.ToString());
                }
                table.Display();

            }
            catch (Exception ex)
            {
                ShowError($"VPNプロファイル一覧の取得に失敗しました: {ex.Message}");
            }
        }

        private static async Task ConnectVpnAsync()
        {
            var profiles = await VpnManager.LoadProfilesAsync();
            if (profiles.Count == 0)
            {
                ShowError("利用可能なVPNプロファイルがありません。先にプロファイルを作成してください。");
                return;
            }

            Console.WriteLine("利用可能なプロファイル:");
            var profileList = profiles.Values.OrderBy(p => p.Name).ToList();
            for (int i = 0; i < profileList.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {profileList[i].Name} ({profileList[i].Provider})");
            }

            Console.Write("接続するプロファイル番号を選択してください: ");
            var choice = Console.ReadLine();
            if (!int.TryParse(choice, out var profileIndex) || profileIndex < 1 || profileIndex > profileList.Count)
            {
                ShowError("無効なプロファイル選択です。");
                return;
            }

            var selectedProfile = profileList[profileIndex - 1];

            ShowInfo($"VPN '{selectedProfile.Name}' に接続しています...");

            using var progress = new ProgressIndicator("接続中");
            progress.Start();

            try
            {
                var result = await VpnManager.ConnectAsync(selectedProfile);
                progress.Stop();

                if (result.Success)
                {
                    ShowSuccess($"VPN '{selectedProfile.Name}' に正常に接続しました。");
                }
                else
                {
                    ShowError($"VPN接続に失敗しました: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                progress.Stop();
                ShowError($"VPN接続中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task DisconnectVpnAsync()
        {
            var activeConnections = VpnManager.GetActiveConnections();
            if (activeConnections.Count == 0)
            {
                ShowError("アクティブなVPN接続がありません。");
                return;
            }

            Console.WriteLine("アクティブな接続:");
            for (int i = 0; i < activeConnections.Count; i++)
            {
                var conn = activeConnections[i];
                Console.WriteLine($"  {i + 1}. {conn.Profile.Name} ({conn.Profile.Provider}) - 接続時間: {DateTime.Now - conn.ConnectedAt:hh\\:mm\\:ss}");
            }

            Console.Write("切断する接続番号を選択してください: ");
            var choice = Console.ReadLine();
            if (!int.TryParse(choice, out var connectionIndex) || connectionIndex < 1 || connectionIndex > activeConnections.Count)
            {
                ShowError("無効な接続選択です。");
                return;
            }

            var selectedConnection = activeConnections[connectionIndex - 1];

            ShowInfo($"VPN '{selectedConnection.Profile.Name}' を切断しています...");

            try
            {
                var success = await VpnManager.DisconnectAsync(selectedConnection.Profile.Id);
                if (success)
                {
                    ShowSuccess($"VPN '{selectedConnection.Profile.Name}' を切断しました。");
                }
                else
                {
                    ShowError("VPN切断に失敗しました。");
                }
            }
            catch (Exception ex)
            {
                ShowError($"VPN切断中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ShowActiveVpnConnectionsAsync()
        {
            var connections = VpnManager.GetActiveConnections();

            if (connections.Count == 0)
            {
                ShowWarning("アクティブなVPN接続はありません。");
                return;
            }

            ShowInfo($"アクティブなVPN接続 ({connections.Count}件):");
            Console.WriteLine();

            var table = new ConsoleTable("名前", "プロバイダー", "サーバー", "接続時間", "ステータス");
            foreach (var connection in connections)
            {
                var duration = DateTime.Now - connection.ConnectedAt;
                table.AddRow(
                    connection.Profile.Name,
                    connection.Profile.Provider.ToString(),
                    connection.Profile.Server,
                    $"{duration:hh\\:mm\\:ss}",
                    connection.Status.ToString()
                );
            }
            table.Display();
        }

        private static async Task TestVpnSpeedAsync()
        {
            var connections = VpnManager.GetActiveConnections();
            if (connections.Count == 0)
            {
                ShowError("アクティブなVPN接続がありません。");
                return;
            }

            if (connections.Count == 1)
            {
                await TestVpnSpeedForConnectionAsync(connections[0].Profile.Id);
                return;
            }

            Console.WriteLine("テストする接続を選択してください:");
            for (int i = 0; i < connections.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {connections[i].Profile.Name}");
            }

            Console.Write("接続番号: ");
            var choice = Console.ReadLine();
            if (!int.TryParse(choice, out var connectionIndex) || connectionIndex < 1 || connectionIndex > connections.Count)
            {
                ShowError("無効な選択です。");
                return;
            }

            await TestVpnSpeedForConnectionAsync(connections[connectionIndex - 1].Profile.Id);
        }

        private static async Task TestVpnSpeedForConnectionAsync(string connectionId)
        {
            ShowInfo("VPN速度テストを実行しています...");

            using var progress = new ProgressIndicator("テスト実行中");
            progress.Start();

            try
            {
                var result = await VpnManager.TestVpnSpeedAsync(connectionId);
                progress.Stop();
                Console.WriteLine();

                if (result.Success)
                {
                    ShowSuccess("VPN速度テストが完了しました:");
                    Console.WriteLine();
                    Console.WriteLine($"ダウンロード速度: {result.DownloadSpeed:F2} Mbps");
                    Console.WriteLine($"アップロード速度: {result.UploadSpeed:F2} Mbps");
                    Console.WriteLine($"遅延: {result.Latency:F0} ms");
                    Console.WriteLine($"テスト時刻: {result.Timestamp:yyyy/MM/dd HH:mm:ss}");
                }
                else
                {
                    ShowError($"VPN速度テストに失敗しました: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                progress.Stop();
                ShowError($"VPN速度テスト中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task RunFullDiagnosticsAsync()
        {
            ShowInfo("完全ネットワーク診断を実行しています...");
            ShowInfo("この処理には数分かかる場合があります。");

            using var progress = new ProgressIndicator("診断実行中");
            progress.Start();

            try
            {
                var report = await NetworkDiagnostics.PerformFullDiagnosticsAsync();
                progress.Stop();
                Console.WriteLine();

                if (report.Success)
                {
                    ShowSuccess($"診断が完了しました（総合スコア: {report.OverallScore}/100）");
                    Console.WriteLine();

                    ShowInfo("テスト結果:");
                    var resultTable = new ConsoleTable("テスト項目", "結果", "スコア", "詳細");
                    foreach (var test in report.Tests)
                    {
                        var status = test.Success ? "✓ 成功" : "✗ 失敗";
                        resultTable.AddRow(test.TestName, status, $"{test.Score}/100", test.Details);
                    }
                    resultTable.Display();

                    if (report.OverallScore >= 80)
                    {
                        ShowSuccess("ネットワーク状態は良好です。");
                    }
                    else if (report.OverallScore >= 60)
                    {
                        ShowWarning("ネットワークに軽微な問題があります。");
                    }
                    else
                    {
                        ShowError("ネットワークに重大な問題があります。");
                    }
                }
                else
                {
                    ShowError($"診断に失敗しました: {report.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                progress.Stop();
                ShowError($"診断中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task RunWifiDiagnosticsAsync()
        {
            ShowInfo("WiFi診断を実行しています...");

            try
            {
                var diagnostics = new NetworkDiagnostics();
                // WiFi固有のテストを実行
                var wifiTest = await diagnostics.TestWifiSpecificAsync();

                Console.WriteLine();
                Console.WriteLine($"WiFi診断結果: {(wifiTest.Success ? "✓ 正常" : "✗ 問題あり")}");
                Console.WriteLine($"スコア: {wifiTest.Score}/100");
                Console.WriteLine($"詳細: {wifiTest.Details}");

                if (wifiTest.Metrics != null && wifiTest.Metrics.ContainsKey("issues"))
                {
                    var issues = wifiTest.Metrics["issues"] as List<string>;
                    if (issues != null && issues.Any())
                    {
                        Console.WriteLine();
                        ShowWarning("検出された問題:");
                        foreach (var issue in issues)
                        {
                            Console.WriteLine($"  • {issue}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError($"WiFi診断中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task RunConnectivityTestAsync()
        {
            ShowInfo("接続テストを実行しています...");

            try
            {
                var diagnostics = new NetworkDiagnostics();
                var connectivityTest = await diagnostics.TestBasicConnectivityAsync();

                Console.WriteLine();
                Console.WriteLine($"接続テスト結果: {(connectivityTest.Success ? "✓ 成功" : "✗ 失敗")}");
                Console.WriteLine($"詳細: {connectivityTest.Details}");

                if (connectivityTest.Metrics != null)
                {
                    if (connectivityTest.Metrics.ContainsKey("roundTripTime"))
                    {
                        Console.WriteLine($"遅延: {connectivityTest.Metrics["roundTripTime"]}ms");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError($"接続テスト中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task CheckSystemRequirementsAsync()
        {
            ShowInfo("システム要件をチェックしています...");

            try
            {
                var success = await ErrorHandler.ValidateSystemRequirements();

                if (success)
                {
                    ShowSuccess("すべてのシステム要件を満たしています。");
                }
                else
                {
                    ShowError("一部のシステム要件を満たしていません。");
                }
            }
            catch (Exception ex)
            {
                ShowError($"システム要件チェック中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task RunPerformanceDiagnosticsAsync()
        {
            ShowInfo("パフォーマンス診断を実行しています...");

            try
            {
                var snapshot = PerformanceMonitor.TakeMemorySnapshot("PerformanceDiagnostics");

                Console.WriteLine();
                ShowInfo("パフォーマンス診断結果:");
                Console.WriteLine($"メモリ使用量: {snapshot.WorkingSet / 1024 / 1024} MB");
                Console.WriteLine($"CPU使用率: {snapshot.CpuUsage:F1}%");
                Console.WriteLine($"スレッド数: {snapshot.ThreadCount}");
                Console.WriteLine($"実行時間: {snapshot.Uptime.TotalMinutes:F1} 分");

                var warnings = PerformanceMonitor.CheckPerformanceWarnings();
                if (warnings.Any())
                {
                    Console.WriteLine();
                    ShowWarning("検出されたパフォーマンス警告:");
                    foreach (var warning in warnings)
                    {
                        Console.WriteLine($"  • {warning.Message}");
                    }
                }
                else
                {
                    ShowSuccess("パフォーマンスに問題はありません。");
                }
            }
            catch (Exception ex)
            {
                ShowError($"パフォーマンス診断中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task CreateFullBackupAsync()
        {
            ShowInfo("完全バックアップを作成しています...");

            using var progress = new ProgressIndicator("バックアップ作成中");
            progress.Start();

            try
            {
                var result = await BackupManager.CreateFullBackupAsync();
                progress.Stop();

                if (result.Success)
                {
                    ShowSuccess("完全バックアップが作成されました。");
                    Console.WriteLine($"バックアップファイル: {result.BackupPath}");
                    Console.WriteLine($"サイズ: {result.SizeBytes / 1024 / 1024:F1} MB");
                }
                else
                {
                    ShowError($"バックアップ作成に失敗しました: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                progress.Stop();
                ShowError($"バックアップ作成中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task CreateConfigBackupAsync()
        {
            ShowInfo("設定バックアップを作成しています...");

            using var progress = new ProgressIndicator("バックアップ作成中");
            progress.Start();

            try
            {
                var result = await BackupManager.CreateConfigBackupAsync();
                progress.Stop();

                if (result.Success)
                {
                    ShowSuccess("設定バックアップが作成されました。");
                    Console.WriteLine($"バックアップファイル: {result.BackupPath}");
                    Console.WriteLine($"サイズ: {result.SizeBytes / 1024:F1} KB");
                }
                else
                {
                    ShowError($"設定バックアップ作成に失敗しました: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                progress.Stop();
                ShowError($"設定バックアップ作成中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ListBackupsAsync()
        {
            try
            {
                var backups = BackupManager.GetAvailableBackups();

                if (backups.Count == 0)
                {
                    ShowWarning("利用可能なバックアップがありません。");
                    return;
                }

                ShowInfo($"利用可能なバックアップ ({backups.Count}件):");
                Console.WriteLine();

                var table = new ConsoleTable("ファイル名", "タイプ", "サイズ", "作成日時");
                foreach (var backup in backups)
                {
                    var size = backup.SizeBytes > 1024 * 1024
                        ? $"{backup.SizeBytes / 1024 / 1024:F1} MB"
                        : $"{backup.SizeBytes / 1024:F1} KB";
                    var type = backup.Metadata?.Type.ToString() ?? "不明";
                    table.AddRow(backup.FileName, type, size, backup.CreatedAt.ToString("yyyy/MM/dd HH:mm"));
                }
                table.Display();

            }
            catch (Exception ex)
            {
                ShowError($"バックアップ一覧の取得に失敗しました: {ex.Message}");
            }
        }

        private static async Task RestoreFromBackupAsync()
        {
            var backups = BackupManager.GetAvailableBackups();
            if (backups.Count == 0)
            {
                ShowError("利用可能なバックアップがありません。");
                return;
            }

            Console.WriteLine("利用可能なバックアップ:");
            for (int i = 0; i < backups.Count; i++)
            {
                var backup = backups[i];
                var size = backup.SizeBytes > 1024 * 1024
                    ? $"{backup.SizeBytes / 1024 / 1024:F1} MB"
                    : $"{backup.SizeBytes / 1024:F1} KB";
                Console.WriteLine($"  {i + 1}. {backup.FileName} ({size}) - {backup.CreatedAt:yyyy/MM/dd HH:mm}");
            }

            Console.Write("復元するバックアップ番号を選択してください: ");
            var choice = Console.ReadLine();
            if (!int.TryParse(choice, out var backupIndex) || backupIndex < 1 || backupIndex > backups.Count)
            {
                ShowError("無効なバックアップ選択です。");
                return;
            }

            var selectedBackup = backups[backupIndex - 1];

            Console.Write("復元オプションを選択してください:");
            Console.WriteLine("  1. すべて復元");
            Console.WriteLine("  2. 設定のみ復元");
            Console.WriteLine("  3. VPNプロファイルのみ復元");
            Console.Write("選択 (1-3): ");
            var optionChoice = Console.ReadLine();

            var restoreTypes = optionChoice switch
            {
                "1" => BackupManager.RestoreType.All,
                "2" => BackupManager.RestoreType.Config,
                "3" => BackupManager.RestoreType.VpnProfiles,
                _ => BackupManager.RestoreType.All
            };

            Console.Write("既存のファイルをバックアップしますか？ (y/N): ");
            var backupExisting = Console.ReadLine()?.Trim().ToLowerInvariant() == "y";

            var options = new BackupManager.RestoreOptions
            {
                RestoreTypes = restoreTypes,
                CreatePreRestoreBackup = true,
                BackupExistingFiles = backupExisting
            };

            ShowInfo("バックアップから復元しています...");

            using var progress = new ProgressIndicator("復元中");
            progress.Start();

            try
            {
                var result = await BackupManager.RestoreFromBackupAsync(selectedBackup.FilePath, options);
                progress.Stop();

                if (result.Success)
                {
                    ShowSuccess($"バックアップからの復元が完了しました。{result.RestoredFileCount}個のファイルを復元しました。");
                }
                else
                {
                    ShowError($"バックアップからの復元に失敗しました: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                progress.Stop();
                ShowError($"バックアップ復元中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ConfigureAutoBackupAsync()
        {
            Console.WriteLine("自動バックアップ設定:");
            Console.WriteLine("  1. 自動バックアップを有効化");
            Console.WriteLine("  2. 自動バックアップを無効化");
            Console.WriteLine("  3. 現在の設定を表示");
            Console.Write("選択: ");
            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    BackupManager.StartAutoBackup();
                    ShowSuccess("自動バックアップが有効化されました。");
                    break;
                case "2":
                    BackupManager.StopAutoBackup();
                    ShowSuccess("自動バックアップが無効化されました。");
                    break;
                case "3":
                    ShowInfo("自動バックアップは現在" + (BackupManager.IsAutoBackupEnabled() ? "有効" : "無効") + "です。");
                    break;
                default:
                    ShowError("無効な選択です。");
                    break;
            }
        }

        private static async Task ShowCurrentSettingsAsync()
        {
            try
            {
                await ConfigManager.DisplayCurrentConfig();
            }
            catch (Exception ex)
            {
                ShowError($"設定表示中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ModifySettingsAsync()
        {
            // 設定変更のインタラクティブUI（簡易版）
            ShowInfo("設定変更機能は準備中です。config.jsonファイルを直接編集してください。");
        }

        private static async Task ResetSettingsAsync()
        {
            Console.Write("設定をデフォルトにリセットします。よろしいですか？ (y/N): ");
            var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (confirm != "y" && confirm != "yes")
            {
                ShowInfo("キャンセルしました。");
                return;
            }

            try
            {
                // 実際のリセット処理はConfigManagerに実装する必要がある
                ShowWarning("設定リセット機能は未実装です。");
            }
            catch (Exception ex)
            {
                ShowError($"設定リセット中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ValidateSettingsAsync()
        {
            try
            {
                var success = await ConfigManager.ValidateConfig();
                if (success)
                {
                    ShowSuccess("設定は有効です。");
                }
                else
                {
                    ShowError("設定に問題があります。詳細は上記のメッセージを確認してください。");
                }
            }
            catch (Exception ex)
            {
                ShowError($"設定検証中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ExportSettingsAsync()
        {
            try
            {
                var json = await ConfigManager.GetSettingsMetadataJson();
                var exportPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"murtiwifi_settings_{DateTime.Now:yyyyMMddHHmmss}.json");

                await File.WriteAllTextAsync(exportPath, json);
                ShowSuccess($"設定をエクスポートしました: {exportPath}");
            }
            catch (Exception ex)
            {
                ShowError($"設定エクスポート中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ImportSettingsAsync()
        {
            Console.Write("インポートする設定ファイルのパスを入力してください: ");
            var filePath = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                ShowError("有効なファイルパスを入力してください。");
                return;
            }

            Console.Write("既存の設定をバックアップしますか？ (y/N): ");
            var backupExisting = Console.ReadLine()?.Trim().ToLowerInvariant() == "y";

            try
            {
                // 実際のインポート処理はConfigManagerに実装する必要がある
                ShowWarning("設定インポート機能は未実装です。");
            }
            catch (Exception ex)
            {
                ShowError($"設定インポート中にエラーが発生しました: {ex.Message}");
            }
        }

        private static async Task ShowSystemInfoAsync()
        {
            ShowInfo("システム情報を収集しています...");

            try
            {
                Console.WriteLine();
                ShowInfo("MurtiWifiConnecter システム情報:");
                Console.WriteLine();
                Console.WriteLine($"バージョン: {GetCurrentVersion()}");
                Console.WriteLine($".NETランタイム: {Environment.Version}");
                Console.WriteLine($"OS: {Environment.OSVersion}");
                Console.WriteLine($"プロセッサ数: {Environment.ProcessorCount}");
                Console.WriteLine($"システムメモリ: {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024} MB");

                var snapshot = PerformanceMonitor.TakeMemorySnapshot("SystemInfo");
                Console.WriteLine($"現在のメモリ使用量: {snapshot.WorkingSet / 1024 / 1024} MB");

                // WiFiアダプタ情報
                var wifiAdapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Count(ni => ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211);
                Console.WriteLine($"WiFiアダプタ数: {wifiAdapters}");

                // VPN接続情報
                var vpnConnections = VpnManager.GetActiveConnections().Count;
                Console.WriteLine($"アクティブなVPN接続: {vpnConnections}");

                // 設定ファイル情報
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MurtiWifiConnecter", "config.json");
                var configExists = File.Exists(configPath);
                Console.WriteLine($"設定ファイル: {(configExists ? "存在します" : "存在しません")}");

            }
            catch (Exception ex)
            {
                ShowError($"システム情報取得中にエラーが発生しました: {ex.Message}");
            }
        }

        // ヘルパーメソッド
        private static void ShowHeader()
        {
            lock (_consoleLock)
            {
                if (_colorsSupported)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                }

                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                           MurtiWifiConnecter                              ║");
                Console.WriteLine("║                        Enterprise WiFi Management                        ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");

                if (_colorsSupported)
                {
                    Console.ResetColor();
                }
            }
        }

        private static void DisplayMenu(string title, MenuOption[] options)
        {
            Console.WriteLine();
            ShowInfo(title);
            Console.WriteLine();

            foreach (var option in options)
            {
                Console.WriteLine($"  {option.Key}. {option.Description}");
            }
            Console.WriteLine();
        }

        private static async Task ShowSubMenuAsync(string title, MenuOption[] options)
        {
            while (true)
            {
                Console.Clear();
                ShowHeader();
                DisplayMenu(title, options);

                var choice = ReadUserChoice();
                var selectedOption = options.FirstOrDefault(o => o.Key == choice);

                if (selectedOption == null)
                {
                    ShowError("無効な選択です。再度入力してください。");
                    await Task.Delay(1500);
                    continue;
                }

                if (selectedOption.Key == "0")
                {
                    break;
                }

                try
                {
                    Console.Clear();
                    await selectedOption.Action();
                }
                catch (Exception ex)
                {
                    ShowError($"操作中にエラーが発生しました: {ex.Message}");
                    await Logger.LogError($"メニュー操作エラー: {ex.Message}", nameof(ConsoleUIHelper), null, ex);
                }

                Console.WriteLine();
                ShowInfo("メニューに戻るにはEnterキーを押してください...");
                Console.ReadLine();
            }
        }

        private static string ReadUserChoice()
        {
            Console.Write("選択してください: ");
            return Console.ReadLine()?.Trim() ?? "";
        }

        private static string ReadPassword()
        {
            var password = string.Empty;
            ConsoleKey key;

            do
            {
                var keyInfo = Console.ReadKey(intercept: true);
                key = keyInfo.Key;

                if (key == ConsoleKey.Backspace && password.Length > 0)
                {
                    Console.Write("\b \b");
                    password = password[0..^1];
                }
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    Console.Write("*");
                    password += keyInfo.KeyChar;
                }
            } while (key != ConsoleKey.Enter);

            Console.WriteLine();
            return password;
        }

        // 色付き出力メソッド
        private static void ShowSuccess(string message)
        {
            lock (_consoleLock)
            {
                if (_colorsSupported)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                }
                Console.WriteLine($"✓ {message}");
                if (_colorsSupported)
                {
                    Console.ResetColor();
                }
            }
        }

        private static void ShowError(string message)
        {
            lock (_consoleLock)
            {
                if (_colorsSupported)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                }
                Console.WriteLine($"✗ {message}");
                if (_colorsSupported)
                {
                    Console.ResetColor();
                }
            }
        }

        private static void ShowWarning(string message)
        {
            lock (_consoleLock)
            {
                if (_colorsSupported)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }
                Console.WriteLine($"⚠ {message}");
                if (_colorsSupported)
                {
                    Console.ResetColor();
                }
            }
        }

        private static void ShowInfo(string message)
        {
            lock (_consoleLock)
            {
                if (_colorsSupported)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                }
                Console.WriteLine($"ℹ {message}");
                if (_colorsSupported)
                {
                    Console.ResetColor();
                }
            }
        }

        // メニューオプションクラス
        private class MenuOption
        {
            public MenuOption(string key, string description, Func<Task> action)
            {
                Key = key;
                Description = description;
                Action = action;
            }

            public string Key { get; }
            public string Description { get; }
            public Func<Task> Action { get; }
        }

        // コンソールテーブル表示クラス
        private class ConsoleTable
        {
            private readonly List<string[]> _rows = new();
            private readonly string[] _headers;

            public ConsoleTable(params string[] headers)
            {
                _headers = headers;
            }

            public void AddRow(params string[] values)
            {
                _rows.Add(values);
            }

            public void Display()
            {
                if (!_rows.Any()) return;

                // 各列の最大幅を計算
                var columnWidths = new int[_headers.Length];
                for (int i = 0; i < _headers.Length; i++)
                {
                    columnWidths[i] = _headers[i].Length;
                }

                foreach (var row in _rows)
                {
                    for (int i = 0; i < row.Length && i < columnWidths.Length; i++)
                    {
                        columnWidths[i] = Math.Max(columnWidths[i], row[i].Length);
                    }
                }

                // ヘッダーを表示
                var headerLine = string.Join(" │ ", _headers.Select((h, i) => h.PadRight(columnWidths[i])));
                var separator = string.Join("─┼─", columnWidths.Select(w => new string('─', w)));

                Console.WriteLine(headerLine);
                Console.WriteLine(separator);

                // データを表示
                foreach (var row in _rows)
                {
                    var line = string.Join(" │ ", row.Select((cell, i) => cell.PadRight(columnWidths[i])));
                    Console.WriteLine(line);
                }
            }
        }

        // プログレスインジケータークラス
        private class ProgressIndicator : IDisposable
        {
            private readonly string _message;
            private readonly string[] _spinner = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
            private int _spinnerIndex;
            private bool _isRunning;
            private readonly Timer _timer;

            public ProgressIndicator(string message)
            {
                _message = message;
                _timer = new Timer(UpdateSpinner, null, Timeout.Infinite, 100);
            }

            public void Start()
            {
                _isRunning = true;
                _timer.Change(0, 100);
            }

            public void Stop()
            {
                _isRunning = false;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
            }

            private void UpdateSpinner(object? state)
            {
                if (!_isRunning) return;

                lock (_consoleLock)
                {
                    Console.Write($"\r{_spinner[_spinnerIndex]} {_message}");
                    _spinnerIndex = (_spinnerIndex + 1) % _spinner.Length;
                }
            }

            public void Dispose()
            {
                Stop();
                _timer.Dispose();
            }
        }

        private static string GetCurrentVersion()
        {
            // 実際のバージョン取得ロジック
            return "3.1.0";
        }
    }
}
