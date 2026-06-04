# ADR-0024: 省電力分析・OUI ベンダー照合

**Date**: 2026-05-13
**Status**: Accepted

## Context

arxiv-improvement-analysis.md カテゴリー1 (省電力) とカテゴリー2 (Evil Twin) の P1 を実装。

## Decision

### PowerSaveAdvisorService (C1-1,2,3)
TWT/rTWT の省電力効果を分析。
- arXiv 2402.15900 (TWT), TASPER 2509.26245 (最大34%削減)
- PowerSaveTier: Legacy/Standard(TWT)/Advanced(rTWT)
- RecommendedScanIntervalSeconds() — バッテリー時に省電力性に応じてスキャン間隔調整
- RecommendPowerMode() — バッテリー残量に応じた Performance/Balanced/MaxSaving
- IsIotFriendly() — TWT 対応で IoT 機器向け判定

### EvilTwinDetector OUI ベンダー照合 (C2-8)
既存 OuiLookupService を統合。
- RecordTrusted() で正規 AP のベンダー (OUI) も記録
- Analyze() で既知と異なる機器ベンダーを検出 → なりすまし兆候
- 攻撃者が BSSID を偽装しても、ハードウェアベンダーの不一致で検出できる

## Consequences

- バッテリー駆動機器で省電力性の高い AP を選べる
- 残量に応じてスキャン頻度・電源モードを自動調整
- Evil Twin 検出にベンダー照合が加わり精度向上
- 全サービスがゼロ外部依存を維持
