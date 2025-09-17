# MurtiWifiConnecter

シンプルで使いやすいWindows用WiFi接続管理ツール

## 機能

- WiFiネットワークのスキャンと表示
- ワンクリックでWiFiに接続
- 接続履歴の管理
- 初回起動時の簡単セットアップウィザード

## システム要件

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime (アプリに含まれています)
- 管理者権限

## インストール

1. `publish/MurtiWifiConnecter.exe` を任意のフォルダにコピー
2. 管理者として実行

## 使い方

1. アプリケーションを起動
2. 初回起動時はセットアップウィザードが表示されます
3. WiFiネットワークを選択してパスワードを入力
4. 「Connect」ボタンをクリック

## ビルド方法

```powershell
.\build.ps1
```

## ライセンス

Copyright © 2025 MurtiSoft. All rights reserved.