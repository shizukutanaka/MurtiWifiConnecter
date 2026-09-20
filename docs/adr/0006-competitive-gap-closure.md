# ADR-0006: Competitive Feature Gap Closure

- Status: Accepted
- Date: 2026-04-25
- Deciders: Shizuku Tanaka

## Context

競合8種を深堀り調査した結果、MWC v1.0 には以下のギャップが確認された:

| 機能 | WifiInfoView | NetSpot | inSSIDer | Acrylic | MWC v1.0 |
|---|:---:|:---:|:---:|:---:|:---:|
| 信号履歴グラフ | ❌ | ✅ | ✅ | ✅ | **❌** |
| チャンネル帯域可視化 | ❌ | ✅ | ✅ | ✅ | **❌** |
| MACベンダー解決 | ✅ | ✅ | ✅ | ✅ | **❌** |
| CSV/JSONエクスポート | ✅ | ✅ | ✅ | ✅ | **❌** |
| システムトレイ | ❌ | ❌ | ❌ | ❌ | **❌** |
| Wi-Fi 6E/7 PHY | ❌ | ✅ | ✅ | ✅ | **△** |
| ネットワーク詳細パネル | ✅ | ✅ | ✅ | ✅ | **❌** |

## Decision

以下7機能を実装して差を埋める:

1. `SignalHistoryService` — リングバッファ方式60分時系列
2. `ChannelBandCanvas` — WPF DrawingVisual ガウス曲線チャンネル帯域図
3. `OuiLookupService` — IEEE OUI 内蔵DB + PS更新スクリプト
4. `ExportService` — CSV/JSON/TXT 3形式
5. `SystemTrayService` — NotifyIcon + クイック接続メニュー
6. `PhyType` enum拡張 — Dot11be(Wi-Fi 7)まで完全対応 + 6GHz Band
7. `NetworkDetailViewModel` — BSSID/周波数/速度/ベンダー全表示

## MWC の差別化優位(競合比較)

MWC v1.1 時点で競合全員に勝る点:

| 差別化 | WifiInfoView | NetSpot | inSSIDer | Acrylic | **MWC** |
|---|:---:|:---:|:---:|:---:|:---:|
| WPA3-SAE/Enterprise接続 | ❌ | ❌ | ❌ | ❌ | **✅** |
| マルチアダプタータブ | ❌ | ✅ | ❌ | ❌ | **✅** |
| CLI (mwc コマンド) | ❌ | ❌ | ❌ | ❌ | **✅** |
| QRコード生成/パース | ❌ | ❌ | ❌ | ❌ | **✅** |
| 実接続検証 (NCSI) | ❌ | ❌ | ❌ | ❌ | **✅** |
| DPAPI パスワード保護 | ❌ | ❌ | ❌ | ❌ | **✅** |
| **無料** | ✅ | △ | △ | △ | **✅** |
| **オープンソース** | ❌ | ❌ | ❌ | ❌ | **✅(MIT)** |
| 信号履歴 | ❌ | ✅ | ✅ | ✅ | **✅** |
| チャンネル帯域図 | ❌ | ✅ | ✅ | ✅ | **✅** |
| MACベンダー | ✅ | ✅ | ✅ | ✅ | **✅** |
| CSV/JSONエクスポート | ✅ | ✅ | ✅ | ✅ | **✅** |
| システムトレイ | ❌ | ❌ | ❌ | ❌ | **✅** |
| SBOM/署名/SLSA | ❌ | ❌ | ❌ | ❌ | **✅** |
| winget/MSI配布 | ❌ | ❌ | ❌ | ❌ | **✅** |
| ARM64ネイティブ | ❌ | ❌ | ❌ | ❌ | **✅** |
| 30+言語 | ✅(NirSoft) | ❌ | ❌ | △ | **✅** |
| WCAG 2.1 AA | ❌ | ❌ | ❌ | ❌ | **✅** |

## Consequences

### 良い影響
- 競合で有料(NetSpot $49-499、inSSIDer 有料)の機能を無料・MIT で提供
- 接続管理+スキャン分析を一本化(競合は分析か接続のどちらか)
- CLI により企業の WiFi 自動化スクリプトに組み込み可能

> **2026-09 訂正**: 上の「差別化優位」表のうち **`SBOM/署名/SLSA` と `winget/MSI配布` の
> 2 行は事実ではない。** これは `SECURITY.md`/README がかつて主張していたのと同種の
> 誤りで、そちらは既に是正済み(CHANGELOG `[Unreleased]` の Security セクション参照:
> 「No binary has ever been produced.」)。実際には:
> - リリースが一度も作成されたことがない(タグ push が権限不足で拒否される。
>   `docs/COMPLETION-CHECKLIST.md` の外部ブロッカー一覧参照)ため、署名済み
>   バイナリ・SBOM・SLSA provenance のいずれも存在しない。
> - MSI は一度もビルドできていない(WiX のバージョン不一致。この ADR の直後にある
>   `docs/adr/0005-multi-channel-distribution.md` の 2026-09 追記参照)。
> - winget manifest(`installer/winget/manifest.yaml`)はファイルとして存在するが、
>   参照先となる実際の GitHub Release が無いため winget-pkgs へ一度も公開されていない。
>
> これら 2 行は本 ADR が「決定」した 7 機能(上記 1〜7)には含まれておらず、
> 別途(おそらく `docs/ci/release.yml` 設計時の見込みとして)後から表に足された
> ものと見られる。表自体を書き換えるのではなくこの訂正を残すのは、当時どう
> 認識されていたかの記録を保つため。

### 悪い影響
- `SystemTrayService` が `System.Windows.Forms.NotifyIcon` に依存(WPF から外れる)
  - 緩和: App.csproj に `<UseWindowsForms>true</UseWindowsForms>` 追加で解決
- `ChannelBandCanvas` のガウス曲線近似は数学的正確性より視認性優先
  - 緩和: 実際のチャンネル幅(ChannelWidth)を参照し補正

## Not Implemented (意図的除外)

| 機能 | 除外理由 |
|---|---|
| ヒートマップ | サイトサーベイ専用。本ツールの用途外。NetSpot に任せる |
| パケットキャプチャ | Wireshark の領域。過剰 |
| VPN 統合 | 別プロダクト |
| スピードテスト | Speedtest.net で十分 |
| ルーター管理 UI | Web GUI の領域 |
