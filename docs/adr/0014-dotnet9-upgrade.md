# ADR-0014: .NET 9 / C# 13 アップグレード戦略

**Date**: 2026-05-13
**Status**: Accepted

## Context

MWC は当初 .NET 8 (LTS) をターゲットとしていた。
2024年11月に .NET 9 がリリースされ、WPF に以下の重要機能が追加された:

1. **Fluent Theme** — Windows 11 ネイティブ UI (Light/Dark/System)
2. **ThemeMode プロパティ** — システムテーマへの自動追従
3. **BinaryFormatter 削除** — WPF のクリップボード/D&D が安全な代替に移行
4. **高DPI改善** — per-monitor DPI awareness の精度向上

また、C# 13 / .NET 9 の言語・ランタイム機能:
1. **System.Threading.Lock** — 専用ロック型。`lock(object)` より安全・高効率
2. **FrozenDictionary** — 読み取り専用の静的データに最適 (ルックアップ ~50% 高速化)
3. **params Span<T>** — GC アロケーション削減
4. **.NET 9 は STS (18ヶ月サポート)** → .NET 10 (LTS, 2025年11月) への準備として採用

## Decision

### .NET 9 へ即座にアップグレード

| 変更 | 理由 |
|---|---|
| `TargetFramework` `net8.0` → `net9.0` | Fluent Theme, Lock, FrozenDictionary |
| `LangVersion` 12.0 → 13.0 | System.Threading.Lock 等の新構文 |
| `global.json` SDK 8.0.100 → 9.0.100 | ビルド環境の統一 |
| WPF `ThemeMode="System"` | Windows 11 システムテーマ自動追従 |

### C# 13 言語機能の活用方針

| 機能 | 使用箇所 |
|---|---|
| `System.Threading.Lock` | `NetworkHistoryService` (非 async 同期ロック) |
| `FrozenDictionary` | `RegulatoryDomainService` (静的25ヶ国テーブル) |
| `params Span<T>` | 将来のパフォーマンスクリティカルな API |

### .NET 10 (LTS) への移行タイムライン

.NET 9 は STS のため、.NET 10 リリース (2025年11月) 後に LTS へ移行を推奨。
`TFM` は `net9.0` → `net10.0` の変更のみで移行可能な設計を維持。

## Consequences

- WPF アプリが Windows 11 の Fluent Design に自動追従
- `NetworkHistoryService` の同期ロックが `Lock` 型で型安全化
- `RegulatoryDomainService` の25ヶ国テーブルが初回アクセス後フリーズ
- CI/CD は .NET 9 SDK (`actions/setup-dotnet@v4` with `9.0.x`) が必要
- netstandard2.0 (Mobile/Unity 互換) は引き続きサポート (`MWC.Core` のマルチターゲット)
