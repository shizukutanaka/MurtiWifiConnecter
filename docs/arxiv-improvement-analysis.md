# MWC arXiv 出典改善点分析 — 10カテゴリー × 10項目

各項目に arXiv / IEEE 論文の出典を付す。優先度 P0=即実装 / P1=次期 / P2=将来。
(前回 docs/improvement-analysis.md は一般調査。本書は arXiv 文献に限定した深掘り。)

---

## カテゴリー1: 省電力・エネルギー効率

1. **[P0] TWT (Target Wake Time) 対応表示** — arXiv 2402.15900: TWT は Service Period 外で doze 状態にしエネルギー削減
2. [P1] rTWT (restricted TWT) リアルタイム最適化 — arXiv 2402.15900: Wi-Fi 7 の専用 rTWT
3. [P1] TWT スケジューリング助言 — arXiv 2509.26245 (TASPER): エネルギー最大34%削減
4. [P2] 802.11ba WUR (Wake-Up Radio) 対応 — arXiv 1909.00594: 極低電力 IoT
5. [P1] AP 省電力モード分析 — arXiv 2411.17424: Wi-Fi 8 AP power save
6. [P2] バッテリー駆動時の接続戦略 (スキャン頻度抑制)
7. [P1] TWT パラメータ (SP/WI/Offset) のモデル化 — arXiv 2302.11512
8. [P2] DTIM 間隔と省電力のトレードオフ表示
9. [P1] アイドル時のアダプター省電力状態監視
10. [P2] エネルギー効率スコア (E/b: energy per bit) — arXiv 2024 ns-3

## カテゴリー2: セキュリティ — Rogue AP / Evil Twin

1. **[P0] Evil Twin 検出 (BSSID 履歴)** — arXiv 2406.01927: 同一SSID異BSSIDの位置ベース検出
2. [P1] RF フィンガープリンティング概念 — arXiv 2403.15739 (DeepCRF): CSI ベース機器識別
3. [P1] 位置ベース rogue AP 検出 — arXiv 2406.01927: AP位置固定性を利用
4. [済] Dragonblood — v3.2.0
5. [済] MFP/deauth — v3.2.0
6. [P1] KRACK (Key Reinstallation) 注意喚起 — arXiv 2510.22731 参照
7. [P2] CSI ベース異常検知 — arXiv 2024: チャネル状態の異常
8. [P1] BSSID OUI ベンダー照合による偽装検出
9. [P0] 信号強度の急変検出 (Evil Twin の物理的兆候)
10. [P2] GAN 生成偽装信号への耐性概念 — arXiv 2510.09663

## カテゴリー3: ローミング・モビリティ

1. [済] 802.11r/k/v — v3.3.0
2. [P1] スティッキークライアント検出 — 遠方APへの固執
3. [P1] ハンドオーバー予測 (信号トレンド + ローミング能力)
4. [P2] 802.11r FT over-the-DS vs over-the-air
5. [P1] ローミング履歴の記録と分析
6. [P2] モビリティドメイン (MD) ID の表示
7. [P1] 連続ローミングのフラッピング検出
8. [P2] PMK キャッシング状態
9. [P1] ローミング閾値のユーザー設定
10. [P2] 802.11k Neighbor Report のパース

## カテゴリー4: チャネル・スペクトラム最適化

1. [済] バンド/チャネル選択 — v3.4.0
2. [P1] OBSS PD (Preamble Detection) しきい値 — 空間再利用
3. [P1] BSS Color (802.11ax) の重複検出
4. [P2] DFS (Dynamic Frequency Selection) レーダー回避チャネル表示
5. [P1] Cross-Technology Interference 検出 — arXiv 2503.05429: BLE/Zigbee干渉
6. [P2] NPCA (Non-Primary Channel Access) — arXiv 2504.15774 (Wi-Fi 8)
7. [P1] チャネル使用率 (BSS Load IE) パース
8. [P2] 5G NR-U 共存検出 — arXiv 2506.22844
9. [P1] 2.4GHz の Bluetooth 共存スコア
10. [P2] AFC (Automatic Frequency Coordination) 6GHz SP モード

## カテゴリー5: 信号品質予測・ML

