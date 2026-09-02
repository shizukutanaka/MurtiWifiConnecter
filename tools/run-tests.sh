#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# tools/run-tests.sh — テストを **NuGet 無しで実際に実行する**。
#
# なぜ在るか:
#   `api.nuget.org` がエグレス拒否のため xunit のランナーが入らず、このリポジトリの
#   テストは**一度も実行されたことがなかった**。しかし
#     - アサーションは tools/stubs/TestFrameworks.Stub.cs が実際に検証し、
#     - [Fact]/[Theory] の反射呼び出しは tools/stubs/MiniRunner.cs が行う
#   ので、xunit 無しでも走らせて合否が出せる。
#
#   2026-08 の初回実行で **1037 件合格 / 実在の欠陥 4 件**が判明した:
#     1. CatImportService — 名前空間なしの CAT ファイルで全プロファイルが**二重**になる
#        (`ns + "X"` と `"X"` が同一クエリになり Concat で倍増)。**製品の不具合**。
#     2. SlnRegistrationTests — 「GUID が 3 回以上出たら重複」は .sln の形式に対して
#        常に偽。プロジェクト GUID は宣言 1 + 構成 4 = 最低 5 回出る。
#     3. EvilTwinDetectorTests — 製品が出さない語 "impersonation" を期待していた。
#     4. HighDensityWifiUriRoundTripTests — WIFI: URI は WPA と WPA2 を区別できないので
#        WPAPSK の完全往復は原理的に不可能だった。
#   いずれも CI の初回で赤くなるもので、型検査では捕まらない **実行時**の欠陥。
#
# 本物の `dotnet test` との差 (必ず承知して使うこと):
#   - 対象は typecheck-tests.sh と同じ範囲 (MWC.App 依存と FsCheck を除く)。
#   - アサーション意味論は近似。BeEquivalentTo は反射による構造比較で、
#     本物の FluentAssertions とは差異があり得る。
#   - 並列実行なし。xunit の collection/fixture は未対応。
#   - **合格は「この近似の下で通った」という意味**であり、`dotnet test` の代わりにならない。
#
# ただし「近似だから無意味」ではない。**検出力は実測してある**:
#   `tools/mutation-check.sh` が製品コードに意図的な欠陥を注入し、失敗が増えるかを見る。
#   2026-08 の測定では実質的な変異 5 件をすべて殺し、コメントだけを変えた対照は生存した。
#   つまりこのスイートは通るだけの張りぼてではなく、実際に意味論を検証している。
#   **主張する前に測ること** — 本サイクルで最も高くついた教訓。
#
# 5 件目の欠陥 (2026-08 に修正済み。上の 4 件と同じくテスト実行で初めて判明した):
#   NetworkHistoryService は保存先を `static readonly` の固定パスで持っていたため、
#   **同一プロセス内の全インスタンスが 1 つのファイルを共有**していた。
#   テストは互いの書き込みを読み、`NetworkHistoryService_ConcurrentWrites_ThreadSafe` が
#   他テストの SSID を拾って落ちていた。xunit はテストクラスを既定で並列実行するので、
#   これは CI で**不定期に落ちる**種類の製品欠陥である (テストの都合ではない)。
#   コンストラクタに `historyPath` を足して注入可能にした。引数を省略した既存の
#   呼び出しは従来どおり動く。
#
# 使い方: bash tools/run-tests.sh [--verbose]
# 終了コード: 0 = 全合格 / 1 = 失敗あり / 2 = SDK 等が無くスキップ
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
cd "$(dirname "$0")/.."

DOTNET_ROOT_DIR=${DOTNET_ROOT:-/usr/lib/dotnet}
SDK_DIR=$(ls -d "$DOTNET_ROOT_DIR"/sdk/*/ 2>/dev/null | sort -V | tail -1)
CSC="${SDK_DIR}Roslyn/bincore/csc.dll"
NETREF=$(ls -d "$DOTNET_ROOT_DIR"/packs/Microsoft.NETCore.App.Ref/*/ref/net*/ 2>/dev/null | sort -V | tail -1)
ASPREF=$(ls -d "$DOTNET_ROOT_DIR"/packs/Microsoft.AspNetCore.App.Ref/*/ref/net*/ 2>/dev/null | sort -V | tail -1)
SHARED=$(ls -d "$DOTNET_ROOT_DIR"/shared/Microsoft.AspNetCore.App/*/ 2>/dev/null | sort -V | tail -1)
RUNTIME_VER=$(basename "$(ls -d "$DOTNET_ROOT_DIR"/shared/Microsoft.NETCore.App/*/ 2>/dev/null | sort -V | tail -1)")

if [ ! -f "$CSC" ] || [ -z "$NETREF" ] || [ -z "$ASPREF" ]; then
  echo "SKIP: .NET SDK or reference packs not available"; exit 2
fi

REFS=""
for f in "$NETREF"*.dll; do REFS="$REFS -r:$f"; done
for d in Microsoft.Extensions.Logging.Abstractions Microsoft.Extensions.DependencyInjection.Abstractions \
         Microsoft.Extensions.DependencyInjection Microsoft.Extensions.Logging Microsoft.Extensions.Options; do
  [ -f "$ASPREF$d.dll" ] && REFS="$REFS -r:$ASPREF$d.dll"
