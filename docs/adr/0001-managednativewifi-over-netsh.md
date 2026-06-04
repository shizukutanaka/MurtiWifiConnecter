# ADR-0001: ManagedNativeWifi over netsh.exe

- Status: Accepted
- Date: 2026-04-25
- Deciders: Shizuku Tanaka

## Context

v0.x は `netsh.exe wlan ...` をサブプロセス起動して WiFi 操作を行っていた。問題:

1. **コマンドインジェクション**: SSID に `"` `&` `|` 等を含めると任意コード実行可能
2. **言語依存パース**: `netsh wlan show interfaces` の出力が OS 言語で変化、日本語 Windows で `BSSID` 行と `SSID` 行を誤検出
3. **接続成功判定不能**: ExitCode は「要求受付」を示すのみ、実接続と無関係
4. **WMI `MSNdis_80211_BSSIList` 非推奨**: Win11 24H2 で動作不可

## Decision

`ManagedNativeWifi` 3.x(`emoacht/ManagedNativeWifi`)経由で **WlanAPI 直叩き** に統一。`netsh.exe` および WMI への依存を完全排除。

## Consequences

### 良い影響
- コマンドインジェクション攻撃面の完全消滅
- OS 言語非依存
- `WlanRegisterNotification` で実接続完了を待機可能
- Win10 1809 〜 Win11 24H2+ で同一コードパス
- 起動時間短縮(プロセス起動回数ゼロ)

### 悪い影響
- 外部依存追加(`ManagedNativeWifi` NuGet パッケージ)
  - 緩和: メンテ活発(2025 年も更新)、MIT ライセンス、純 C#
- WlanAPI 学習コスト
  - 緩和: ManagedNativeWifi のラッパーで隠蔽

## Alternatives Considered

| 候補 | 不採用理由 |
|---|---|
| `netsh.exe` 継続 | 致命バグ複数、修正不可能 |
| 自前 P/Invoke | 約 2000 行の実装コスト、保守負担 |
| `wagnerhsu/nuget-NativeWifi` | 最終更新が古い |
| `consp1racy/NativeWifi` | 同上 |
| `managedwifi 1.1.0` | 2012 年最終更新、Win11 未対応 |
