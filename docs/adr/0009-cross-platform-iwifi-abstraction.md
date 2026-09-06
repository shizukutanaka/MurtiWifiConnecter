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

## 2026-09 追記 — Android/iOS は削除済み

上記「決定」に挙げた 5 プラットフォームのうち `MWC.Platform.Android`/`MWC.Platform.iOS`
は 2026-07 に削除された(`docs/FEATURE-AUDIT.md` §1c)。全メソッドが空配列/false/失敗を
返す完全スタブで、App/CLI からの参照はゼロ、`MWC.sln` への登録以外に存在理由が無かった。
CLAUDE.md の Why が「**Windows PC** で複数の無線アダプターを管理する」である以上、
動かない実装を抱えることは実際にはサポートしていない対象を「サポートしている」ように
見せるだけの負債だった。復活させる場合は動作する実装と実機検証をセットにすること
(このセクションを書き換えるのではなく追記に留めるのは、決定当時の背景を残すため)。

`MWC.Platform.Linux`(nmcli)と `MWC.Platform.MacOS`(airport/networksetup)は現存する
— 前者は完全実装、後者は `RegisterProfileAsync` が未実装の半実装プロトタイプ
(詳細は `docs/FEATURE-AUDIT.md` §1c、ファイル自身のコメント参照)。
