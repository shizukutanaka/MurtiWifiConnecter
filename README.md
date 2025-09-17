# MurtiWifiConnecter

Simple and efficient WiFi connection manager for Windows.

## Features

- Quick WiFi network scanning
- Easy connection management
- Auto-reconnect capability
- Saved network profiles
- Clean and intuitive UI
- Lightweight and fast (30-50MB memory)

## Requirements

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime
- Administrator privileges (for WiFi operations)
- WiFi adapter

## インストール・使用方法

1. 最新リリースをダウンロード
2. `MurtiWifiConnecter.exe` を管理者権限で実行
3. ネットワークリストから接続先を選択
4. パスワード入力（セキュアネットワークの場合）
5. 接続ボタンをクリック
6. システムトレイからの操作も可能

## 使用方法

### 基本操作
1. アプリケーションを起動
2. 「更新」ボタンでネットワークをスキャン
3. 接続したいネットワークを選択
4. パスワードを入力（必要な場合）
5. 「接続」ボタンをクリック

### システムトレイ操作
- ダブルクリック: メイン画面を開く
- 右クリック: 設定・状態確認・終了

### コマンドライン使用
```bash
# CLIモード起動
MurtiWifiConnecter.exe --cli

# 基本コマンド
s - 状況確認
f - 問題自動修復
h - 接続状態表示
```

## トラブルシューティング

### 接続できない場合
1. 管理者権限で再起動
2. WiFiアダプターの確認
3. パスワードの再確認

### メモリ使用量が多い場合
- アプリケーションを再起動
- 他のアプリケーションを終了

## ファイル構成

```
MurtiWifiConnecter/
├── Core/                    # 基本機能
│   ├── WifiOperations.cs    # WiFi操作
│   ├── NetworkScanning.cs   # ネットワークスキャン
│   ├── MemoryOptimizer.cs   # メモリ最適化
│   └── StabilityEnhancements.cs # 安定性強化
├── Personal/               # 個人利用向け機能
├── UI/                     # ユーザーインターフェース
└── MainWindow.xaml(.cs)    # メインウィンドウ
```

## 特徴

### 実用性重視
- 余計な機能を排除した軽量設計
- 個人ユーザーに必要な機能に特化
- 直感的で分かりやすいインターフェース

### 高性能
- オブジェクトプーリングによるメモリ効率化
- 文字列処理の最適化
- バッチUI更新による滑らかな操作

### 高信頼性
- 接続失敗時の自動復旧
- メモリ圧迫時の緊急クリーンアップ
- 例外処理の完全統合

## ライセンス

MIT License

## 開発方針

このアプリケーションは実用性と軽量性を最優先に設計されています。
個人ユーザーにとって本当に必要な機能のみを実装し、
過度に複雑な機能は意図的に除外しています。