done
GEN=""
for name in Microsoft.Extensions.Logging.Generators.dll System.Text.RegularExpressions.Generator.dll; do
  g=$(find "$DOTNET_ROOT_DIR/packs" -path '*/analyzers/dotnet/cs/*' -name "$name" 2>/dev/null | sort -V | tail -1)
  [ -n "$g" ] && GEN="$GEN -analyzer:$g"
done

# 出力先の深さが重要: RepositoryIntegrityTests / SlnRegistrationTests は
# アセンブリ位置から 5 階層上をリポジトリルートとみなす (通常のビルド出力と同じ深さ)。
# 浅い場所に置くとルートを見つけられず、それらのテストが偽陽性で落ちる。
RUNDIR="artifacts/bin/MiniRunner/Release/net10.0"
mkdir -p "$RUNDIR"

# shellcheck disable=SC2086
dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable -nowarn:CS1591 \
  -out:"$RUNDIR/MWC.Core.dll" $REFS $GEN \
  $(find src/MWC.Core -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*') > /dev/null 2>&1 \
  || { echo "MWC.Core does not compile; run tools/typecheck-core.sh"; exit 1; }

# 対象の選定。除外は **実測に基づく** — 全ファイルでコンパイルして失敗したものだけを外した。
# (以前「テストヘルパーのクラス名衝突」と書いたが**それは誤り**だった。実際の失敗理由は
#  すべて System.Windows.Input / MWC.App.ViewModels への依存で、衝突は 1 件も無い。
#  同じ誤診を繰り返さないよう、除外理由をファイル名とともに明記する。)
#   NetworkDetailViewModelVpnEapWiringTests / OweWiringTests /
#   ProfileManagerViewModelErrorHandlingTests / SignalIconWiringTests … ViewModel 依存
#   QualityImprovementTests … System.Windows.Input (KeyboardShortcutService)
#   PropertyBasedTests … FsCheck
#   FinalValidationV8Tests / OnboardingTests / BugFixRegressionTests … ViewModel / Dialog 依存
#   RefactoringTests / QualityScanV8Tests (中の LocalizationTests 等) … L.cs は .resx をコンパイルして
#     埋め込んだ .resources を必要とする。csc 直叩きでは resgen 相当が無く生成できないため、
#     実行すると MissingManifestResourceException になる (製品の不具合ではない)。
WPF_DEPENDENT="NetworkDetailViewModelVpnEapWiringTests.cs OweWiringTests.cs \
ProfileManagerViewModelErrorHandlingTests.cs QualityImprovementTests.cs SignalIconWiringTests.cs \
FinalValidationV8Tests.cs OnboardingTests.cs BugFixRegressionTests.cs PropertyBasedTests.cs \
RefactoringTests.cs QualityScanV8Tests.cs"

APP_SOURCES=""
for f in $(find src/MWC.App -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -not -name '*.xaml.cs'); do
  # Serilog は tools/stubs/Serilog.Stub.cs が最小限を供給するので除外しない。
  grep -qE 'using System\.Windows|using CommunityToolkit|ObservableProperty|RelayCommand|System\.Windows\.' "$f" \
    || APP_SOURCES="$APP_SOURCES $f"
done

STUBS="tools/stubs/ImplicitUsings.Stub.cs tools/stubs/TestFrameworks.Stub.cs \
tools/stubs/MwcAppNotification.Stub.cs tools/stubs/MwcAppVersion.Stub.cs \
tools/stubs/Serilog.Stub.cs"

FILES=""; SKIPPED=0
for f in $(find tests -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*'); do
  case " $WPF_DEPENDENT " in *" $(basename "$f") "*) SKIPPED=$((SKIPPED+1)) ;; *) FILES="$FILES $f" ;; esac
done

# shellcheck disable=SC2086
dotnet "$CSC" -nologo -nostdlib -target:exe -langversion:12 -nullable:enable -main:MwcMiniRunner.Program \
  -nowarn:CS1591,CS8600,CS8601,CS8602,CS8603,CS8604,CS8620,CS8625 \
  -out:"$RUNDIR/run.dll" $REFS -r:"$RUNDIR/MWC.Core.dll" \
  $STUBS tools/stubs/MiniRunner.cs $APP_SOURCES $FILES
[ $? -eq 0 ] || { echo "tests do not compile; run tools/typecheck-tests.sh"; exit 1; }

cat > "$RUNDIR/run.runtimeconfig.json" <<EOF
{ "runtimeOptions": { "tfm": "net10.0",
  "framework": { "name": "Microsoft.NETCore.App", "version": "${RUNTIME_VER:-10.0.0}" } } }
EOF
[ -n "$SHARED" ] && cp "$SHARED"Microsoft.Extensions.Logging.Abstractions.dll "$RUNDIR/" 2>/dev/null

# 永続化された前回の状態を消す。消さないと EapAuthStatsService 等の件数が積み上がり、
# 製品の不具合と紛らわしい失敗になる (実際に踏んだ)。
rm -rf "${XDG_DATA_HOME:-$HOME/.local/share}/MWC"

echo "running $(echo "$FILES" | wc -w) test files ($SKIPPED skipped: MWC.App-dependent or FsCheck)"
dotnet "$RUNDIR/run.dll" "$@"
