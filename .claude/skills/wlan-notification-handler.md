# Skill: wlan-notification-handler

## 用途
WLAN 接続状態変化の通知を待機するパターン。CLAUDE.md 必須事項
「接続成功は `WlanNotification` の `connection_complete` 受信 + 疎通確認の 2 段」の
前段(通知待機)を実装する際に使う。

## 現状(2026-09 実測。このスキルが最初から必要だった理由)

`src/MWC.Platform.Windows/ConnectionWaiter.cs` と
`NetworkStateChangedEventHandlerBridge.cs` は `NativeWifi.NetworkStateChanged` という
**実在しない static イベント**を購読しようとしており、一度もコンパイルできていなかった
(`docs/COMPLETION-CHECKLIST.md` §5 に詳細と再現手順あり)。この skill が存在しなかった
ことが、その欠陥が長期間気づかれなかった一因——「通知待機の正しい実装パターン」を
指し示す文書が無ければ、間違った API を推測で書いても誰も気づけない。

## 実装場所(未修正。このスキルはこれから直すためのもの)
- `src/MWC.Platform.Windows/ConnectionWaiter.cs`
- `src/MWC.Platform.Windows/NetworkStateChangedEventHandlerBridge.cs`

## 実 API(ManagedNativeWifi 3.0.2。GitHub 実ソースで確認済み — 推測ではない)

`NativeWifi`(static クラス)には `public static event` が **1 つも無い**。
状態変化通知は代わりに **`NativeWifiPlayer`**(構築して使う `IDisposable` の instance
クラス、`Source/ManagedNativeWifi/NativeWifiPlayer.cs`)が公開する **7 種類の
instance イベント**:

```csharp
using var player = new NativeWifiPlayer();   // IDisposable

player.NetworkRefreshed     += (s, e) => { };  // EventHandler
player.AvailabilityChanged  += (s, e) => { };  // EventHandler<AvailabilityChangedEventArgs>
player.InterfaceChanged     += (s, e) => { };  // EventHandler<InterfaceChangedEventArgs>
player.ConnectionChanged    += (s, e) => { };  // EventHandler<ConnectionChangedEventArgs>  ← 主に使う
player.ProfileChanged       += (s, e) => { };  // EventHandler<ProfileChangedEventArgs>
player.RadioStateChanged    += (s, e) => { };  // EventHandler<RadioStateChangedEventArgs>
player.SignalQualityChanged += (s, e) => { };  // EventHandler<SignalQualityChangedEventArgs>
```

各イベント引数の実体は `Source/ManagedNativeWifi/*EventArgs.cs` に実在する
(存在しない型を推測しないこと — 使う前に実ソースで確認する)。

## 必須パターン

### NativeWifiPlayer のライフサイクル
`NativeWifiPlayer` は `IDisposable`。`ConnectionWaiter` の生存期間(1回の接続試行)と
一致させるか、`WindowsWifiService.SubscribeEventsAsync`(`IAsyncEnumerable` を
`yield` し続ける長寿命の購読)のどちらに束縛するかは設計判断——後者なら
`WindowsWifiService` 側でアプリ全体の生存期間、前者なら接続試行ごとに
construct/dispose する必要があり、通知の取りこぼし(construct 前に発生した
遷移)が起きうる。

### 現在の 1 イベント設計から 7 イベントへの対応
`ConnectionWaiter` が判定したい状態(`connected`/`disconnecting`/認証失敗/
未検出)を、7 種のうちどれから再構成するかは実機検証が要る設計判断:
- 接続完了/切断は `ConnectionChanged` が主候補(`ConnectionChangedEventArgs` の
  実フィールドを確認すること)。
- 無線オフ等は `RadioStateChanged`、プロファイル変更由来の遷移は `ProfileChanged`
  も絡みうる。
- 現在の `NetworkStateChangedEventArgs.State`/`.Reason`(自製型、
  `ConnectionWaiter.cs` 内で定義)相当の情報が実イベント引数から本当に
  得られるかを個々に確かめる前提で設計すること。

## 禁止事項
- 存在しない API(`NativeWifi.NetworkStateChanged` 等)を推測で書かない。
  必ず `git clone https://github.com/emoacht/ManagedNativeWifi.git` して
  `Directory.Packages.props` のピン留めバージョンと一致するタグで実ソースを確認する。
- Windows 実機で検証できない状態で「たぶん動く」設計を確定させない
  (この欠陥のクラスは静的解析やモックでは検出できず、実機接続でしか表面化しない)。

## テスト
`NativeWifiPlayer`/実イベントは Windows 実機の WLAN サービスに依存するため、
純粋な単体テストでは検証できない。型検査は `tools/typecheck-platform.sh` が
`tools/stubs/ManagedNativeWifi.Stub.cs`(実ソースから書き写した非循環スタブ)で
可能な範囲まで行う——ただし `ConnectionWaiter.cs`/`NetworkStateChangedEventHandlerBridge.cs`
自体は上記の理由でまだ検査対象外(スタブ側に正しい API を用意していない。
用意する = 設計を決めることそのものなので、先に設計判断が要る)。

## 関連
- `docs/COMPLETION-CHECKLIST.md` §5(発見の経緯・再現手順・次のステップ)
- `docs/FEATURE-AUDIT.md` §1c 相当の記載(半実装プロトタイプとの扱いの違い)
