# ADR-0009: クロスプラットフォーム戦略

**ステータス**: 採用済み  
**日付**: 2026-05-05

---

## 背景

MWC v2.0 は Windows 専用だったが、v2.4 で Linux / macOS / Android / iOS への展開を決定。

## 決定

**インターフェース分離原則**で `MWC.Core` をプラットフォーム非依存に保つ。

```
MWC.Core (netstandard2.0 + net8.0)
  └─ IWifiService (抽象)

MWC.Platform.Windows  (net8.0-windows)  → ManagedNativeWifi
MWC.Platform.Linux    (net8.0)          → nmcli CLI
MWC.Platform.MacOS    (net8.0-macos)    → airport + networksetup
MWC.Platform.Android  (net8.0)          → WifiManager (MAUI)
MWC.Platform.iOS      (net8.0)          → NEHotspotConfiguration
```

## 依存方向

```
App → Core ← Platform
(Platformが Coreのインターフェースを実装)
```

`MWC.Core` への逆依存は禁止。`Platform` が `Core` の `IWifiService` を実装する。

## netstandard2.0 除外対象

Registry / P/Invoke / X509Store / MEF PluginHost など Windows 固有 API は `net8.0` 専用とし、`netstandard2.0` ターゲットでは `csproj` の `<Compile Remove>` で除外。

## 今後の課題

- Linux 版の `RegisterProfileAsync` は `nmcli connection` 形式で実装済みだが、
  `wpa_supplicant.conf` 直接書き込みの代替も検討
- iOS は `NEHotspotConfiguration` の App Store 審査エンタイトルメントが必要
