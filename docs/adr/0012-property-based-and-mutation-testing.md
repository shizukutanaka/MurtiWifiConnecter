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

## 2026-09 追記 — Stryker.NET は一度も導入されていない

`Stryker` はこのリポジトリのどこにも存在しない(`Directory.Packages.props` に
参照なし、`.csproj`/CI 設定にも無し)。`.github/workflows/` 自体が無い(§0、
`docs/FEATURE-AUDIT.md` 参照)ため、週次実行や `[mutation]` コミットトリガーは
そもそも成立しようがなかった。加えて Stryker.NET は NuGet 経由でしか入手できず、
このセッション群が動く環境では `api.nuget.org` がエグレス拒否のため取得不能
(`docs/COMPLETION-CHECKLIST.md` の外部ブロッカー一覧参照)。

代わりに **`tools/mutation-check.sh`** という手書きスクリプトが、限定的だが
実測済みの代替になっている: 製品コードへ意図的な欠陥を 1 箇所ずつ手で注入し
(現在 5 件 + 対照 1 件)、テスト失敗数が増える(kill)か対照は変化しない(survive)かを
`csc` 直叩き + 手製ランナーで確認する。Stryker のような自動変異生成・スコア閾値
(high=80%/low=60%/break=50%)・週次スケジュールは無く、対象は手で選んだ代表 5 パスの
みで、スクリプト自身のヘッダが「網羅的な mutation testing ではない」と明記している。

FsCheck 側は実装されている(`tests/MWC.Core.Tests/PropertyBasedTests.cs`、10 個の
`[Property(MaxTest = ...)]`)。ケース数はおおむね主張どおり 100〜300 だが、1 件のみ
`MaxTest = 50` で範囲外。CI が無いため「`dotnet test` で毎回実行」も未検証(NuGet
封鎖のため FsCheck 自体、このセッション群ではコンパイルすらできない —
`tools/typecheck-tests.sh` が FsCheck 依存の 1 ファイルを検査対象外にしている理由)。
