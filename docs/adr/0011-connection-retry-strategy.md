# ADR-0011: ConnectionExecutor リトライとタイムアウト設計

**Date**: 2026-05-13
**Status**: Accepted

## Context

Wi-Fi 接続は一時的な失敗(ドライバーの一時不具合、チャネル混雑など)で失敗することがある。
単一試行で失敗した場合にユーザーが手動リトライを強いられるのは UX として不適切。

## Decision

### リトライ戦略
- 最大 2 回リトライ(合計 3 回試行)
- リトライ間隔: 指数バックオフ (500ms, 1000ms)
- リトライしない条件: `BadCredentials`, `InsufficientPrivilege`(ユーザー操作が必要)
- ユーザー起因のキャンセル(`ct.IsCancellationRequested`)はリトライせず再スロー

### タイムアウト設計
- `CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts)` でタイムアウト CTS を合成
- タイムアウト切れは `OperationCanceledException` として捕捉し `Timeout` 失敗として返す
- 外部からのキャンセル(`ct.IsCancellationRequested`)は上位に伝播

### 変更前後の比較

| | 変更前 | 変更後 |
|---|---|---|
| 失敗時 | 1回だけ試行して返す | 最大3回試行 |
| タイムアウト処理 | TimeSpan を _wifi に渡すだけ | 明示的 CTS で制御 |
| 例外 | OsError に変換 | 状況に応じた ConnectionFailure |

## Consequences

- 一時的な失敗の自動回復により UX が向上する
- 認証失敗は即時返却するため無駄なリトライが発生しない
- 接続所要時間は最悪 3 倍になるが、実際の失敗では避けられない
- 履歴記録はリトライ全体の最終結果に対して1回のみ行われる
