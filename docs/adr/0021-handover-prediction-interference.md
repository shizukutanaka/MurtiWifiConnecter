# ADR-0021: ハンドオーバー予測・干渉分析

**Date**: 2026-05-13
**Status**: Accepted

## Context

arxiv-improvement-analysis.md の高価値 P1 を実装。既存サービスを統合し、移動時の接続品質を高める。

## Decision

### HandoverPredictor (C3-2,3,7)
SignalQualityPredictor (信号トレンド) と RoamingAdvisoryService (ローミング能力) を統合。
- Evaluate(): 信号悪化を予測して事前ローミングを推奨 (Urgency 4段階)
- IsStickyClient(): 弱信号で遠方 AP に固執する状態を検出
- DetectFlapping(): 短時間の AP 往復 (ピンポン) を検出、閾値調整を促す

### InterferenceAnalyzer (C4-5,9)
Cross-Technology Interference (arXiv 2503.05429 系) をクライアント視点で分析。
- co-channel / adjacent-channel 干渉のスコア化
- 2.4GHz の Bluetooth/Zigbee 共存リスク
- BluetoothCoexistenceScore(): 非重複チャネルとAP密度から共存性を評価
- 干渉レベルに応じたバンド移行推奨

## Consequences

- 移動中の信号悪化を予測し、途切れる前にローミングを促せる
- スティッキー/フラッピングという2大ローミング問題を検出
- 2.4GHz の CTI を定量化し、5GHz/6GHz 移行を助言
- 全サービスがゼロ外部依存を維持
