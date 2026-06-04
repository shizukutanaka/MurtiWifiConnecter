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
