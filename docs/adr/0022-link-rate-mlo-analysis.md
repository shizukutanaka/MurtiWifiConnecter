# ADR-0022: リンクレート推定・MLO 分析

**Date**: 2026-05-13
**Status**: Accepted

## Context

arxiv-improvement-analysis.md の P1 (信号予測・MLO カテゴリー) を実装。

## Decision

### LinkRateEstimator (C5-5,9,10)
RSSI → SNR → MCS → スループット の推定チェーン。
- EstimateSnr(): RSSI - ノイズフロア (-95dBm)
- EstimateMaxMcs(): SNR から達成可能な最高 MCS (802.11ax/be テーブル)
- EstimatePhyRateMbps(): MCS/チャネル幅/空間ストリームから理論レート
- Estimate(): 実効スループット (PHY×65%) と品質5段階

### MloAnalyzerService (C6 — Wi-Fi 7 MLO)
既存 MloLink モデルを分析。
- Analyze(): リンク数/バンド/クロスバンド/集約スループット/信頼性階層
- EstimateLatencyReductionPercent(): 複数リンク選択によるレイテンシ削減 (2link≈30%, 3link≈45%)
- BestLink(): STR で優先される最良リンク

## Consequences

- ユーザーは接続前に期待スループットを把握できる (RSSI だけでなく実効 Mbps)
- Wi-Fi 7 MLO の利点 (集約/低レイテンシ/信頼性) を定量提示できる
- LinkRateEstimator は MloAnalyzerService からも利用され、リンク毎のレートを集約
- 全サービスがゼロ外部依存を維持
