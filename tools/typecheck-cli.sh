#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# tools/typecheck-cli.sh — MWC.Cli を **NuGet 無しで**型検査する。
#
# なぜ必要か:
#   `api.nuget.org` がエグレスポリシーで拒否されている環境では System.CommandLine を
#   入手できず、CLI は一切コンパイルできなかった。そして **CLI にはロジックの大半が
#   ハンドラのラムダ内に置かれている**ため、そこが検査されないと Core API の誤用が
#   まるごと素通りする。
#
#   単に「本物の Core を参照して Cli をコンパイルし、参照欠落エラーを無視する」のは
#   **効かない**。SetHandler のデリゲート型が未解決だと Roslyn はラムダ本体を束縛せず、
#   存在しないメンバ名を書いてもエラーが出ない(実験で確認済み)。
#   そこで tools/stubs/ に **型検査専用スタブ**を置き、デリゲート型を解決させて
#   ラムダ本体を束縛させる。
#
#   2026-08 にこれを実際に走らせたところ、**実在の欠陥 3 件**が出た:
#     1. QualityHistoryCommand.cs — `using MWC.Core.Abstractions/Models` 欠落。
#        using は**ファイル単位**であり、同じ partial class の別ファイルにあっても効かない。
#     2. MultiAdapterCommand.cs — `using MWC.Core.Models` 欠落。`PhyType.ToShortLabel()` は
#        拡張メソッドなので、宣言名前空間を import しないと解決しない (CS1061)。
#     3. Program.cs — GetValueForArgument の戻り値 (T?) を required な
#        `WifiProfileSpec.Ssid` へ代入。CS8601 は TreatWarningsAsErrors=true でエラー。
#
# ★ 信用してよい範囲(スタブを使う以上、ここは厳密に)
#   信用してよい : ハンドラ本体の中身 — Core の API 名・引数・null 許容・BCL 利用。
#                  これらは**本物の MWC.Core.dll と本物の参照アセンブリ**で検査される。
#                  ハーネスに検出力があることは、ラムダ内のメンバ名をわざと壊して
#                  CS0117 が出ることを毎回確認して担保する (--selftest)。
#   信用しては×  : System.CommandLine の面そのもの — SetHandler のアリティ、
#                  オーバーロード解決、GetValueFor* の戻り値の null 許容性。
#                  **スタブは「私の理解」を写しただけ**で、本物と食い違い得る。
#                  ここでのエラー/無エラーを根拠に主張しないこと。
#   検査していない: Platform.Windows の実装(スタブは空)、実行時の挙動。
#
# 使い方: bash tools/typecheck-cli.sh [--selftest]
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
  echo "SKIP: .NET SDK or reference packs not available"
  exit 2
fi

REFS=""
for f in "$NETREF"*.dll; do REFS="$REFS -r:$f"; done
for d in Microsoft.Extensions.Logging.Abstractions Microsoft.Extensions.DependencyInjection.Abstractions \
         Microsoft.Extensions.DependencyInjection Microsoft.Extensions.Logging \
         Microsoft.Extensions.Logging.Console Microsoft.Extensions.Options; do
  [ -f "$ASPREF$d.dll" ] && REFS="$REFS -r:$ASPREF$d.dll"
done

GEN=""
for name in Microsoft.Extensions.Logging.Generators.dll System.Text.RegularExpressions.Generator.dll; do
  g=$(find "$DOTNET_ROOT_DIR/packs" -path '*/analyzers/dotnet/cs/*' -name "$name" 2>/dev/null | sort -V | tail -1)
  [ -n "$g" ] && GEN="$GEN -analyzer:$g"
done

OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT

# 1. 本物の Core をビルドする (CLI が参照する対象は本物でなければ意味がない)
# shellcheck disable=SC2086
dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable -nowarn:CS1591 \
  -out:"$OUT/MWC.Core.dll" $REFS $GEN \
  $(find src/MWC.Core -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*') > "$OUT/core.log" 2>&1
if [ $? -ne 0 ]; then
  echo "MWC.Core does not compile; fix that first (run tools/typecheck-core.sh):"
  head -5 "$OUT/core.log"
  exit 1
fi

compile_cli() {
  # shellcheck disable=SC2086
  dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable \
    -warnaserror -nowarn:CS1591 \
    -out:"$1" $REFS -r:"$OUT/MWC.Core.dll" \
    tools/stubs/*.cs \
    $(find src/MWC.Cli -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*') 2>&1
}

# 2. セルフテスト: ラムダ本体が本当に束縛されているかを毎回確かめる。
#    これを怠ると「エラーが出ない = 検査できている」と誤読する。実際、
#    スタブ導入前は何を壊してもエラーが出ない状態だった。
if [ "$SELFTEST" -eq 1 ]; then
  probe="src/MWC.Cli/PrivacyCommand.cs"
  cp "$probe" "$OUT/probe.bak"
  sed -i 's/MacAddressModeInference\.TryParse/MacAddressModeInference.TryParseZZZ/' "$probe"
  hit=$(compile_cli "$OUT/selftest.dll" | grep -c 'TryParseZZZ')
  cp "$OUT/probe.bak" "$probe"
  if [ "$hit" -eq 0 ]; then
    printf '\033[31mSELFTEST FAILED\033[0m: a deliberately broken member inside a handler lambda was\n'
    printf 'not reported. The harness is blind — do not trust a clean run.\n'
    exit 1
  fi
  printf '\033[32mselftest ok\033[0m (handler bodies are being bound)\n'
fi

# 3. 本番の型検査
output=$(compile_cli "$OUT/cli.dll")
status=$?
[ -n "$output" ] && echo "$output"

if [ $status -eq 0 ]; then
  printf '\033[32mMWC.Cli type-checks clean\033[0m (stubbed CommandLine/Platform; see header for what this does and does not prove)\n'
else
  printf '\033[31mMWC.Cli failed to type-check.\033[0m\n'
fi
exit $status
