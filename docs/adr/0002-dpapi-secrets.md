# ADR-0002: DPAPI for Secret Protection

- Status: Accepted
- Date: 2026-04-25

## Context

WiFi パスフレーズ等の機密情報を保存する必要がある(「保存パスワード表示」「自動再接続用プロファイル」等)。脅威モデル:

- 同一 PC 上の別ユーザーが秘密ファイルを読む
- ディスクイメージ抽出(物理盗難等)
- マルウェアによる読み取り

## Decision

**Windows DPAPI (`ProtectedData.Protect`)** を `DataProtectionScope.CurrentUser` で使用。アプリ識別エントロピー(`"MWC-v1"` 8 バイト)を追加。

## Consequences

### 良い影響
- ユーザーログイン情報を鍵として OS が管理。アプリは鍵を持たない
- 別ユーザーは復号不可
- 別 PC へ移しても復号不可(ユーザープロファイル移行時を除く)
- 標準 .NET API、追加依存ゼロ

### 悪い影響
- ユーザーアカウント破損時に復号不可 → 再入力が必要
  - 緩和: クラウド同期は提供しない(プライバシー優先)
- マルウェアが同一ユーザーで動作すれば復号可能
  - 緩和: SecureString + ゼロクリアでメモリ上の存在時間最小化
  - 緩和: 既知の MITRE ATT&CK T1555.005 への対策として、アプリ識別エントロピーで他アプリの DPAPI トークンと混同されないようにする

## Alternatives Considered

| 候補 | 不採用理由 |
|---|---|
| Windows Credential Manager | UI に必ず表示される、アプリ管理度が低い |
| `LocalMachine` scope DPAPI | 同 PC の別ユーザーから復号可能 |
| AES + ユーザー入力鍵 | UX 悪化(毎回プロンプト) |
| WLAN プロファイル内 `protected=true` | OS 任せだが取り出せない、共有不能 |