1. [済] EMA 線形結合予測 — v3.2.0
2. **[P0] Kalman フィルタ RSSI 平滑化** — EMA より高精度な状態推定
3. [P1] 信号品質の信頼区間推定
4. [P2] 軽量時系列異常検知 (移動Z-score)
5. [P1] スループット予測 (RSSI → 推定リンクレート)
6. [P2] チャネル品質の周期性検出 (FFT)
7. [P1] 接続成功率の学習 (履歴ベース)
8. [P2] マルチパスフェージング指標
9. [P1] SNR 推定 (RSSI + ノイズフロア)
10. [P2] リンクレート適応 (MCS) の予測

## カテゴリー6: マルチリンク (Wi-Fi 7 MLO)

1. [P1] MLO (Multi-Link Operation) リンク状態表示 — Wi-Fi 7
2. [P1] STR (Simultaneous Transmit Receive) 能力
3. [P2] EMLSR (Enhanced Multi-Link Single Radio)
4. [P1] リンク別 RSSI/帯域の集約表示
5. [P2] MLO トラフィックステアリング
6. [P1] バンド間 MLO (5GHz+6GHz) の検出
7. [P2] リンク信頼性ベースの冗長化
8. [P1] MLO レイテンシ削減の定量化
9. [P2] T2LM (Traffic to Link Mapping)
10. [P1] MLO 対応 AP のバッジ表示

## カテゴリー7: テスト・形式検証

1. [済] property-based / mutation — 既存
2. **[P0] WifiUri パーサーのファズテスト** — 不正入力耐性
3. [P1] プロファイル XML のスナップショットテスト
4. [P1] 並行性テスト拡充 (RecommendationEngine)
5. [P2] モデル検査 (状態機械の網羅)
6. [P1] i18n リソースのゴールデンファイルテスト
7. [P2] メタモルフィックテスト (スコア単調性)
8. [P1] 回帰テストのベースライン固定
9. [P0] エッジケーステスト (空/最大/境界 RSSI)
10. [P1] クロスプラットフォーム動作の契約テスト

## カテゴリー8: 観測可能性

1. [P0] OpenTelemetry LoggerMessage source generation — 高性能構造化ログ
2. [P1] ActivitySource トレーシング (接続フロー)
3. [P1] Meter メトリクス (p50/p95/p99 レイテンシ)
4. [P2] OTLP エクスポーター
5. [P1] ヘルスチェック
6. [P2] 診断ダンプ
7. [P1] PII 非含有の自動検証
8. [P2] イベントカウンター
9. [P1] ログレベル動的変更
10. [P2] 分散コンテキスト伝播

## カテゴリー9: アクセシビリティ・UX

1. [済] WCAG AA — 既存
2. **[P0] 信号強度の非色覚依存表現** — アイコン形状で冗長符号化
3. [P1] スクリーンリーダー実機テスト
4. [P1] 推奨グレードの音声・テキスト併記
5. [P2] ハイコントラストテーマ
6. [P1] reduced-motion 設定
7. [P1] フォントスケーリング
8. [P2] 触覚フィードバック (モバイル)
9. [P1] Empty State 充実
10. [P0] エラー ID 表示

## カテゴリー10: アーキテクチャ・拡張性

1. [P1] プラグイン API のセキュリティサンドボックス強化
2. [P0] 推奨エンジンの説明可能性 (なぜこのAPか)
3. [P1] サービス間の疎結合 (イベント駆動)
4. [P2] CQRS 風の読み取り/書き込み分離
5. [P1] 設定のスキーマバリデーション
6. [P2] ホットリロード可能な設定
7. [P1] テレメトリのプライバシー保護集約
8. [P2] WASM プラグイン対応
9. [P1] API バージョニング戦略
10. [P2] 拡張ポイントの文書化

---

## 今サイクル実装 (P0)

1. **Evil Twin / Rogue AP 検出** (C2-1,9) — arXiv 2406.01927
2. **TWT 省電力対応** (C1-1) — arXiv 2402.15900
3. **Kalman フィルタ RSSI 平滑化** (C5-2)
4. **推奨エンジンの説明可能性** (C10-2)

v3.6.0 として実装する。

---

## 2026-H2 追補(Web 調査ベース)

前サイクルの100項目に対する差分。出典は本文中の URL を参照。

### 実装済み(このセッション)

- **[実装済み] WPA3-SAE の SSID 非束縛に関する情報提供** — 2025年前半に指摘された
  「WPA3(SAE)でもメッシュ等で SSID がハンドシェイクに暗号学的に束縛されない」という知見を
  `SecurityAdvisoryService`(`MWC-SEC-008`)に追加。既存の `EvilTwinDetector` が引き続き
  有効な防御策であることを利用者に伝える Info 級勧告。
