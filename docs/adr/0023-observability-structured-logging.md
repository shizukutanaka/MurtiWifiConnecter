# ADR-0023: 観測可能性 — 構造化ログ・ヘルスチェック・PII検証

**Date**: 2026-05-13
**Status**: Accepted

## Context

arxiv-improvement-analysis.md カテゴリー8 (観測可能性) の P0/P1 を実装。最後の P0。

## Decision

### MwcLog — LoggerMessage source generation (C8-1,2)
.NET の [LoggerMessage] 属性でコンパイル時にログメソッドを生成。
- ゼロアロケーション (ログレベル無効時は何もしない)
- 構造化フィールド自動抽出 (検索・集計可能)
- 文字列補間を完全排除
- HashSsid() — SSID を FNV-1a でハッシュ化、PII を含めず追跡可能 (I5)
- netstandard2.0 からは除外 (source gen は net9.0 で使用)

### HealthCheckService (C8-5,7)
- CheckAdapters() — アダプター状態の liveness/readiness 診断
- HealthStatus: Healthy/Degraded/Unhealthy
- VerifyNoPii() — ログ文字列が IPv4/MAC/メール/電話を含まないことを検証 (I5)

## Consequences

- ログが高性能・型安全・構造化され、SIEM や集計に適する
- SSID/パスフレーズ等の PII がログに漏れない (ハッシュ化 + 自動検証)
- アダプターの稼働状態を監視できる
- LoggerMessage はゼロアロケーションで本番のログオーバーヘッドを最小化
