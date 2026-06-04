# ADR-0003: CommunityToolkit.Mvvm for MVVM

- Status: Accepted
- Date: 2026-04-25

## Context

v0.x は MVVM 不採用、code-behind で全ロジック実装。テスト不能、UI と業務ロジックが密結合。

## Decision

`CommunityToolkit.Mvvm` 8.x を使用。`[ObservableProperty]` `[RelayCommand]` ソースジェネレーターで boilerplate 削減。

## Consequences

### 良い影響
- ViewModel 単体テスト可能(プラットフォーム非依存)
- ソースジェネレーターで `INotifyPropertyChanged` boilerplate ゼロ
- 軽量(`Microsoft.Toolkit.Mvvm` 旧名と異なり、依存も最小)
- Microsoft 公式メンテ

### 悪い影響
- ソースジェネレーター学習コスト
- `partial class` 必須

## Alternatives Considered

| 候補 | 不採用理由 |
|---|---|
| Prism | 重い、Region/Module は本プロジェクトに過剰 |
| MVVMLight | 開発停止 |
| ReactiveUI | 学習コスト高、本プロジェクト規模に不釣合 |
| 自前 INotifyPropertyChanged | boilerplate 過多 |
| `Microsoft.Toolkit.Mvvm`(旧名) | 非推奨、CommunityToolkit に統合済 |
