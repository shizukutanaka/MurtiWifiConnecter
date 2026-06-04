# ADR-0012: プロパティベーステストとミューテーションテスト戦略

**Date**: 2026-05-13
**Status**: Accepted

## Context

MWC の品質保証において、手動で書くユニットテスト(Fact/Theory)だけでは以下が不足する:

1. **見えない境界値**: "abcde" や "あいう" など思いつかない入力は手動テストに含まれない
2. **テストの有効性確認**: テストが通っても実装が間違っていないとは限らない(デッドコード・常に true 等)

## Decision

### プロパティベーステスト (FsCheck)
- **対象**: ビジネスロジック (WifiUri, ProfileXmlBuilder, AccessibilityAudit, RegulatoryDomain, AdapterPrefs)
- **ケース数**: 各100-300ケース(ランダム生成)
- **実行**: 通常の `dotnet test` で毎回実行

### ミューテーションテスト (Stryker.NET)
- **対象**: `src/MWC.Core/Services/*.cs`, `Profile/*.cs`, `Models/*.cs`
- **スコア閾値**: high=80%, low=60%, break=50%
- **実行**: 週次(毎週月曜 02:00 UTC) + `[mutation]` コミットメッセージトリガー
- **理由**: ミューテーションテストは重いため CI 毎回は実行しない

## Consequences

- 開発時は FsCheck で素早くランダム境界値テスト
- 週次 Stryker でテスト品質のドリフトを検出
- 新機能追加時は `[mutation]` タグで即座に検証できる
