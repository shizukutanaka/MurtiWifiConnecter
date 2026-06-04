# ADR-0020: 説明可能性・非色覚依存表現・ファズテスト

**Date**: 2026-05-13
**Status**: Accepted

## Context

arxiv-improvement-analysis.md の残 P0 を実装。

## Decision

### 推奨エンジンの説明可能性 (C10-2)
NetworkRecommendationEngine.Explain() で「なぜこの AP か」を各次元の重み付き寄与とともに提示。
- DimensionContribution: 次元名/スコア/重み/寄与
- 寄与の大きい順にソート、TopFactor を特定
- 各次元の寄与合計が総合スコアに一致 (検証可能)

### 信号強度の非色覚依存表現 (C9-2, WCAG 1.4.1)
SignalIconService で色以外の冗長な手がかりを提供:
- バー本数 (0-4) / 記号 (▰▱) / テキストラベル / 補助的な色
- AccessibleLabel() はスクリーンリーダー向けに色名を含まない説明を生成
- 色覚多様性 (約8%の男性) のユーザーも判別可能

### ファズテスト (C7-2)
WifiUri.TryParse() — 例外安全ラッパーを追加。
- 不正入力 (空/制御文字/エスケープ/超長文/ランダム200ケース) で例外を投げない
- パーサーの堅牢性を保証

## Consequences

- ユーザーは推奨理由を理解でき、信頼して選択できる (ブラックボックス回避)
- WCAG 1.4.1 準拠で色覚多様性に対応
- パーサーが任意の入力に対してクラッシュしない
