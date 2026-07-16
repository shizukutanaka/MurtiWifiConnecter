# ADR-0025: 自AP用チャネルプランナー (ChannelPlanner)

**Date**: 2026-06-22
**Status**: Accepted

## Context

既存の助言サービス群 (ADR-0017 `ChannelAdvisorService` ほか) はすべて
**クライアント視点**——「可視 AP のうちどれに接続すべきか」を助言する。
`ChannelAdvisorService.EstimateCongestion(networks, channel)` も「<b>与えられた</b>
チャネルの混雑度」を返す評価関数である。

一方、自分でルーター/AP を運用するユーザーが最も知りたいのは逆問題、
すなわち「<b>近隣スキャンを踏まえて自分の AP をどのチャネルに設定すべきか</b>」である。
これは NetSpot / WiFi Analyzer 等が最前面に置く定番機能「推奨チャネル」に相当し、
MWC には欠けていた (CLAUDE.md「Wi-Fi に集中」に合致する中核機能であり、
AI/量子のような派手機能ではない)。

## Decision

### `ChannelPlannerService` 新設 (MWC.Core, 純関数)

近隣スキャン (`IReadOnlyList<WifiNetwork>`) からバンド別の最適チャネルを推奨する。
スキャンや設定変更は行わず推奨のみ。プラットフォーム非依存でゴールデンテスト可能。

| メソッド | 機能 |
|---|---|
| `Recommend(band, visible, includeDfs)` | 1 バンドの最良チャネル + 根拠 |
| `RankCandidates(band, visible, includeDfs)` | 全候補をスコア順に (UI 表示用) |
| `RecommendAllBands(visible, includeDfs)` | 2.4/5/6GHz をまとめて推奨 |

### 候補集合 (運用ベストプラクティス)

- **2.4GHz**: 非重複の `1 / 6 / 11` のみ
- **5GHz**: 既定で**非 DFS** (UNII-1 + UNII-3)。DFS はレーダー検出で突然停止しうるため
  除外し、`includeDfs=true` で UNII-2/2e も候補に含める (`DfsChannelHelper` を再利用)
- **6GHz**: PSC (Preferred Scanning Channel) — `SixGhzChannelHelper` を再利用

### スコアリング (決定論的)

```
候補 c のコスト = Σ_{near AP on band} OverlapFactor(c, ap) × SignalWeight(ap)
score = round(100 / (1 + cost))      # cost 0→100, 1→50 … 単調減少、0-100 に有界
```

- **OverlapFactor**
  - 2.4GHz: 5MHz 間隔・20MHz 幅 → `|Δch| < 5 ? 1 - |Δch|/5 : 0` (重なりの階調)
  - 5/6GHz: 20MHz 非重複だが**近隣のチャネル幅を考慮**。幅 W の AP は (W/20) スロット =
    中心から `±(slots-1)×4` ch に及ぶ (80MHz は ±12ch)。co-channel=1.0、サブチャネル
    重なり=0.6、隣接ブロック=0.3
- **SignalWeight**: `SignalQuality(0-100)/100` — 強い近隣ほど重く干渉する
- 同点はチャネル番号昇順で決定論的にタイブレーク
- **チャネル不明 (`Channel ≤ 0`) の AP は除外** (低番号候補への偽干渉を防ぐ)

## Consequences

- AP 運用者が近隣干渉を最小化する自 AP チャネルをバンド別に選べる
- DFS の突発停止リスクを既定で回避しつつ、明示時のみ DFS を活用できる
- ワイドチャネル (40/80/160/320MHz) の占有を考慮するため、80MHz 中心の現代環境でも
  干渉を過小評価しない
- 既存の `ChannelAdvisorService` (client 視点) と相補的。重複しない
- 将来: BSS Load IE (実測チャネル使用率) を重みに加味すれば精度が上がる。
  CLI/UI への露出 (例: `mwc plan-channels`、設定ダイアログの「推奨チャネル」表示) は
  別タスクで Core → CLI → UI の順に追加する
- 純 Core・15+ ゴールデンテストで挙動を固定。実機ビルド不要で設計の正しさを検証可能
