# ADR-0013: WifiProfileSpec 入力検証戦略

**Date**: 2026-05-13
**Status**: Accepted

## Context

Wi-Fi 接続プロセスで SSID や Passphrase が無効な場合、
エラーがプラットフォーム層 (WLAN API) まで伝播して非自明なエラーメッセージが返る。

例: 9バイト超の SSID → `ERROR_INVALID_PARAMETER` (Windows WLAN API)
例: 7文字のパスフレーズ → 接続試行後にタイムアウトまで待機

## Decision

### 検証レイヤー

1. **`WifiProfileValidator`** (新設, `MWC.Core.Models` 名前空間)
   - IEEE 802.11-2020 の仕様に準拠した検証ルール
   - `Validate()`: 問題があれば `ArgumentException` を投げる
   - `TryValidate()`: 例外を投げず `bool + errorMessage` を返す
   - `IsValidSsid()`: UI のリアルタイム検証用

2. **`ProfileXmlBuilder.Build()`** でも `WifiProfileValidator.Validate()` を呼ぶ
   - Defense in depth: 呼び出し元が検証を省略しても安全

### 検証ルール

| フィールド | ルール |
|---|---|
| SSID | 1-32 バイト(UTF-8), 制御文字禁止 |
| WPA2/WPA3 Passphrase | 8-63 ASCII (0x20-0x7E), または 64桁16進 raw PSK |
| Open/OWE Passphrase | 不問(無視) |
| Enterprise Passphrase | 不問(EAP 使用) |

### なぜプレゼンテーション層ではなく Core 層に置くか

- CLI / WPF / SDK のどの経路からでも同一ルールが適用される
- テストが書きやすい (WPF のモックが不要)
- `WifiProfileValidator.IsValidSsid()` を WPF の `TextChanged` でも呼べる

## Consequences

- 無効な入力が WLAN API に届く前に明確な例外で検出される
- UI はリアルタイム検証に `IsValidSsid()` を使える
- CLI は `TryValidate()` でユーザーフレンドリーなエラーを表示できる
- 将来の `WifiProfileSpec` フィールド追加時にこのファイルを更新する
