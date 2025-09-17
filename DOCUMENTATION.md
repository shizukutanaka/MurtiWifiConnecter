# MurtiWifi Connector - 完全ガイド

## 📋 目次

1. [概要](#概要)
2. [システム要件](#システム要件)
3. [インストール](#インストール)
4. [初期設定](#初期設定)
5. [基本的な使い方](#基本的な使い方)
6. [高度な機能](#高度な機能)
7. [トラブルシューティング](#トラブルシューティング)
8. [開発者向け情報](#開発者向け情報)
9. [FAQ](#faq)

---

## 概要

MurtiWifi Connectorは、Windows向けの高機能WiFi管理ツールです。個人・家庭利用に最適化されており、シンプルな操作で高度なネットワーク管理を実現します。

### 主な特徴

- 🚀 **高速接続**: ワンクリックでWiFiネットワークに接続
- 🔄 **自動再接続**: 接続が切れた場合の自動復旧
- 🔋 **バッテリー最適化**: 電源状態に応じた動作モード切替
- 📊 **詳細な分析**: 接続履歴と統計情報の表示
- 🛡️ **セキュリティ**: パスワードの安全な管理
- 🎯 **軽量設計**: メモリ使用量50MB以下

---

## システム要件

### 最小要件
- **OS**: Windows 10 バージョン 1809以降
- **CPU**: 1GHz以上のプロセッサ
- **メモリ**: 2GB RAM
- **ストレージ**: 100MB以上の空き容量
- **ネットワーク**: WiFiアダプター必須

### 推奨要件
- **OS**: Windows 11
- **CPU**: デュアルコア 2GHz以上
- **メモリ**: 4GB RAM以上
- **ストレージ**: 500MB以上の空き容量
- **.NET**: .NET 6.0以降

### 必要なソフトウェア
- .NET Desktop Runtime 6.0以降
- Windows WiFiドライバー（最新版推奨）

---

## インストール

### インストーラーを使用する場合

1. **ダウンロード**
   - 最新版の`MurtiWifiConnector_Setup_vX.X.X.exe`をダウンロード

2. **インストール実行**
   ```
   1. インストーラーをダブルクリック
   2. 言語選択（日本語/英語）
   3. インストール先を選択（デフォルト推奨）
   4. 追加タスクを選択：
      - デスクトップアイコン作成
      - Windows起動時の自動開始
   5. 「インストール」をクリック
   ```

3. **初回起動**
   - インストール完了後、自動的に初期設定ウィザードが起動

### ポータブル版を使用する場合

1. **ZIPファイルをダウンロード**
   ```
   MurtiWifiConnector_vX.X.X_x64.zip
   ```

2. **展開**
   ```powershell
   # 任意のフォルダに展開
   Expand-Archive -Path "MurtiWifiConnector_vX.X.X_x64.zip" -DestinationPath "C:\Tools\MurtiWifi"
   ```

3. **実行**
   ```powershell
   # 管理者権限で実行
   Start-Process "C:\Tools\MurtiWifi\MurtiWifiConnecter.exe" -Verb RunAs
   ```

---

## 初期設定

### 設定ウィザード

初回起動時に表示される設定ウィザードで、以下を設定します：

#### ステップ1: ユーザー情報
- **ユーザー名**: 表示名（例：太郎、お父さん）
- **使用形態**:
  - 個人利用（デフォルト）
  - 家族利用（複数ユーザー）
  - お子様利用（制限モード）

#### ステップ2: ホームネットワーク
- **SSID**: 自宅のWiFiネットワーク名
- **表示名**: わかりやすい名前（例：リビングのWiFi）

#### ステップ3: 確認
- 設定内容の確認
- バッテリー最適化の自動有効化

### 設定ファイル

設定は以下の場所に保存されます：
```
%APPDATA%\MurtiWifiConnector\config.json
```

#### 設定例
```json
{
  "version": "2.0.0",
  "user": {
    "name": "太郎",
    "mode": "Personal"
  },
  "network": {
    "autoConnect": true,
    "scanInterval": 15,
    "preferredNetworks": [
      {
        "ssid": "MyHomeWiFi",
        "displayName": "自宅",
        "priority": 1
      }
    ]
  },
  "performance": {
    "lightweightMode": true,
    "batteryOptimization": true,
    "maxMemoryMB": 50
  },
  "logging": {
    "enabled": true,
    "level": "Info",
    "maxSizeMB": 10
  }
}
```

---

## 基本的な使い方

### メイン画面

#### 1. ネットワーク一覧
- 利用可能なWiFiネットワークがリスト表示
- 信号強度、セキュリティタイプを表示
- クリックで詳細情報表示

#### 2. 接続操作
```
1. ネットワークを選択
2. パスワードを入力
3. 「接続」ボタンをクリック
```

#### 3. クイックアクション
- **更新**: ネットワークを再スキャン（F5キー）
- **切断**: 現在の接続を切断（Ctrl+D）
- **診断**: ネットワーク診断を実行（F12キー）

### コマンドラインインターフェース

```powershell
# ヘルプ表示
MurtiWifiConnecter.exe --help

# CLIモードで起動
MurtiWifiConnecter.exe --cli

# 特定のネットワークに接続
MurtiWifiConnecter.exe --connect "MyWiFi" --password "password123"

# ネットワークスキャン
MurtiWifiConnecter.exe --scan

# 現在の接続状態表示
MurtiWifiConnecter.exe --status

# 診断実行
MurtiWifiConnecter.exe --diagnose
```

---

## 高度な機能

### 自動接続管理

#### 優先ネットワーク設定
1. 設定 → ネットワーク → 優先ネットワーク
2. ネットワークをドラッグ&ドロップで順序変更
3. 自動接続の有効/無効を設定

#### 接続ルール
```
- 信号強度60%以上で自動接続
- 5回失敗したネットワークは30分間無視
- バッテリー20%以下では新規スキャン停止
```

### バッテリー最適化

#### 電源モード
| モード | バッテリー | 動作 |
|--------|-----------|------|
| 高性能 | AC電源/80%以上 | フル機能 |
| バランス | 50-80% | 標準動作 |
| 省電力 | 20-50% | スキャン間隔延長 |
| 超省電力 | 20%以下 | 最小限動作 |

#### 設定方法
```
設定 → パフォーマンス → バッテリー最適化
- 自動モード切替: ON/OFF
- カスタム閾値設定
```

### 接続分析

#### ダッシュボード
- 今日の接続数
- 週間成功率
- よく使うネットワークTOP5
- 時間帯別使用状況

#### レポート生成
```
分析 → レポート生成
- 期間選択（7日/30日/カスタム）
- 形式選択（PDF/Excel/CSV）
- メール送信オプション
```

### セキュリティ機能

#### パスワード管理
- Windows資格情報マネージャー統合
- AES-256暗号化
- マスターパスワード保護（オプション）

#### ネットワーク検証
```
- 証明書チェック
- DNSスプーフィング検出
- 悪意のあるAPの警告
```

---

## トラブルシューティング

### よくある問題と解決方法

#### 1. WiFiアダプターが認識されない
```powershell
# 管理者権限のPowerShellで実行
# アダプターリセット
netsh wlan set hostednetwork mode=disallow
netsh wlan set hostednetwork mode=allow

# ドライバー更新
Get-PnpDevice -Class Net | Where-Object {$_.FriendlyName -like "*Wi-Fi*"} | Update-PnpDevice
```

#### 2. 接続できない
```
1. ネットワーク診断を実行
   - ツール → 診断 → ネットワーク診断
   
2. プロファイルをリセット
   - 設定 → ネットワーク → プロファイル管理 → リセット
   
3. WiFiサービス再起動
   - ツール → サービス → WiFiサービス再起動
```

#### 3. パフォーマンス問題
```
1. キャッシュクリア
   - ツール → メンテナンス → キャッシュクリア
   
2. 軽量モード有効化
   - 設定 → パフォーマンス → 軽量モード: ON
   
3. ログレベル変更
   - 設定 → ログ → レベル: Error のみ
```

### エラーコード一覧

| コード | 説明 | 対処法 |
|--------|------|--------|
| E001 | WiFiアダプター無効 | アダプターを有効化 |
| E002 | 認証失敗 | パスワード確認 |
| E003 | タイムアウト | ネットワーク範囲確認 |
| E004 | プロファイル作成失敗 | 管理者権限で実行 |
| E005 | サービス停止 | WiFiサービス再起動 |

### ログファイル

ログは以下の場所に保存：
```
%APPDATA%\MurtiWifiConnector\logs\
```

ログレベル：
- **Error**: エラーのみ
- **Warning**: 警告以上
- **Info**: 情報以上（デフォルト）
- **Debug**: すべて（開発用）

---

## 開発者向け情報

### アーキテクチャ

```
MurtiWifiConnector/
├── Core/               # コア機能
│   ├── WifiOperations  # WiFi操作
│   ├── NetworkScanning # スキャン
│   └── ProcessExecutor # プロセス実行
├── Personal/           # 個人向け機能
│   ├── PersonalWifiSystem
│   └── BatteryAwareManager
└── UI/                 # ユーザーインターフェース
    ├── MainWindow
    └── QuickSetupWizard
```

### ビルド方法

#### 必要なツール
- Visual Studio 2022以降
- .NET SDK 6.0以降
- Windows SDK

#### ビルド手順
```powershell
# リポジトリクローン
git clone https://github.com/yourusername/MurtiWifiConnector.git
cd MurtiWifiConnector

# 依存関係復元
dotnet restore

# ビルド
dotnet build --configuration Release

# テスト実行
dotnet test

# パッケージ作成
.\Deployment\build.ps1 -Configuration Release -CreateInstaller
```

### API リファレンス

#### IWifiService
```csharp
public interface IWifiService
{
    Task<Result<WifiConnectionResult>> ConnectAsync(string ssid, string password, CancellationToken ct = default);
    Task<Result<bool>> DisconnectAsync(CancellationToken ct = default);
    Task<Result<string>> GetCurrentSSIDAsync(CancellationToken ct = default);
}
```

#### INetworkScanner
```csharp
public interface INetworkScanner
{
    Task<Result<NetworkInfo[]>> ScanAsync(CancellationToken ct = default);
}
```

### プラグイン開発

プラグインインターフェース：
```csharp
public interface IWifiPlugin
{
    string Name { get; }
    string Version { get; }
    Task InitializeAsync();
    Task<object> ExecuteAsync(string command, object parameters);
}
```

---

## FAQ

### Q: 管理者権限は必要ですか？
**A**: はい、WiFi設定の変更には管理者権限が必要です。

### Q: 複数のWiFiアダプターに対応していますか？
**A**: はい、設定で優先アダプターを選択できます。

### Q: VPNと併用できますか？
**A**: はい、VPN接続には影響しません。

### Q: Windows 7/8で動作しますか？
**A**: いいえ、Windows 10以降のみサポートしています。

### Q: ポータブル版とインストーラー版の違いは？
**A**: 機能は同じです。ポータブル版はレジストリを使用しません。

### Q: 接続履歴はどのくらい保存されますか？
**A**: デフォルトで90日間、設定で変更可能です。

### Q: 自動アップデートはありますか？
**A**: 次期バージョンで実装予定です。

---

## サポート

### 問い合わせ先
- GitHub Issues: [https://github.com/yourusername/MurtiWifiConnector/issues](https://github.com/yourusername/MurtiWifiConnector/issues)
- Email: support@murtisoftware.com

### ライセンス
MIT License - 詳細はLICENSEファイルを参照

### 貢献
プルリクエスト歓迎です！CONTRIBUTINGガイドラインを確認してください。

---

*最終更新: 2024年*