# ADR-0010: NetworkHistoryService スレッド安全化

**Date**: 2026-05-13
**Status**: Accepted

## Context

`NetworkHistoryService` は `List<ConnectionHistoryEntry>` を内部状態として保持する。
`RecordConnection()` は以下の経路から並列呼出される可能性がある:

1. `AutoReconnectService.WatchAsync()` — バックグラウンドスレッドから
2. `ConnectionExecutor.ConnectAsync()` — 接続処理(並列接続の場合)
3. `MainWindowCommands` → UI スレッドから

`List<T>` はスレッドセーフでないため、競合により InvalidOperationException または
サイレントなデータ破損が発生し得る。

## Decision

`SemaphoreSlim(1, 1)` を使用して全 `_entries` アクセスをシリアライズする。

**なぜ lock ではなく SemaphoreSlim か:**
- `Save()` 内部が `File.WriteAllText()` でブロッキング I/O になる可能性
- 将来 async 版 Save() に移行するときに lock のままでは async/await と組み合わせられない
- SemaphoreSlim は async lock として使えるため拡張性が高い

**なぜ ConcurrentDictionary/ConcurrentBag ではないか:**
- 履歴の「先頭に追加して最大 500 件に刈り込む」操作は原子的でなければならない
- 単一のロックで複数操作を原子化する方が簡潔

## Consequences

- `RecordConnection()` / `GetRecent()` / `GetAll()` / `Forget()` / `ClearAll()` は SemaphoreSlim で保護
- `GetRecentSsids()` は `GetRecent()` を呼ぶため間接的に保護される
- ロック範囲を最小化するため、JSON シリアライズはロック外で実行
- パフォーマンス影響: 接続履歴の書き込みは低頻度(接続時のみ)のため無視できる
