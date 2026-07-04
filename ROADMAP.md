# MWC Roadmap

このドキュメントは MWC の今後の方向性を示す。優先順は固定ではなく、コミュニティの反響と技術的実現性で変動する。

> **注記(2026-07 監査)**: 「実装されている」(Core にクラスがあり単体テストが通る)と
> 「機能している」(ユーザーが App/CLI から実際に到達できる)は別物であることが判明した。
> 監査の結果、`[x]` 完了扱いだった項目のうち 6 件が、対応する Core サービスを持ちながら
> App/CLI/他の Core サービスのいずれからも一度も呼び出されていない(=呼び出し元ゼロ)ことを
> 確認したため `[ ]` へ差し戻した。コードは変更していない — チェック状態のみ実態に合わせた。
> 機能の過剰・不足・適正の全体リストと再監査手順は [docs/FEATURE-AUDIT.md](docs/FEATURE-AUDIT.md) を参照。

## v2.x (短期: 3-6ヶ月)

### コア機能強化
- [x] EAP-TLS クライアント証明書ストアからの自動選択 UI
- [x] Wi-Fi 7 (802.11be) MLO (Multi-Link Operation) サポート
- [x] 6 GHz 帯の規制ドメイン別チャネル表示 — `RegulatoryDomainService` を `NetworkDetailViewModel`
  (6GHz ネットワークのみ表示)に 2026-07 配線完了。詳細は `docs/FEATURE-AUDIT.md` §1a 参照。
- [x] スキャン履歴の長期保存(90日・500件上限)— 実装は `NetworkHistoryService` による JSON
  ファイル保存(`%LocalAppData%/MWC/history.json`)。**SQLite ではない**(2026-07 監査で
  判明、以前の記載は技術詳細が誤り)。500件規模の単純な読み書きに SQLite は過剰で、
  CLAUDE.md の「依存追加 vs 自前実装 → ≤200行なら自前」という方針にも整合するため、
  機能要件(90日保持)自体は満たしており、実装方針の変更は不要と判断。

### UX
- [x] 言語追加: 中国語簡体字以外の地域言語(ヒンディー語、ベンガル語、タミル語)。
  2026-07 監査で `Strings.bn.resx`/`Strings.hi.resx`/`Strings.ta.resx` の 426 キー中 274 キー
  (64%)が英語原文のまま一字一句コピーされたプレースホルダーで未翻訳と判明したため、
  この3言語分を翻訳して適用。また全15ロケールファイルで欠落していた `Captive_NavigationFailed`
  (全ロケール共通)と、13ロケールで欠落していた `Export_FilterCsv`/`FilterJson`/`FilterTxt`/
  `FilterDiagnostic`/`QR_PngFileFilter`/`Tray_AdapterMenuItem` も補完し、全15ファイルが
  ちょうど515キーで揃うことを確認。`LocaleKeyConsistencyTests` を追加し、キー欠落の再発を
  自動検出する(値の翻訳品質までは検証しない — ネイティブスピーカーによるレビューは別途推奨)。
- [x] WCAG AAA 全画面ナビゲーション検証(Dark/Light/Nord/Catppuccin テーマ。Solarized は
  実在の著名パレット保持を優先し AA。Fluent は OS システムカラー依存のため本文コントラストは
  検証対象外)— `ThemeAccessibilityAuditTests` で自動検証。2026-07 監査でこの検証自体が
  一度も実行されていなかったことが判明し、実施したところ4件の実コントラスト不足を発見・修正
  (Light の AccentTextBrush、Dark/Nord/Solarized の DangerTextBrush)。
- [ ] スクリーンリーダー実機テスト (NVDA / JAWS / ナレーター)— **未検証**(2026-07 監査)。
  リポジトリ内に対応する自動テスト・実施記録が一切見つからず、この主張を裏付ける証拠がない。
  実機での人手検証が本質的に必要な項目のため、この監査では「反証」ではなく「証跡なし」の
  指摘にとどめる(AutomationProperties.Name 自体は全 View で要素数以上の出現数が確認できて
  おり、少なくとも土台は整っている)。

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
- [ ] Wi-Fi Direct ピアツーピア接続 — `WifiDirectService` (Core) は実装済みだが、App/CLI の
  どこからも呼び出されておらず、ユーザーは到達できない。GUI/CLI への配線が未完了。
- [x] WPA3-OWE (Opportunistic Wireless Encryption) 自動選択 — `OweSelectionService` を CLI
  `mwc scan` および App 両スキャンパイプライン(`AdapterViewModel`/`AdapterPanelViewModel`)に
  2026-07 配線完了。詳細は `docs/FEATURE-AUDIT.md` §1a 参照。
- [ ] Hotspot 2.0 / Passpoint 自動接続プロファイル — `Hotspot20Service` (Core, キャリアプリセット
  含む) は実装済みだが未配線。同上。
- [ ] eduroam ワンクリック設定 (CAT XML インポート) — `CatImportService` (Core, XXE 対策済み)は
  実装済みだが未配線。同上。

### 開発者向け
- [x] MWC.SDK NuGet パッケージ(Cli/App をライブラリ化)
- [x] PowerShell モジュール `Install-Module MWC`

## v4.0 (長期: 1年以降)

### モバイル
- [x] Android 版 (.NET MAUI + WifiManager)
- [x] iOS 版 (NEHotspotConfiguration)

### エンタープライズ
- [ ] Group Policy 経由でのプロファイル一括配布 — `GroupPolicyProvider` (Core, レジストリ
  読み取り) は実装済みだが、App/CLI のどこからも呼び出されておらず未配線。
- [x] Intune / Endpoint Manager 統合
- [x] 自社 RADIUS サーバ証明書の自動検証

### コミュニティ
- [x] プラグイン API (C# / TypeScript)
- [x] テーマパック (有志制作の Light/Dark 派生)

## 検討中(未確定)

- [x] VPN 使用推奨(信頼済み AP では VPN 任意/不要と助言)— `VpnAdvisoryService` / `mwc vpn-advice`
  として実装済み。ただし「自動切替」(OS の VPN 接続を実際にオン/オフする)部分は未実装。
  VPN 状態を誤って変更した際の影響が大きい(機密トラフィック露出等)ため、本サービスは
  助言のみ提供し、実際の切替はユーザー/OS の判断に委ねる設計とした。
- ネットワーク品質長期トレンド分析(機械学習)— CLAUDE.md により量子/AI 等の「派手な機能」は
  対象外。統計的トレンド分析(EMA 等、非 ML)であれば再検討の余地あり。
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
