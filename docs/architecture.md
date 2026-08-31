# Architecture

## レイヤー構成

```
  MWC.App (WPF)              MWC.Cli (CLI)         プレゼンテーション層
  ├─ ViewModels (MVVM)       ├─ Program.cs
  ├─ Views (XAML)            ├─ AdapterCommand
  ├─ Services                └─ MultiAdapterCmd
  ├─ Controls / Converters
  ├─ Resources (L.cs + 12 resx)
  └─ Themes (Dark/Light)
  ─────────────────────────────────────────────
  MWC.Platform.Windows                             プラットフォーム層
  ├─ WindowsWifiService (ManagedNativeWifi)
  ├─ DpapiSecretProtector
  └─ HttpConnectivityChecker
  ─────────────────────────────────────────────
  MWC.Core                                         ドメイン層
  ├─ Abstractions (IWifiService)
  ├─ Models (WifiNetwork, AuthMethod)
  ├─ Profile (ProfileXmlBuilder, WifiUri)
  └─ Services
     ├─ ConnectionExecutor   (唯一の接続エントリ)
     ├─ AdapterPreferencesService
     ├─ NetworkQualityService / NetworkHistoryService
     ├─ SignalHistoryService / OuiLookupService
     ├─ SecurityBadgeService / ExportService
     └─ TroubleshootingHelper
```

## 接続経路 (ConnectionExecutor)

App層から _wifi.ConnectAsync/DisconnectAsync/RegisterProfileAsync の直接呼出はゼロ。

全接続は ConnectionExecutor 経由:
1. ProfileXmlBuilder.Build(spec) で XML 生成
2. _wifi.RegisterProfileAsync() でプロファイル登録
3. _wifi.ConnectAsync() で実接続
4. _history.RecordConnection() で履歴記録
5. 構造化ログ出力

## DI (31サービス)

App.xaml.cs で全サービスをコンストラクタ注入で解決。重複登録ゼロ。

## i18n (516キー x 14言語 + 中立ベース = 7,740エントリ)

L.cs が型安全アクセサ。L.Get("key") / L.Format("key", args) / L.ActionRefresh 等。
App層コードのハードコード日本語: 0箇所。

## 安全性パターン

- async void: AsyncEventHelper.SafeRunAsync で全例外捕捉
- fire-and-forget: task.Forget(log) で例外をログ
- グローバル: Dispatcher + AppDomain + TaskScheduler の3層捕捉

## テーマ

ThemeService が Dark.xaml/Light.xaml を差替。全XAML は DynamicResource 参照。
Window.Resources にローカルブラシ定義なし。
