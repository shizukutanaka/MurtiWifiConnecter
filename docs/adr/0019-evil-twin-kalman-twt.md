# ADR-0019: Evil Twin 検出・Kalman 平滑化・TWT 省電力

**Date**: 2026-05-13
**Status**: Accepted

## Context

arXiv 文献の10カテゴリー×10項目分析 (docs/arxiv-improvement-analysis.md) から P0 を実装。

## Decision

### EvilTwinDetector (arXiv 2406.01927)
クライアント側で観測可能な特徴のみで Evil Twin / Rogue AP を検出。CSI/専用HW不要。
- 同一SSIDの複数セキュリティ設定混在
- 既知BSSIDとの不一致・OUI相違
- セキュリティ降格 (WPA3→Open)
- 暗号化SSIDのオープンなりすまし
- リスク3段階: None/Suspicious/HighRisk

### KalmanRssiFilter (C5-2)
1次元カルマンフィルタで RSSI を平滑化。EMA と異なりプロセスノイズ(Q)と測定ノイズ(R)を
明示的にモデル化し、急変追従とノイズ除去を両立。

### TWT フラグ (arXiv 2402.15900, 2411.17424)
WifiNetwork に TargetWakeTime / RestrictedTwt を追加。IoT/バッテリー機器の省電力対応を表示。

## Consequences

- なりすまし AP への接続前に警告できる (フィッシング/中間者攻撃の防止)
- Kalman で信号予測の精度が向上 (SignalQualityPredictor と選択可能)
- TWT 対応 AP を識別し、省電力性を提示できる
- 全サービスがゼロ外部依存を維持

## 2026-09 追記 — KalmanRssiFilter は削除済み

`KalmanRssiFilter` は製品コード(App/CLI)からの参照が一度も無い、既に配線済みだった
`SignalQualityPredictor`(EMA 実装)の未配線な重複だったため削除済み
(CHANGELOG `[Unreleased]` / `docs/FEATURE-AUDIT.md` §1a 参照)。アルゴリズムとしては
Kalman の方が優れているため、平滑化を改善する際は git 履歴から復元して EMA を
**置き換える**形の、実機検証を伴う意図的な変更として行うこと — 2 つ目の未使用実装を
また作らない。`EvilTwinDetector` と TWT フラグは現存し、この ADR の該当箇所は今も有効。
