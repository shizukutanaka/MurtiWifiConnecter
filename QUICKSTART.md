# MurtiWifi Connector - クイックスタートガイド

実用的で軽量なWiFi接続管理ツール

## 基本的な使用方法

### 1. 簡単な接続
```csharp
// WiFiネットワークに接続
var connected = await Services.Quick.ConnectAsync("MyWiFi", "password123");
Console.WriteLine($"接続結果: {(connected ? "成功" : "失敗")}");
```

### 2. ネットワークスキャン
```csharp
// 利用可能なネットワークをスキャン
var networks = await Services.Quick.ScanAsync();
foreach (var network in networks)
{
    Console.WriteLine($"  - {network}");
}
```

### 3. 現在の接続確認
```csharp
// 現在接続中のネットワークを取得
var current = await Services.Quick.GetCurrentNetworkAsync();
Console.WriteLine($"現在の接続: {current}");
```

### 4. 切断
```csharp
// WiFi接続を切断
var disconnected = await Services.Quick.DisconnectAsync();
Console.WriteLine($"切断結果: {(disconnected ? "成功" : "失敗")}");
```

## 高度な使用方法

### 優先度付き自動接続
```csharp
// 優先順位を付けたネットワークリスト
var priorityNetworks = new[] { "HomeWiFi", "OfficeWiFi", "MobileHotspot" };
var passwords = new Dictionary<string, string>
{
    { "HomeWiFi", "home_password" },
    { "OfficeWiFi", "office_password" },
    { "MobileHotspot", "mobile_password" }
};

// 利用可能な最初のネットワークに自動接続
var result = await PracticalWifiHelper.AutoConnectWithFallbackAsync(priorityNetworks, passwords);
Console.WriteLine($"自動接続結果: {result}");
```

### ネットワーク診断
```csharp
// 簡単なネットワーク診断を実行
var diagnostics = await PracticalWifiHelper.RunQuickDiagnosticsAsync();
Console.WriteLine("ネットワーク診断:");
Console.WriteLine(diagnostics);
```

### ネットワーク切り替え
```csharp
// 現在のネットワークから別のネットワークに切り替え
var switched = await PracticalWifiHelper.SwitchToNetworkAsync("NewNetwork", "new_password");
Console.WriteLine($"切り替え結果: {(switched ? "成功" : "失敗")}");
```

### ネットワーク可用性チェック
```csharp
// 特定のネットワークが利用可能かチェック
var available = await PracticalWifiHelper.IsNetworkAvailableAsync("HomeWiFi");
Console.WriteLine($"HomeWiFi 利用可能: {available}");

// 信号強度を取得
if (available)
{
    var strength = await PracticalWifiHelper.GetNetworkStrengthAsync("HomeWiFi");
    Console.WriteLine($"信号強度: {strength}");
}
```

## CLI使用方法

### 基本コマンド
```bash
# CLIモードで起動
MurtiWifiConnecter.exe --cli

# または
MurtiWifiConnecter.exe -c
```

### CLIメニュー
1. ネットワークをスキャン
2. WiFiに接続
3. 現在の接続状態を表示
4. プロファイル管理
5. 自動接続設定
6. システム情報
7. パスワードツール

## プログラムから使用する場合

### 例1: 基本的な使用
```csharp
// プログラムのMain関数から使用
public static async Task Main(string[] args)
{
    // クイックテストを実行
    await QuickStartExample.QuickTest();

    // すべてのサンプルを実行
    await QuickStartExample.RunAllExamples();
}
```

### 例2: エラーハンドリング付き
```csharp
public static async Task ConnectSafely(string ssid, string password)
{
    try
    {
        var connected = await Services.Quick.ConnectAsync(ssid, password);
        if (connected)
        {
            Console.WriteLine($"✓ {ssid} に接続しました");
        }
        else
        {
            Console.WriteLine($"✗ {ssid} への接続に失敗しました");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"エラー: {ex.Message}");
    }
}
```

## システム要件

- Windows 10/11
- .NET 6.0 以上
- 管理者権限（推奨）

## トラブルシューティング

### よくある問題

1. **接続に失敗する**
   - 管理者権限で実行しているか確認
   - パスワードが正しいか確認
   - ネットワークが利用可能か確認

2. **ネットワークが見つからない**
   - WiFiアダプターが有効か確認
   - ネットワークの電波範囲内にいるか確認

3. **メモリ使用量が多い**
   ```csharp
   // メモリ最適化を実行
   CoreOptimizations.EmergencyCleanup();
   ```

### 診断ツール
```csharp
// システム状態をチェック
var memory = CoreOptimizations.GetMemoryUsageMB();
var pressure = CoreOptimizations.IsMemoryPressure();

Console.WriteLine($"メモリ使用量: {memory}MB");
Console.WriteLine($"メモリ圧迫: {(pressure ? "あり" : "なし")}");
```

## ライセンス

個人・家庭利用向けの実用的なWiFi管理ツールです。