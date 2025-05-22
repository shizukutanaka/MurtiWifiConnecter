# Murti WiFi Connector

複数のWiFiアダプタを搭載したWindows PCで、各アダプタごとに異なるWiFiネットワークに接続するためのユーティリティアプリケーションです。

## 特徴

- 複数のWiFiアダプタを個別に管理
- 各アダプタごとに接続先ネットワークを選択可能
- シグナル強度の可視化
- ネットワークの優先順位設定
- 多言語対応（日本語/英語/他主要言語）

## 必要条件

- Windows 11 バージョン 21H2 以降
- .NET 7.0 以上のランタイム（推奨: .NET 8.0）
- 管理者権限

## インストール方法

1. [Releases](https://github.com/yourusername/MurtiWifiConnecter/releases) ページから最新のインストーラーをダウンロード
2. インストーラーを実行し、画面の指示に従ってインストール

## 使い方

1. アプリケーションを管理者権限で起動
2. 画面上部のドロップダウンからWiFiアダプタを選択
3. 検出されたWiFiネットワークの一覧から接続したいネットワークを選択
4. 必要に応じてパスワードを入力し、「接続」ボタンをクリック

## ビルド方法

1. リポジトリをクローン
   ```
   git clone https://github.com/yourusername/MurtiWifiConnecter.git
   ```
2. ソリューションを開く
   ```
   cd MurtiWifiConnecter
   start MurtiWifiConnecter.sln
   ```
3. Visual Studio 2022 でビルドを実行

## ライセンス

このプロジェクトはMITライセンスの下で公開されています。詳細は[LICENSE](LICENSE)ファイルを参照してください。

## 貢献方法

1. リポジトリをフォーク
2. 機能ブランチを作成 (`git checkout -b feature/AmazingFeature`)
3. 変更をコミット (`git commit -m 'Add some AmazingFeature'`)
4. ブランチにプッシュ (`git push origin feature/AmazingFeature`)
5. プルリクエストを開く

## 作者
Shizuku Tanaka

---

## English Summary

Murti WiFi Connector is a utility application for Windows PCs with multiple WiFi adapters. It allows users to manage each adapter independently and connect to different WiFi networks per adapter. Features include network prioritization, signal strength visualization, and multilingual support (Japanese, English, and more).

---

## ライセンス

このプロジェクトはMITライセンスの下で公開されています。詳細は[LICENSE](LICENSE)ファイルを参照してください。