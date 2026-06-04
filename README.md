# MWC — Multi WiFi Connector

[![CI](https://github.com/shizukutanaka/MurtiWifiConnecter/actions/workflows/ci.yml/badge.svg)](https://github.com/shizukutanaka/MurtiWifiConnecter/actions/workflows/ci.yml)
[![CodeQL](https://github.com/shizukutanaka/MurtiWifiConnecter/actions/workflows/codeql.yml/badge.svg)](https://github.com/shizukutanaka/MurtiWifiConnecter/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-0078D6)](https://www.microsoft.com/windows)
[![Tests](https://img.shields.io/badge/tests-354%20passing-22C55E)](#)
[![i18n](https://img.shields.io/badge/i18n-15%20langs%20%C2%B7%20178%20keys-00C4CC)](#)

**MWC**は複数の無線アダプターをひとつの画面で管理する Windows 用 Wi-Fi ツール。

WPA3・Enterprise 接続・スキャン分析・QR コード生成・CLI を **すべて無料・MIT** で提供する初の Windows ツール。

---

## なぜ MWC か

| 機能 | Win標準 | WifiInfoView | NetSpot | inSSIDer | **MWC** |
|---|:---:|:---:|:---:|:---:|:---:|
| WPA3-SAE / Enterprise 接続 | △ | ❌ | ❌ | ❌ | **✅** |
| マルチアダプタータブ | ❌ | ❌ | △ | ❌ | **✅** |
| CLI (`mwc connect`) | ❌ | ❌ | ❌ | ❌ | **✅** |
| QR コード生成・パース | ❌ | ❌ | ❌ | ❌ | **✅** |
| 信号履歴グラフ | ❌ | ❌ | ✅ | ✅ | **✅** |
| チャンネル帯域グラフ | ❌ | ❌ | ✅ | ✅ | **✅** |
| MAC ベンダー表示 | ❌ | ✅ | ✅ | ✅ | **✅** |
| CSV/JSON エクスポート | ❌ | ✅ | ✅ | ✅ | **✅** |
| ネットワーク品質計測(RTT/Loss) | △ | ❌ | △ | △ | **✅** |
| 自動再接続 (Auto-Join) | △ | ❌ | ❌ | ❌ | **✅** |
| キャプティブポータル統合 | △ | ❌ | ❌ | ❌ | **✅** |
| Wi-Fi 7 (802.11be) 対応 | △ | ❌ | ✅ | ✅ | **✅** |
| 6 GHz バンド対応 | △ | ❌ | ✅ | ✅ | **✅** |
| Light/Dark/System テーマ | △ | ❌ | ❌ | ❌ | **✅** |
| Sigstore 署名 + SBOM | ❌ | ❌ | ❌ | ❌ | **✅** |
| ARM64 ネイティブ | ❌ | ❌ | ❌ | ❌ | **✅** |
| 11言語 UI | △ | ✅ | ❌ | ❌ | **✅** |
| WCAG AAA アクセシビリティ | ❌ | ❌ | ❌ | ❌ | **✅** |
| 無料 + MIT | ✅ | ✅ | ❌ | ❌ | **✅** |

---

## 主な機能

### 接続管理
- マルチアダプター並列スキャン + タブ UI
- Open / OWE / WEP / WPA / WPA2-PSK / **WPA3-SAE** / WPA3-Transition
- **WPA2/WPA3 Enterprise** (PEAP-MSCHAPv2, EAP-TLS, WPA3 Enterprise 192-bit)
- 実接続検証 (`WlanNotification` + msftconnecttest.com 疎通確認)
- **キャプティブポータル自動検出 + in-app WebBrowser 認証**
- **Auto-Join 自動再接続** — 既知ネットワーク優先順位

### スキャン分析
- **信号履歴グラフ** — 60分 RSSI 時系列 (WPF DrawingVisual)
- **チャンネル帯域グラフ** — 2.4G/5G/6G ガウス曲線可視化
- **MAC ベンダー解決** — IEEE OUI 内蔵 DB、月次自動更新
- **ネットワーク品質計測** — Ping レイテンシ + パケットロス + 評価グレード

### Apple HIG 準拠 UX
- **Clarity** — "WPA3SAE" → "最高セキュリティ" 人間語変換
- **Deference** — 検索ボックス (Spotlight風) + ⋯ オーバーフローメニュー
- **Depth** — シンプル/詳細モード切替 (Progressive Disclosure)
- **Feedback** — 4ステップ接続進捗 + Windowsトースト通知
- **Onboarding** — 初回起動3ページウィザード
- **Recovery** — 接続失敗時 TroubleshootingDialog で解決ガイド

### 出力
- **QR コード生成** — `WIFI:` URI スキーム、PNG 保存
- **エクスポート** — CSV / JSON / TXT 3 形式
- **システムトレイ** 常駐 + クイック接続メニュー
- **Windows JumpList** — タスクバー右クリックに最近接続

### CLI
```powershell
mwc list                          # アダプター一覧
mwc scan --json                   # JSON スキャン
mwc connect "MyWiFi" -p $env:PW   # 接続
mwc qr "MyWiFi" -p secret         # WIFI: URI 出力
mwc export --format csv           # CSV エクスポート
mwc quality 8.8.8.8 -s 10         # 品質計測 (Ping × 10)
mwc history                       # 接続履歴
mwc profile delete "OldNet"       # プロファイル削除
```

### アクセシビリティ (WCAG 2.1 AAA)
- すべての主要カラーペアでコントラスト比 7:1 以上
- スクリーンリーダー (Narrator/NVDA) Live Region 通知
- キーボードのみで完全操作可能 (Ctrl+R / Ctrl+F / Tab / Enter)

### 国際化
**対応11言語** (UI 100% 翻訳済み): ja / en / zh-Hans / zh-Hant / ko / es / fr / de / ru / ar (RTL) / pt-BR

---

## インストール

### winget (推奨)
```powershell
winget install ShizukuTanaka.MWC
```

### MSI
[最新リリース](https://github.com/shizukutanaka/MurtiWifiConnecter/releases/latest) から `MWC-x.x.x-win-x64.msi` または `-win-arm64.msi`。

### dotnet tool (CLI のみ)
```powershell
dotnet tool install -g mwc-cli
```

**動作要件**: Windows 10 1809+ / .NET 8 Runtime / 管理者権限(プロファイル登録時)

---

## ビルド

```powershell
git clone https://github.com/shizukutanaka/MurtiWifiConnecter.git
cd MurtiWifiConnecter
dotnet restore MWC.sln
dotnet build   MWC.sln -c Release
dotnet test    MWC.sln                    # 120 tests
```

---

## アーキテクチャ

```
   MWC.App (WPF)         MWC.Cli (CLI)
        ↓                      ↓
        └───── DI ─────────────┘
                  ↓
       MWC.Platform.Windows
       ├─ WindowsWifiService (ManagedNativeWifi)
       ├─ DpapiSecretProtector
       └─ HttpConnectivityChecker
                  ↓
        MWC.Core (platform-agnostic)
        ├─ ProfileXmlBuilder
        ├─ WifiUri Parser/Builder
        ├─ SignalHistoryService
        ├─ OuiLookupService
        ├─ ExportService
        ├─ NetworkQualityService
        ├─ NetworkHistoryService
        ├─ SecurityBadgeService
        └─ TroubleshootingHelper
```

詳細: [`docs/architecture.md`](docs/architecture.md) / ADR: [`docs/adr/`](docs/adr/)

---

## セキュリティ

- パスワードは **DPAPI** (CurrentUser scope + アプリエントロピー) で保護
- `netsh.exe` / WMI を一切使わず WlanAPI 直叩き (コマンドインジェクション面ゼロ)
- `SecureString` + 使用直後ゼロクリア
- MSI / zip は **Sigstore keyless signing** + **SLSA L3 provenance** 付き
- 詳細: [`SECURITY.md`](SECURITY.md)

---

## 翻訳貢献

新言語サポート: [`docs/i18n-guide.md`](docs/i18n-guide.md) を参照して PR をどうぞ。

すべて Strings.resx ベース。1ファイル46キー × 11言語 = 506エントリ完備。

---


## ドキュメント

- [ユーザーガイド](docs/user-guide.md) — インストールと基本操作
- [FAQ](docs/faq.md) — よくある質問
- [トラブルシューティング](docs/troubleshooting.md) — エラー別の対処
- [アーキテクチャ](docs/architecture.md) — 設計概要
- [ベンチマーク](docs/benchmarks.md) — 性能ベースライン
- [ADR](docs/adr/) — アーキテクチャ決定記録 (14件)

## ライセンス

[MIT](LICENSE)