- **[出典更新のみ] `PrivacyAdvisoryService` の引用追加** — arXiv 2408.01578
  (マルチチャネルスニファ+2段クラスタリングによる MAC de-randomization、2024)を
  XML doc に追記。ロジック変更なし(引用の鮮度維持のみ)。

### 発見済み・調査完了・未実装(理由付き)

- **[発見] MLO(Wi-Fi 7 マルチリンク)表示が実は機能していない** —
  `WifiNetwork.MloLinks` を供給するプラットフォームコードが Windows/Linux/macOS
  いずれの実装にも存在しない(`grep -rn "MloLinks\s*=" src/MWC.Platform.*` は0件)ため、
  `MloAnalyzerService` は常に早期リターンし GUI の MLO 行は永遠に非表示。ROADMAP は
  「Wi-Fi 7 MLO サポート」を `[x]` 完了と申告しているが、この観点では未達。
  **解決策を調査済み**: 依存ライブラリ `ManagedNativeWifi`(現行 v3.0.2、
  `Directory.Packages.props`)は v3.0.1(2025-07-04)で
  `NativeWifi.GetRealtimeConnectionQuality(interfaceId)` を追加済み。README
  (`github.com/emoacht/ManagedNativeWifi`)によれば戻り値は
  `(ActionResult result, RealtimeConnectionQualityInfo rcq)` タプルで、`rcq` は
  `PhyType`/`LinkQuality`/`RxRate`/`TxRate`/`IsMultiLinkOperation`/`Links`
  (各リンクは `Rssi`/`Frequency`/`Bandwidth`)を持ち、`WifiNetwork.MloLinks`
  (`MloLink { LinkId, Band, Channel, FrequencyMhz, Rssi, ChannelWidth }`)への
  変換は機械的に可能。**Windows 11 24H2 以降でのみ動作**。
  **実装しなかった理由**: (a) この環境に dotnet SDK がなくコンパイル検証不能、
  (b) `RealtimeConnectionQualityInfo.PhyType` は `ManagedNativeWifi` 名前空間の型で、
  MWC 自身の `MWC.Core.Models.PhyType` と**名前が衝突**するため名前空間修飾が必須、
  (c) README の記載と初期 PR ドラフト(`#71`)とで型名・戻り値の形が食い違っており
  (`GetRealtimeConnectionQuality`/タプル戻り値 vs `GetConnectionQualityInfo`/単純クラス)、
  2系統の情報源で完全一致が取れなかった。実装を誤ると `WindowsWifiService.cs`
  全体がコンパイル不能になり Windows ビルドを壊すリスクがあるため、**dotnet/Windows実機
  検証が可能なセッションでの実装を推奨**する(このドキュメントに必要な情報は揃えてある)。
- **OpenRoaming の主流化(2026)** — WBA 2025 産業調査: 回答企業の81%が導入計画
  (25%は既に展開中、42%が2026年内、27%が2026年目標)。孤立中の `Hotspot20Service`
  (Passpoint 対応、`docs/FEATURE-AUDIT.md` §1a)の配線価値がこの動向により上昇したが、
  配線を阻むブロッカー(802.11u Interworking IE をどのプラットフォーム層も抽出しない)は不変。
- **Wi-Fi 8 (802.11bn) draft 1.0 確定(2025-07)** — 初の民生ルーターが2026年夏に出荷開始。
  `PhyType.Dot11bn` は enum・ラベル("Wi-Fi 8 (802.11bn — Preview)")とも対応済みで
  現状のまま適正。SMD/ELR/DSO 等の新機能はスキャンデータに現れる段階になく対応不要。
- **Windows 11 25H2 の Wi-Fi 7 Enterprise(WPA3-Enterprise + MLO)対応強化** —
  OS/ドライバ層の変更で、MWC 側の対応は不要(既存の `AuthMethod.WPA3Enterprise` +
  上記 MLO 実装で対応範囲に収まる)。

### 検討したが対象外と判断

- **SAE commit frame CPU DoS(70フレーム/秒でAP CPU 100%)** — クライアント側 WLAN API
  からはAP側のCPU負荷を観測できず、MWC(クライアントアプリ)の対応範囲外。
- **WPA3 メッシュの SSID 非束縛「攻撃」の実装** — 本サービスは攻撃を実行しない防御的
  ツールであるため、情報提供(実装済み、上記)に留める。
