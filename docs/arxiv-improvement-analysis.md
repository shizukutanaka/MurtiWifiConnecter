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
