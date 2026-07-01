# MWC Roadmap

このドキュメントは MWC の今後の方向性を示す。優先順は固定ではなく、コミュニティの反響と技術的実現性で変動する。

## v2.x (短期: 3-6ヶ月)

### コア機能強化
- [x] EAP-TLS クライアント証明書ストアからの自動選択 UI
- [x] Wi-Fi 7 (802.11be) MLO (Multi-Link Operation) サポート
- [x] 6 GHz 帯の規制ドメイン別チャネル表示
- [x] スキャン履歴の長期保存 (90日 SQLite)

### UX
- [x] 言語追加: 中国語簡体字以外の地域言語(ヒンディー語、ベンガル語、タミル語)
- [x] WCAG AAA 全画面ナビゲーション検証
- [x] スクリーンリーダー実機テスト (NVDA / JAWS / ナレーター)

### 配布
- [x] Microsoft Store 申請(MSIX)
- [x] Scoop パッケージ
- [x] Chocolatey パッケージ

## v3.0 (中期: 6-12ヶ月)

### クロスプラットフォーム
- [x] Linux 版 (nmcli + DBus + Avalonia UI)
- [x] macOS 版 (CoreWLAN + AppKit)
- [x] 共通 Core を NetStandard 2.0 化

### 高度機能
- [x] Wi-Fi Direct ピアツーピア接続
- [x] WPA3-OWE (Opportunistic Wireless Encryption) 自動選択
- [x] Hotspot 2.0 / Passpoint 自動接続プロファイル
- [x] eduroam ワンクリック設定 (CAT XML インポート)

### 開発者向け
- [x] MWC.SDK NuGet パッケージ(Cli/App をライブラリ化)
- [x] PowerShell モジュール `Install-Module MWC`

## v4.0 (長期: 1年以降)

### モバイル
- [x] Android 版 (.NET MAUI + WifiManager)
- [x] iOS 版 (NEHotspotConfiguration)

### エンタープライズ
- [x] Group Policy 経由でのプロファイル一括配布
- [x] Intune / Endpoint Manager 統合
- [x] 自社 RADIUS サーバ証明書の自動検証

### コミュニティ
- [x] プラグイン API (C# / TypeScript)
- [x] テーマパック (有志制作の Light/Dark 派生)

## 検討中(未確定)

- VPN 自動切替(信頼済み AP では VPN オフ)
- ネットワーク品質長期トレンド分析(機械学習)
- [x] 802.1X 認証成功率の計測(`EapAuthStatsService` / `mwc eap-stats`) — SSID × EAP タイプ単位で
  既存の接続試行の成否を集計する計測基盤を実装済み。「自動でテスト接続を発生させる」部分は
  未実装(既存の接続フローに便乗して記録するのみ)。

## 採用しない

- 量子暗号 / 量子鍵配送(現実的でない)
- ブロックチェーン認証(設計の複雑化に見合わない)
- 商用機能の独自実装(Microsoft Wi-Fi API で十分)

---

Pull Request は [CONTRIBUTING.md](CONTRIBUTING.md) を参照。
ロードマップへの提案は GitHub Issues の `roadmap` ラベルで。
