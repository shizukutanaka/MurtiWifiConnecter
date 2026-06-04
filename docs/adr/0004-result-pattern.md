# ADR-0004: Result Pattern for Business Failures

- Status: Accepted
- Date: 2026-04-25

## Context

WiFi 接続失敗には複数の業務上の理由がある:
- パスフレーズ誤り
- 圏外
- アダプター無効
- タイムアウト
- 権限不足
- キャプティブポータル

これらは「期待された失敗」であり、例外ではない。一方、`UnauthorizedAccessException` 等の OS 例外は本物の例外。

## Decision

**Result 型**(`ConnectionResult` struct)で業務失敗を表現。例外は OS/プログラムバグ用に限定。

```csharp
public readonly record struct ConnectionResult
{
    public bool Success { get; init; }
    public ConnectionFailure? Failure { get; init; }
    ...
}

public enum ConnectionFailure
{
    BadCredentials, Timeout, NotInRange,
    AdapterDisabled, InsufficientPrivilege, ...
}
```

## Consequences

### 良い影響
- 呼出側がすべての失敗パターンを網羅できる(コンパイラ支援、`switch expression`)
- 例外スタック生成コスト回避
- 型シグネチャから失敗が明示

### 悪い影響
- C# 標準パターンではないため学習必要
- `try/catch` の代わりに `if (!result.Success)` ボイラープレート
  - 緩和: パターンマッチで簡潔化

## Alternatives Considered

| 候補 | 不採用理由 |
|---|---|
| 例外 only | 業務失敗まで例外化、性能/可読性低下 |
| `FluentResults` パッケージ | 依存追加に見合わない、自前 30 行で十分 |
| `OneOf<T1, T2, ...>` | 型階層複雑化 |
| `Tuple<bool, string?>` | 命名不能、保守性低 |
