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

## 2026-09 追記 — 実装は `SemaphoreSlim` ではなく `object` ロック 2 本

現行の `src/MWC.Core/Services/NetworkHistoryService.cs` は、この ADR が明示的に退けた
`lock` 文を使っている(`SemaphoreSlim` ではない)。ただし単一ロックではなく、
**この ADR 自身が挙げた「ロック範囲最小化」「読み取りを I/O でブロックしない」という
目的を、別の手段で満たす形**になっている:

- `_lock` — `_entries` (List) へのアクセス全般を保護。
- `_saveLock` — `Save()` のディスク I/O だけを直列化する別ロック。
  読み取り系メソッドは `_lock` を握るだけで済み、書き込み中のディスク I/O に
  ブロックされない。

`SemaphoreSlim` を選ばなかった理由はソース内コメントに明記されている:
「`_entries` 保護用。net9.0 / netstandard2.0 双方でビルドできるよう object lock を使用」
——当時 `MWC.Core` は netstandard2.0 を含むマルチターゲットで(`docs/adr/0009-*.md` の
2026-09 追記参照)、`Save()` はブロッキング I/O のまま(async 化されていない)ため、
`SemaphoreSlim` の非同期待機能力を活かす場面が無く、単純な `lock` で足りると判断された
とみられる(コミット時の直接の記録は無いため、コードとコメントからの推定)。

これは ADR-0010 の目的(読み取りを I/O でブロックしない・ロック範囲最小化)を裏切る
ものではなく、**手段が変わっただけ**。ただし `NetworkHistoryService` は過去に実在の
並行実行バグ(`NetworkHistoryService_ConcurrentWrites_ThreadSafe` — 保存先が
`static readonly` の固定パスで全インスタンス共有していた不具合。
`docs/COMPLETION-CHECKLIST.md` 参照)の現場でもあるため、ロック機構自体を
変更する場合は必ず並行実行の実測検証を伴わせること。
