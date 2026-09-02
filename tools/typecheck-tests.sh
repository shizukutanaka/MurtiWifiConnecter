#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# tools/typecheck-tests.sh — テストプロジェクトを **NuGet 無しで**型検査する。
#
# なぜ必要か:
#   テストは Core の API を最も広く使う消費者であり、約 900 のテストメソッドが
#   API 名・引数・enum メンバ・レコード構築を「実際に呼んで」いる。ところが
#   xunit / FluentAssertions / NSubstitute は NuGet 経由でしか入らず
#   (`api.nuget.org` はエグレス拒否)、テストプロジェクトはこの環境で
#   **一度もコンパイルされたことがなかった**。
#
#   型検査専用スタブ (tools/stubs/TestFrameworks.Stub.cs) で xunit の属性と
#   FluentAssertions の連鎖を「型が合う程度に」解決させ、本物の MWC.Core.dll に
#   対してテスト本体を束縛させる。2026-08 に初めて走らせたところ、
#   **実在の欠陥 5 件**が出た(いずれも CI の最初の 1 回で赤くなるもの):
#     1. ServicesCoverageTests — 生文字列リテラルの閉じ `"""` が本文と同じ行 (CS9000)
#     2. AdapterPreferencesTests — 外側ラムダの仮引数が `_` のため、内側の `_ = ...` が
#        破棄でなく **int への代入**に束縛された (CS0029)。C# の有名な罠。
#     3. ApplePhase3Tests — `UpdateCheckResult` (MWC.App.Services) の using 欠落
#     4. ValidationAndSecurityTests — `MWC.Core.Services` の using 欠落
#     5. HighDensityScenarioTests — `required` な `BssInfo.Bssid` を 5 箇所で未設定 (CS9035)
#     (+ ServicesTests の `Lookup(null!)` は string/ReadOnlySpan<byte> 間で曖昧になり得るため明示)
#
# ★ 何を検査できて、何を検査していないか(ここを誤解すると危険)
#   検査できる : テスト本体が呼ぶ Core の API 名・引数・型・enum メンバ・レコード構築。
#                アサーションの**外側**。存在しないメンバ名は確実に落ちる (--selftest で担保)。
#   検査しない : **アサーションの意味**。スタブの `Be(object?)` は何でも受けるため、
#                `x.Should().Be("文字列")` のような型の食い違いは通る。
#                「通った = テストが正しい」ではない。「型が合っている」だけ。
#   実行しない : テストは 1 つも走らない。結果は CI (dotnet test) の担当。
#   対象外     : `MWC.App` を参照するテスト (WPF 依存) と FsCheck を使う PropertyBasedTests。
#                件数は実行時に表示する。
#
#   **対象を広げようとして止めた記録** (2026-08。同じ探索を繰り返さないために残す):
#     App 依存テスト 14 件のうち数件は、WPF 非依存の App サービス層 (typecheck-app-services.sh
#     が扱う 6 ファイル) しか使っていない。そこでそれらを本検査に取り込もうとしたが:
#       - 候補を足すと **テストヘルパーのクラス名が衝突**し、エラーが *基礎側* の
#         ファイルに報告される。これを「基礎側が壊れた」と解釈して除外する素朴な
#         反復アルゴリズムは、健全な Core 専用テストまで落としてしまい**収束が不安定**だった。
#       - 得られる増分は 751 → 約 774 メソッド (+2.5%) にすぎない。
#     **不安定な検査は、小さくても安定した検査より悪い** (本サイクルで繰り返し確認した原則)。
#     よって対象は「MWC.App を参照しない」という単純で決定的な規則のまま据え置く。
#     ここを広げたいなら、まず本物の WPF 参照パックか NuGet を用意するのが筋。
#
# 使い方: bash tools/typecheck-tests.sh [--selftest]
# 終了コード: 0 = 成功 / 1 = 型エラー / 2 = SDK 等が無くスキップ
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
cd "$(dirname "$0")/.."

SELFTEST=0
[ "${1:-}" = "--selftest" ] && SELFTEST=1

DOTNET_ROOT_DIR=${DOTNET_ROOT:-/usr/lib/dotnet}
SDK_DIR=$(ls -d "$DOTNET_ROOT_DIR"/sdk/*/ 2>/dev/null | sort -V | tail -1)
CSC="${SDK_DIR}Roslyn/bincore/csc.dll"
NETREF=$(ls -d "$DOTNET_ROOT_DIR"/packs/Microsoft.NETCore.App.Ref/*/ref/net*/ 2>/dev/null | sort -V | tail -1)
ASPREF=$(ls -d "$DOTNET_ROOT_DIR"/packs/Microsoft.AspNetCore.App.Ref/*/ref/net*/ 2>/dev/null | sort -V | tail -1)

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

OUT=$(mktemp -d); trap 'rm -rf "$OUT"' EXIT

# shellcheck disable=SC2086
dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable -nowarn:CS1591 \
  -out:"$OUT/MWC.Core.dll" $REFS $GEN \
  $(find src/MWC.Core -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*') > "$OUT/core.log" 2>&1 \
  || { echo "MWC.Core does not compile; run tools/typecheck-core.sh first"; head -5 "$OUT/core.log"; exit 1; }

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
QualityScanV8Tests.cs"

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

compile_tests() {
  # nullable 警告 (CS86xx) はスタブの annotation を反映するだけなので抑制する。
  # 本物の FluentAssertions では出ない/出るが異なるため、ここで判定材料にはできない。
  # shellcheck disable=SC2086
  dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable \
    -nowarn:CS1591,CS8600,CS8601,CS8602,CS8603,CS8604,CS8620,CS8625 \
    -out:"$1" $REFS -r:"$OUT/MWC.Core.dll" \
    $STUBS $APP_SOURCES $FILES 2>&1
}

if [ "$SELFTEST" -eq 1 ]; then
  probe="tests/MWC.Core.Tests/MacAddressModeInferenceTests.cs"
  cp "$probe" "$OUT/probe.bak"
  sed -i 's/MacModeEvidence\.LocallyAdministeredBitSet/MacModeEvidence.LocallyAdministeredBitSetZZZ/' "$probe"
  hit=$(compile_tests "$OUT/selftest.dll" | grep -c 'LocallyAdministeredBitSetZZZ')
  cp "$OUT/probe.bak" "$probe"
  if [ "$hit" -eq 0 ]; then
    printf '\033[31mSELFTEST FAILED\033[0m: a deliberately wrong enum member inside a test was not reported.\n'
    exit 1
  fi
  printf '\033[32mselftest ok\033[0m (test bodies are bound against the real Core)\n'
fi

output=$(compile_tests "$OUT/tests.dll"); status=$?
[ -n "$output" ] && echo "$output" | grep -E 'error' | head -20

n=$(echo "$FILES" | wc -w)
if [ $status -eq 0 ]; then
  printf '\033[32m%s test files type-check against the real Core\033[0m (%s skipped: MWC.App-dependent or FsCheck; assertions are NOT semantically checked)\n' "$n" "$SKIPPED"
else
  printf '\033[31mtest project failed to type-check.\033[0m\n'
fi
exit $status
