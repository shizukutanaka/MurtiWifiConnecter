# ビルド阻害(取込み v2.5.0 ソース)— 静的洗い出しと対応

> 本環境は .NET SDK 無し・外部ネットワーク遮断・WPF/テストは Windows 専用のため
> ローカルでコンパイルできない。以下は **静的解析(grep/パターン照合)で確定**した
> ビルド阻害と対応。最終検証は Windows CI(`ci/github-workflows/`)で行う。

取込んだ v2.5.0 ソースは複数の確定的コンパイルエラーを含んでおり、現状のままでは
ビルドできない(= 元から CI を通っていなかった可能性が高い)。

## 確定・対応済み

| # | 阻害 | 影響 | 対応 | コミット |
|---|---|---|---|---|
| 1 | `MainViewModel.RefreshAllAsync` 二重定義 | MWC.App コンパイル不能 (CS0111) | 2実装を統合 | b485eba |
| 2 | `NetworkHistoryService` が net9.0 専用 `System.Threading.Lock` を使用 | ns2.0 で CS0246 | `object` ロックへ置換 | 5925420 |
| 3 | `Models/WifiNetwork.cs` が `System.Linq` 未 import で `.Any()` 使用 | MWC.Core 全体が不能 | `using` 追加 | 9d570d3 |
| 4 | **ns2.0 で net6+ API 多用**(`Math.Clamp`/`ArgumentNullException.ThrowIfNull`/`Random.Shared`/`.ToHashSet()` 等 約10ファイル) | ns2.0 ビルド不能・ポリフィル不可 | **Core/SDK を net9.0 単一ターゲット化** | (本コミット) |
| 5 | `GroupPolicyProvider` が `Microsoft.Win32.Registry` 使用だが plain net9.0 では in-box でない | Core net9.0 で型解決不能 | `Microsoft.Win32.Registry` を明示参照 | (本コミット) |

### #4 の判断根拠
- `Math.Clamp` は netstandard2.1+ のみ、`ArgumentNullException.ThrowIfNull` /
  `Random.Shared` は net6+、`.ToHashSet()` は netstandard2.1+。いずれも **sealed BCL 型の
  静的メソッド**でありポリフィル(型拡張)不可。call site の全面書き換え(約25箇所)か
  ターゲット撤廃の二択。
- ソリューション内で Core/SDK を ns2.0 として消費するプロジェクトは無し
  (Platform.Android/iOS/Linux は plain net9.0、Windows/App/Cli は net9.0-windows)。
- よって **net9.0 単一化が最小・確実・実態整合**。`Math.Clamp` 等は net9.0 で正常。

## 未検証(CI で要確認)
- 上記以外の重複メンバー・未 import・net-only API は、Core では本走査で追加検出なし。
  ただし **App/Cli/Platform 各層(Windows 専用・本環境でビルド不可)は未走査領域が残る**。
- `Microsoft.Win32.Registry` 5.0.0 は netstandard2.0 アセットを持つため net9.0 で
  NU1701 は出ない見込みだが、CI で確認すること。
- `TreatWarningsAsErrors=true` のため、初回 CI ではアナライザ警告がエラー化し得る。

## 推奨
CI(`ci/github-workflows/*.yml`)を `.github/workflows/` へ設置して net9.0 ビルド+テストを
実走させ、残存エラーを確定的に潰すのが次の最優先。
