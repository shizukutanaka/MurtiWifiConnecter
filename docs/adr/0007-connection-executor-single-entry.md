# ADR-0007: ConnectionExecutor — 接続の単一エントリポイント

**ステータス**: 採用済み  
**日付**: 2026-04-25  
**決定者**: @shizukutanaka

---

## 背景

v1.0〜v1.6 では `_wifi.ConnectAsync()` が App 層の 4 箇所で直接呼ばれていた。

```
MainWindow → _wifi.ConnectAsync()
AdapterViewModel → _wifi.ConnectAsync()
AutoReconnectService → _wifi.ConnectAsync()
AllAdaptersOverviewView → _wifi.ConnectAsync()
```

これにより:
- 各呼出元が「プロファイル登録 → 接続 → 履歴記録 → ログ出力」を個別に実装
- 一箇所でのバグが他に伝播しない代わりに重複実装が蓄積
- テスト困難(4箇所をモックする必要)

## 決定

`ConnectionExecutor` クラスを `MWC.Core.Services` に設け、**App 層から `_wifi.ConnectAsync/DisconnectAsync/RegisterProfileAsync` を直接呼ぶコードをゼロにする**。

```
Any caller → ConnectionExecutor.ConnectAsync()
                ├─ ProfileXmlBuilder.Build(spec)
                ├─ _wifi.RegisterProfileAsync()
                ├─ _wifi.ConnectAsync()
                ├─ _history.RecordConnection()
                └─ _log.LogInformation()
```

## 結果

- **良い点**: プロファイル登録漏れ、履歴未記録が原理的に不可能
- **良い点**: `FakeWifi` モックで全経路をテスト可能
- **良い点**: `ConfigureAwait(false)` を一箇所に集約
- **トレードオフ**: App 層→Core 層への依存が増加(許容。Core はプラットフォーム非依存)

## 検証

```bash
# App層のwifi直接呼出がゼロであることをCIで保証
grep -rn "_wifi\.\(ConnectAsync\|DisconnectAsync\|RegisterProfileAsync\)" src/MWC.App/
# → 0件 (WifiConnectAuditTests でアサート)
```
