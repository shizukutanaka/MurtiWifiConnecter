# ADR-0018: 統合推奨エンジン・リトライポリシー・Captive Portal API

**Date**: 2026-05-13
**Status**: Accepted

## Context

10カテゴリー×10項目の改善分析 (docs/improvement-analysis.md) から、横断的影響の大きい P0 項目を実装する。

## Decision

### NetworkRecommendationEngine (C4-4)
既存4サービス (Security/Roaming/Channel/Signal) のスコアを用途別重みで合算し、単一推奨を提示。
- UsageProfile: General/Realtime/Secure/Throughput で重みを変更
- Rank() でランキング、Recommend() で最適1件、Grade で4段階評価

### RetryPolicy (C1-6)
指数バックオフ + Full Jitter (AWS方式) で thundering herd を回避。
- delay = random(0, min(cap, base*2^attempt))
- IsRetriable() で認証失敗等の非リトライ判定

### CaptivePortalService (C1-1, C2-1)
RFC 8908/8910 準拠の Captive Portal API 状態機械。
- DHCP Option 114 / RA で検出された portal の JSON 状態をパース
- captive / user-portal-url / venue-info-url / seconds-remaining 等
- レガシー HTTP リダイレクト傍受より堅牢

## Consequences

- ユーザーは用途に応じた最適 AP を単一スコアで選べる
- 再接続の衝突が時間分散され、混雑環境での成功率が向上
- modern iOS/Android と同様の Captive Portal 体験を提供できる
- 全サービスがゼロ外部依存を維持 (Polly 不使用、軽量自前実装)
