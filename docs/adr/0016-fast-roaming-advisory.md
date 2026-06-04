# ADR-0016: 802.11r/k/v 高速ローミング診断

**Date**: 2026-05-13
**Status**: Accepted

## Context

モバイル利用 (建物内を歩き回る、複数 AP 環境) では、AP 間のローミングが頻発する。標準的なローミングでは再認証に 200-300ms かかり、VoIP やビデオ会議で音切れ・映像停止を引き起こす。

arXiv / IEEE の研究 (Machań & Wozniak, "On the fast BSS transition algorithms in the IEEE 802.11r local area wireless networks", Telecommunication Systems) によれば、高速ローミングの3標準が遷移遅延を大幅に削減する:

| 標準 | 機能 | 効果 |
|---|---|---|
| 802.11r (Fast BSS Transition) | 再認証ハンドシェイクの簡略化 | 250ms → 50ms、最良 13ms |
| 802.11k (Neighbor Report) | AP 候補リスト提供 | 全チャネルスキャンを排除 |
| 802.11v (BSS Transition Management) | ネットワーク主導のローミング誘導 | 最適 AP への遷移を誘導 |

これらは WPA2/WPA3-Enterprise で最も効果的 (複雑な 802.1X 認証を高速化する設計)。

## Decision

### モデル拡張

`WifiNetwork` に3つのフラグを追加:
- `FastTransition` (802.11r)
- `NeighborReport` (802.11k)
- `BssTransitionMgmt` (802.11v)

### `RoamingAdvisoryService` 新設

- `Analyze()` → `RoamingProfile` (Tier / 対応標準 / 推定遷移遅延 / VoIP適性)
- `RoamingTier`: Seamless (r+k+v) / Fast (r) / Assisted (k+v) / Standard
- `IsRealtimeCapable()` — 50ms 以下なら VoIP 可能と判定
- `RecommendForMobility()` — 同一 SSID から最もローミングに優れた AP を推奨
- `DescribeRoaming()` — 人間語のアドバイス生成

### 遷移遅延の目安 (論文値)

```
LegacyHandoverMs = 250  // 標準再認証
FastTransitionMs =  50  // 802.11r
OptimalFtMs      =  13  // FT + 最適化 (論文の最良ケース)
```

## Consequences

- ユーザーは移動中に途切れにくいネットワークを選べる (VoIP/ビデオ会議向け)
- モビリティ重視シーンで最適 AP を自動推奨できる
- Enterprise 認証時に 802.11r の効果が最大であることを提示できる
- プラットフォーム層で実際の RSN/Mobility Domain IE から
  802.11r/k/v フラグを取得する実装が今後必要 (現状はモデルとロジックのみ)
- `SignalQualityPredictor` (ADR-0015) と組み合わせ、
  信号悪化を予測して事前にローミングを促す将来拡張が可能
